namespace Manifestor.Build
{
    using System;
    using UnityEditor;
    using UnityEngine;

    internal sealed class ManifestorBuildRunner
    {
        private readonly Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> _completed;
        private bool _isQueued;
        private long _nextResumeAfterUtcTicks;

        public bool isActive => ManifestorBuildPipelineStateStore.Load().isActive;

        public ManifestorBuildRunner(Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> completed)
        {
            _completed = completed;
        }

        public void Queue()
        {
            if (_isQueued)
            {
                return;
            }

            _isQueued = true;
            EditorApplication.update += Process;
        }

        private void Stop()
        {
            if (!_isQueued)
            {
                return;
            }

            EditorApplication.update -= Process;
            _isQueued = false;
            _nextResumeAfterUtcTicks = 0;
        }

        public void Restore()
        {
            var state = ManifestorBuildPipelineStateStore.Load();
            ManifestorBuildProgress.Restore(state);
            ManifestorBuildPipelineStateStore.Save(state);
            _nextResumeAfterUtcTicks = state.resumeAfterUtcTicks;
            if (!state.isActive)
            {
                return;
            }

            if (state.status != ManifestorBuildPipelineStatus.Running)
            {
                Queue();
                return;
            }

            var recoveryMessage = TryHandleInterruption(state, out var handlerMessage)
                ? $" Cleanup completed: {handlerMessage}"
                : string.IsNullOrEmpty(handlerMessage)
                    ? string.Empty
                    : $" Cleanup failed: {handlerMessage}";
            Complete(
                state,
                ManifestorBuildPipelineStatus.Failed,
                $"Custom build was interrupted while running {GetCurrentActionName(state)} and was not retried.{recoveryMessage}");
        }

        private bool TryHandleInterruption(ManifestorBuildPipelineState state, out string message)
        {
            message = string.Empty;

            var action = GetCurrentAction(state);
            if (action?.kind != ManifestorBuildActionKind.Step)
            {
                return false;
            }

            var stepType = Type.GetType(action.stepTypeName ?? string.Empty);
            if (stepType == null ||
                !typeof(IManifestorBuildStepInterruptionHandler).IsAssignableFrom(stepType))
            {
                return false;
            }

            var profilePath = AssetDatabase.GUIDToAssetPath(state.profileGuid);
            var profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(profilePath);
            if (profile == null)
            {
                message = $"Manifest profile with GUID '{state.profileGuid}' could not be loaded.";
                return false;
            }

            try
            {
                var context = CreateContext(state, profile, true, true);
                var handler = (IManifestorBuildStepInterruptionHandler)Activator.CreateInstance(stepType);
                var result = handler.HandleInterruption(context);
                message = result.message;
                return result.outcome is ManifestorBuildStepOutcome.Succeeded or ManifestorBuildStepOutcome.Cancelled;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                message = exception.Message;
                return false;
            }
        }

        public ManifestorResult Start(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            string outputFolderPath,
            BuildOptions options,
            ManifestorBuildStepTargets targets)
        {
            return Start(profile, operation, outputFolderPath, options, targets, null);
        }

        public ManifestorResult Start(ManifestProfileSO profile, BuildPlayerOptions buildPlayerOptions)
        {
            return Start(
                profile,
                ManifestorBuildOperation.Build,
                string.Empty,
                BuildOptions.None,
                ManifestorBuildStepTargets.Standard,
                buildPlayerOptions);
        }

        private ManifestorResult Start(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            string outputFolderPath,
            BuildOptions options,
            ManifestorBuildStepTargets targets,
            BuildPlayerOptions? initialBuildPlayerOptions)
        {
            var currentState = ManifestorBuildPipelineStateStore.Load();
            if (currentState.isActive || BuildPipeline.isBuildingPlayer)
            {
                return ManifestorResult.Error("A custom build is already in progress.");
            }

            ManifestorBuildPipelineState state;
            var planResult = initialBuildPlayerOptions.HasValue
                ? ManifestorBuildExecution.TryCreatePlan(
                    profile, initialBuildPlayerOptions.Value, out state)
                : ManifestorBuildExecution.TryCreatePlan(
                    profile, operation, outputFolderPath, options, targets, out state);
            if (!planResult.success)
            {
                return planResult;
            }

            ManifestorBuildProgress.Start(state);
            ManifestorBuildPipelineStateStore.Save(state);
            _nextResumeAfterUtcTicks = state.resumeAfterUtcTicks;
            Queue();
            return ManifestorResult.Ok();
        }

        public ManifestorResult Cancel()
        {
            var state = ManifestorBuildPipelineStateStore.Load();
            if (!state.isActive)
            {
                return ManifestorResult.Error("No custom build is in progress.");
            }

            state.cancellationRequested = true;
            state.message = "Custom build cancellation requested.";
            state.resumeAfterUtcTicks = DateTime.UtcNow.Ticks;
            ReportAndSaveActiveState(state);
            Queue();
            return ManifestorResult.Ok();
        }

        public void Tick()
        {
            var state = ManifestorBuildPipelineStateStore.Load();
            if (!state.isActive)
            {
                Stop();
                return;
            }

            _nextResumeAfterUtcTicks = state.resumeAfterUtcTicks;
            if (DateTime.UtcNow.Ticks < state.resumeAfterUtcTicks)
            {
                return;
            }

            if (state.cancellationRequested && !state.currentActionStarted)
            {
                Complete(state, ManifestorBuildPipelineStatus.Cancelled, "Custom build was cancelled.");
                return;
            }

            if (state.actions == null || state.nextActionIndex < 0)
            {
                Complete(state, ManifestorBuildPipelineStatus.Failed, "The persisted build action list is invalid.");
                return;
            }

            if (state.nextActionIndex >= state.actions.Count)
            {
                Complete(
                    state,
                    ManifestorBuildPipelineStatus.Succeeded,
                    state.operation == ManifestorBuildOperation.Apply
                        ? "Manifest apply completed successfully."
                        : "Custom build completed successfully.");
                return;
            }

            var action = state.actions[state.nextActionIndex];
            if (action == null)
            {
                Complete(
                    state,
                    ManifestorBuildPipelineStatus.Failed,
                    $"Build action at index {state.nextActionIndex} is missing.");
                return;
            }

            var profilePath = AssetDatabase.GUIDToAssetPath(state.profileGuid);
            var profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(profilePath);
            if (profile == null)
            {
                Complete(state, ManifestorBuildPipelineStatus.Failed,
                    $"Manifest profile with GUID '{state.profileGuid}' could not be loaded.");
                return;
            }

            try
            {
                var currentFingerprint = ManifestorProfileFingerprint.Calculate(profile);
                if (!string.Equals(currentFingerprint, state.profileFingerprint, StringComparison.Ordinal))
                {
                    Complete(state, ManifestorBuildPipelineStatus.Failed,
                        $"Manifest profile '{profilePath}' changed while the custom build was running.");
                    return;
                }
            }
            catch (Exception exception)
            {
                Complete(state, ManifestorBuildPipelineStatus.Failed,
                    $"Failed to validate manifest profile before the next build action: {exception.Message}");
                return;
            }

            if (action.kind == ManifestorBuildActionKind.PlayerBuild)
            {
                RunPlayerBuild(state, profile);
                return;
            }

            if (action.kind != ManifestorBuildActionKind.Step)
            {
                Complete(
                    state,
                    ManifestorBuildPipelineStatus.Failed,
                    $"Build action at index {state.nextActionIndex} has an unsupported kind '{action.kind}'.");
                return;
            }

            var stepType = Type.GetType(action.stepTypeName ?? string.Empty);
            if (stepType == null)
            {
                Complete(
                    state,
                    ManifestorBuildPipelineStatus.Failed,
                    $"Build step type '{action.stepTypeName}' could not be loaded.");
                return;
            }

            state.status = ManifestorBuildPipelineStatus.Running;
            state.currentActionStarted = true;
            state.message = $"Running build step '{stepType.FullName}'.";

            ReportAndSaveActiveState(state);

            var context = CreateContext(state, profile, state.cancellationRequested, true);

            var result = ManifestorBuildExecution.ExecuteStep(stepType, context);

            state.buildPlayerOptions = SerializableBuildPlayerOptions.From(context.buildPlayerOptions);
            state.stepState = context.persistedState;

            if (result.outcome == ManifestorBuildStepOutcome.Waiting)
            {
                state.status = ManifestorBuildPipelineStatus.Waiting;
                state.message = string.IsNullOrEmpty(result.message)
                    ? $"Build step '{stepType.FullName}' is waiting."
                    : result.message;
                state.resumeAfterUtcTicks = DateTime.UtcNow.AddSeconds(result.retryAfterSeconds).Ticks;
                ReportAndSaveActiveState(state);
                return;
            }

            if (!result.success)
            {
                Complete(
                    state,
                    result.outcome == ManifestorBuildStepOutcome.Cancelled
                        ? ManifestorBuildPipelineStatus.Cancelled
                        : ManifestorBuildPipelineStatus.Failed,
                    CreateStepMessage(stepType, result.message));
                return;
            }

            if (state.cancellationRequested)
            {
                Complete(state, ManifestorBuildPipelineStatus.Cancelled, "Custom build was cancelled.");
                return;
            }

            state.nextActionIndex++;
            state.currentActionStarted = false;
            state.stepState = string.Empty;
            state.status = ManifestorBuildPipelineStatus.Waiting;
            state.message = string.IsNullOrEmpty(result.message)
                ? $"Build step '{stepType.FullName}' completed."
                : result.message;
            state.resumeAfterUtcTicks = DateTime.UtcNow.Ticks;
            ReportAndSaveActiveState(state);
        }

        private void RunPlayerBuild(ManifestorBuildPipelineState state, ManifestProfileSO profile)
        {
            state.status = ManifestorBuildPipelineStatus.Running;
            state.currentActionStarted = true;
            state.message = "Building the Unity player.";
            ReportAndSaveActiveState(state);

            var context = CreateContext(state, profile, state.cancellationRequested, false);
            var preparationResult = ManifestorPlayerBuild.Prepare(context, state.targets);
            state.buildPlayerOptions = SerializableBuildPlayerOptions.From(context.buildPlayerOptions);
            if (!preparationResult.success)
            {
                Complete(
                    state,
                    preparationResult.outcome == ManifestorBuildStepOutcome.Cancelled
                        ? ManifestorBuildPipelineStatus.Cancelled
                        : ManifestorBuildPipelineStatus.Failed,
                    string.IsNullOrEmpty(preparationResult.message)
                        ? "Player build preparation failed."
                        : preparationResult.message);
                return;
            }

            var result = ManifestorPlayerBuild.Build(context);
            state.buildPlayerOptions = SerializableBuildPlayerOptions.From(context.buildPlayerOptions);
            if (!result.success)
            {
                Complete(
                    state,
                    result.outcome == ManifestorBuildStepOutcome.Cancelled
                        ? ManifestorBuildPipelineStatus.Cancelled
                        : ManifestorBuildPipelineStatus.Failed,
                    string.IsNullOrEmpty(result.message) ? "The Unity player build failed." : result.message);
                return;
            }

            state.nextActionIndex++;
            state.currentActionStarted = false;
            state.status = ManifestorBuildPipelineStatus.Waiting;
            state.message = result.message;
            state.resumeAfterUtcTicks = DateTime.UtcNow.Ticks;
            ReportAndSaveActiveState(state);
        }

        private void ReportAndSaveActiveState(ManifestorBuildPipelineState state)
        {
            ManifestorBuildProgress.Report(state);
            ManifestorBuildPipelineStateStore.Save(state);
            _nextResumeAfterUtcTicks = state.resumeAfterUtcTicks;
        }

        private void Complete(
            ManifestorBuildPipelineState state,
            ManifestorBuildPipelineStatus terminalStatus,
            string message)
        {
            state.isActive = false;
            state.status = terminalStatus;
            state.message = message;
            state.currentActionStarted = false;
            state.stepState = string.Empty;
            ManifestorBuildProgress.Finish(state, terminalStatus);
            ManifestorBuildPipelineStateStore.Save(state);
            Stop();

            switch (terminalStatus)
            {
                case ManifestorBuildPipelineStatus.Succeeded:
                    Debug.Log(message);
                    break;
                case ManifestorBuildPipelineStatus.Cancelled:
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.LogError(message);
                    break;
            }

            _completed?.Invoke(state.operation, terminalStatus);

            state.userData = new SerializableBuildUserData();
            ManifestorBuildPipelineStateStore.Save(state);
        }

        private static ManifestorBuildAction GetCurrentAction(ManifestorBuildPipelineState state)
        {
            return state?.actions != null &&
                   state.nextActionIndex >= 0 &&
                   state.nextActionIndex < state.actions.Count
                ? state.actions[state.nextActionIndex]
                : null;
        }

        private static string GetCurrentActionName(ManifestorBuildPipelineState state)
        {
            var action = GetCurrentAction(state);
            if (action?.kind == ManifestorBuildActionKind.PlayerBuild)
            {
                return "the Unity player build";
            }

            var stepType = Type.GetType(action?.stepTypeName ?? string.Empty);
            return stepType == null
                ? "an unknown build action"
                : $"build step '{stepType.FullName}'";
        }

        private static string CreateStepMessage(Type stepType, string message)
        {
            return string.IsNullOrEmpty(message)
                ? $"Build step '{stepType.FullName}' did not complete successfully."
                : $"Build step '{stepType.FullName}' did not complete successfully: {message}";
        }

        private static ManifestorBuildContext CreateContext(
            ManifestorBuildPipelineState state,
            ManifestProfileSO profile,
            bool cancellationRequested,
            bool persistCheckpoint)
        {
            Action<string, BuildPlayerOptions> saveCheckpoint = null;
            if (persistCheckpoint)
            {
                saveCheckpoint = (stepState, buildPlayerOptions) =>
                {
                    state.stepState = stepState;
                    state.buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions);
                    ManifestorBuildPipelineStateStore.Save(state);
                };
            }

            return new ManifestorBuildContext(
                profile,
                state.operation,
                state.buildPlayerOptions?.ToBuildPlayerOptions() ?? default,
                cancellationRequested,
                persistCheckpoint ? state.stepState : string.Empty,
                saveCheckpoint,
                state.userData?.ToDictionary(),
                userData =>
                {
                    state.userData = SerializableBuildUserData.From(userData);
                    ManifestorBuildPipelineStateStore.Save(state);
                });
        }

        private void Process()
        {
            if (DateTime.UtcNow.Ticks < _nextResumeAfterUtcTicks ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            Tick();
        }
    }

    internal static class ManifestorBuildProgress
    {
        private const int InvalidProgressId = -1;

        public static void Restore(ManifestorBuildPipelineState state)
        {
            if (state == null || !state.isActive)
            {
                RemoveStaleProgress(state);
                return;
            }

            try
            {
                if (state.progressId == InvalidProgressId || !Progress.Exists(state.progressId))
                {
                    state.progressId = Create(state);
                }
                else
                {
                    RegisterCancellation(state.progressId);
                }

                Report(state.progressId, state);
            }
            catch (Exception exception)
            {
                HandleFailure("restore", state, exception);
            }
        }

        public static void Start(ManifestorBuildPipelineState state)
        {
            if (state == null || !state.isActive)
            {
                return;
            }

            try
            {
                RemoveStaleProgress(state);
                state.progressId = Create(state);
                Report(state.progressId, state);
            }
            catch (Exception exception)
            {
                HandleFailure("start", state, exception);
            }
        }

        public static void Report(ManifestorBuildPipelineState state)
        {
            if (state == null || !state.isActive)
            {
                return;
            }

            try
            {
                if (state.progressId == InvalidProgressId || !Progress.Exists(state.progressId))
                {
                    state.progressId = Create(state);
                }

                Report(state.progressId, state);
            }
            catch (Exception exception)
            {
                HandleFailure("update", state, exception);
            }
        }

        public static void Finish(
            ManifestorBuildPipelineState state,
            ManifestorBuildPipelineStatus terminalStatus)
        {
            try
            {
                if (state != null && state.progressId != InvalidProgressId && Progress.Exists(state.progressId))
                {
                    var totalSteps = GetTotalSteps(state);
                    Progress.Report(state.progressId, totalSteps, totalSteps, state.message ?? string.Empty);
                    Progress.Finish(state.progressId, ToProgressStatus(terminalStatus));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Manifestor could not finish the build progress item: {exception.Message}");
            }
            finally
            {
                if (state != null)
                {
                    state.progressId = InvalidProgressId;
                }
            }
        }

        private static int Create(ManifestorBuildPipelineState state)
        {
            var progressId = Progress.Start(
                GetTitle(state),
                state.message ?? string.Empty,
                Progress.Options.Unmanaged | Progress.Options.Synchronous);
            Progress.SetPriority(progressId, Progress.Priority.Normal);
            Progress.SetStepLabel(progressId, "Build steps");
            RegisterCancellation(progressId);
            return progressId;
        }

        private static void RegisterCancellation(int progressId)
        {
            Progress.UnregisterCancelCallback(progressId);
            Progress.RegisterCancelCallback(progressId, RequestCancellation);
        }

        private static bool RequestCancellation()
        {
            return ManifestorUnityEditorPipeline.Cancel().success;
        }

        private static void Report(int progressId, ManifestorBuildPipelineState state)
        {
            var totalSteps = GetTotalSteps(state);
            var completedSteps = Math.Max(0, Math.Min(state.nextActionIndex, totalSteps));
            Progress.Report(progressId, completedSteps, totalSteps, state.message ?? string.Empty);
        }

        private static int GetTotalSteps(ManifestorBuildPipelineState state)
        {
            return Math.Max(1, state?.actions?.Count ?? 0);
        }

        private static string GetTitle(ManifestorBuildPipelineState state)
        {
            var operationName = state.operation switch
            {
                ManifestorBuildOperation.Apply => "Apply",
                _ => "Build"
            };
            var profilePath = AssetDatabase.GUIDToAssetPath(state.profileGuid);
            var profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(profilePath);
            return profile == null
                ? $"Manifestor {operationName}"
                : $"Manifestor {operationName}: {profile.profileName}";
        }

        private static Progress.Status ToProgressStatus(ManifestorBuildPipelineStatus status)
        {
            return status switch
            {
                ManifestorBuildPipelineStatus.Succeeded => Progress.Status.Succeeded,
                ManifestorBuildPipelineStatus.Cancelled => Progress.Status.Canceled,
                _ => Progress.Status.Failed
            };
        }

        private static void RemoveStaleProgress(ManifestorBuildPipelineState state)
        {
            if (state != null && state.progressId != InvalidProgressId && Progress.Exists(state.progressId))
            {
                Progress.Remove(state.progressId, forceSynchronous: true);
            }

            if (state != null)
            {
                state.progressId = InvalidProgressId;
            }
        }

        private static void HandleFailure(
            string operation,
            ManifestorBuildPipelineState state,
            Exception exception)
        {
            try
            {
                if (state != null && state.progressId != InvalidProgressId && Progress.Exists(state.progressId))
                {
                    Progress.Remove(state.progressId, forceSynchronous: true);
                }
            }
            catch
            {
                // Progress UI failures must not affect the build pipeline.
            }
            finally
            {
                if (state != null)
                {
                    state.progressId = InvalidProgressId;
                }
            }

            Debug.LogWarning($"Manifestor could not {operation} the build progress item: {exception.Message}");
        }
    }
}
