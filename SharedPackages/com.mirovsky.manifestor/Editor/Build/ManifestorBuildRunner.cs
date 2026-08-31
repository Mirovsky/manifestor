namespace Manifestor.Build
{
    using System;
    using UnityEditor;
    using UnityEngine;

    internal sealed class ManifestorBuildRunner
    {
        private readonly Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> _completed;

        public static bool isActive => ManifestorBuildPipelineStateStore.Load().isActive;

        public ManifestorBuildRunner(Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> completed)
        {
            _completed = completed;
        }

        public bool Restore()
        {
            var state = ManifestorBuildPipelineStateStore.Load();
            ManifestorBuildProgress.Restore(state);
            if (!state.isActive)
            {
                return false;
            }

            if (state.status != ManifestorBuildPipelineStatus.Running)
            {
                return true;
            }

            var recoveryMessage = TryHandleInterruption(state, out var handlerMessage)
                ? $" Cleanup completed: {handlerMessage}"
                : string.IsNullOrEmpty(handlerMessage)
                    ? string.Empty
                    : $" Cleanup failed: {handlerMessage}";
            Complete(
                state,
                ManifestorBuildPipelineStatus.Failed,
                $"Custom build was interrupted while running step '{state.currentStepTypeName}' and was not retried.{recoveryMessage}");

            return false;
        }

        private bool TryHandleInterruption(ManifestorBuildPipelineState state, out string message)
        {
            message = string.Empty;

            var stepType = Type.GetType(state.currentStepTypeName ?? string.Empty);
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
                var context = new ManifestorBuildContext(
                    profile,
                    state.operation,
                    state.buildPlayerOptions?.ToBuildPlayerOptions() ?? default,
                    true,
                    state.stepState,
                    (stepState, buildPlayerOptions) =>
                    {
                        state.stepState = stepState;
                        state.buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions);
                        ManifestorBuildPipelineStateStore.Save(state);
                    },
                    state.userData?.ToDictionary(),
                    userData =>
                    {
                        state.userData = SerializableBuildUserData.From(userData);
                        ManifestorBuildPipelineStateStore.Save(state);
                    });
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

        public ManifestorResult Start(ManifestorBuildPipelineState state)
        {
            var currentState = ManifestorBuildPipelineStateStore.Load();
            if (currentState.isActive || BuildPipeline.isBuildingPlayer)
            {
                return ManifestorResult.Error("A custom build is already in progress.");
            }

            ManifestorBuildPipelineStateStore.Save(state);
            ManifestorBuildProgress.Start(state);
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
            ManifestorBuildPipelineStateStore.Save(state);
            ManifestorBuildProgress.Report(state);
            return ManifestorResult.Ok();
        }

        public void Tick()
        {
            var state = ManifestorBuildPipelineStateStore.Load();
            if (!state.isActive || DateTime.UtcNow.Ticks < state.resumeAfterUtcTicks)
            {
                return;
            }

            if (state.cancellationRequested && string.IsNullOrEmpty(state.currentStepTypeName))
            {
                Complete(state, ManifestorBuildPipelineStatus.Cancelled, "Custom build was cancelled.");
                return;
            }

            var hasRemainingSteps = state.orderedStepTypeNames != null &&
                                    state.nextStepIndex < state.orderedStepTypeNames.Count;
            var stepTypeName = hasRemainingSteps
                ? state.orderedStepTypeNames[state.nextStepIndex]
                : string.Empty;
            var stepType = hasRemainingSteps ? Type.GetType(stepTypeName) : null;
            if (hasRemainingSteps && stepType == null)
            {
                Complete(state, ManifestorBuildPipelineStatus.Failed, $"Build step type '{stepTypeName}' could not be loaded.");
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

            if (ShouldBuildPlayer(state, stepType))
            {
                RunPlayerBuild(state, profile);
                return;
            }

            if (!hasRemainingSteps)
            {
                Complete(
                    state,
                    ManifestorBuildPipelineStatus.Succeeded,
                    state.operation == ManifestorBuildOperation.Apply
                        ? "Manifest apply completed successfully."
                        : "Custom build completed successfully.");
                return;
            }

            state.status = ManifestorBuildPipelineStatus.Running;
            state.currentStepTypeName = stepTypeName;
            state.message = $"Running build step '{stepType.FullName}'.";

            ManifestorBuildPipelineStateStore.Save(state);
            ManifestorBuildProgress.Report(state);

            var context = new ManifestorBuildContext(
                profile,
                state.operation,
                state.buildPlayerOptions?.ToBuildPlayerOptions() ?? default,
                state.cancellationRequested,
                state.stepState,
                (stepState, buildPlayerOptions) =>
                {
                    state.stepState = stepState;
                    state.buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions);
                    ManifestorBuildPipelineStateStore.Save(state);
                },
                state.userData?.ToDictionary(),
                userData =>
                {
                    state.userData = SerializableBuildUserData.From(userData);
                    ManifestorBuildPipelineStateStore.Save(state);
                });

            var result = ManifestorBuildStepExecutor.Execute(stepType, context);

            state.buildPlayerOptions = SerializableBuildPlayerOptions.From(context.buildPlayerOptions);
            state.stepState = context.persistedState;

            if (result.outcome == ManifestorBuildStepOutcome.Waiting)
            {
                state.status = ManifestorBuildPipelineStatus.Waiting;
                state.message = string.IsNullOrEmpty(result.message)
                    ? $"Build step '{stepType.FullName}' is waiting."
                    : result.message;
                state.resumeAfterUtcTicks = DateTime.UtcNow.AddSeconds(result.retryAfterSeconds).Ticks;
                ManifestorBuildPipelineStateStore.Save(state);
                ManifestorBuildProgress.Report(state);
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

            state.nextStepIndex++;
            state.currentStepTypeName = string.Empty;
            state.stepState = string.Empty;
            state.status = ManifestorBuildPipelineStatus.Waiting;
            state.message = string.IsNullOrEmpty(result.message)
                ? $"Build step '{stepType.FullName}' completed."
                : result.message;
            state.resumeAfterUtcTicks = DateTime.UtcNow.Ticks;
            ManifestorBuildPipelineStateStore.Save(state);
            ManifestorBuildProgress.Report(state);
        }

        private static bool ShouldBuildPlayer(ManifestorBuildPipelineState state, Type nextStepType)
        {
            return state.operation == ManifestorBuildOperation.Build &&
                   !state.playerBuildCompleted &&
                   (nextStepType == null ||
                    ManifestorBuildStepOrderResolver.GetCategory(nextStepType) == ManifestorBuildStepCategory.PostBuild);
        }

        private void RunPlayerBuild(ManifestorBuildPipelineState state, ManifestProfileSO profile)
        {
            state.status = ManifestorBuildPipelineStatus.Running;
            state.currentStepTypeName = typeof(ManifestorPlayerBuilder).AssemblyQualifiedName;
            state.message = "Building the Unity player.";
            ManifestorBuildPipelineStateStore.Save(state);
            ManifestorBuildProgress.Report(state);

            var context = new ManifestorBuildContext(
                profile,
                state.operation,
                state.buildPlayerOptions?.ToBuildPlayerOptions() ?? default,
                state.cancellationRequested,
                string.Empty,
                null,
                state.userData?.ToDictionary(),
                userData =>
                {
                    state.userData = SerializableBuildUserData.From(userData);
                    ManifestorBuildPipelineStateStore.Save(state);
                });
            var preparationResult = ManifestorPlayerBuildPreparation.Prepare(context, state.targets);
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

            var result = ManifestorPlayerBuilder.Build(context);
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

            state.playerBuildCompleted = true;
            state.currentStepTypeName = string.Empty;
            state.status = ManifestorBuildPipelineStatus.Waiting;
            state.message = result.message;
            state.resumeAfterUtcTicks = DateTime.UtcNow.Ticks;
            ManifestorBuildPipelineStateStore.Save(state);
            ManifestorBuildProgress.Report(state);
        }

        private void Complete(
            ManifestorBuildPipelineState state,
            ManifestorBuildPipelineStatus terminalStatus,
            string message)
        {
            state.isActive = false;
            state.status = terminalStatus;
            state.message = message;
            state.currentStepTypeName = string.Empty;
            state.stepState = string.Empty;
            ManifestorBuildPipelineStateStore.Save(state);
            ManifestorBuildProgress.Finish(state, terminalStatus);
            ManifestorBuildScheduler.Stop();

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

        private static string CreateStepMessage(Type stepType, string message)
        {
            return string.IsNullOrEmpty(message)
                ? $"Build step '{stepType.FullName}' did not complete successfully."
                : $"Build step '{stepType.FullName}' did not complete successfully: {message}";
        }
    }

    internal static class ManifestorBuildProgress
    {
        private const string ProgressIdKey = "Manifestor.UnityEditorPipeline.ProgressId";
        private const int InvalidProgressId = -1;

        public static void Restore(ManifestorBuildPipelineState state)
        {
            if (state == null || !state.isActive)
            {
                RemoveStaleProgress();
                return;
            }

            try
            {
                var progressId = GetProgressId();
                if (progressId == InvalidProgressId || !Progress.Exists(progressId))
                {
                    progressId = Create(state);
                }
                else
                {
                    RegisterCancellation(progressId);
                }

                Report(progressId, state);
            }
            catch (Exception exception)
            {
                HandleFailure("restore", exception);
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
                RemoveStaleProgress();
                var progressId = Create(state);
                Report(progressId, state);
            }
            catch (Exception exception)
            {
                HandleFailure("start", exception);
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
                var progressId = GetProgressId();
                if (progressId == InvalidProgressId || !Progress.Exists(progressId))
                {
                    progressId = Create(state);
                }

                Report(progressId, state);
            }
            catch (Exception exception)
            {
                HandleFailure("update", exception);
            }
        }

        public static void Finish(
            ManifestorBuildPipelineState state,
            ManifestorBuildPipelineStatus terminalStatus)
        {
            try
            {
                var progressId = GetProgressId();
                if (progressId != InvalidProgressId && Progress.Exists(progressId))
                {
                    var totalSteps = GetTotalSteps(state);
                    Progress.Report(progressId, totalSteps, totalSteps, state?.message ?? string.Empty);
                    Progress.Finish(progressId, ToProgressStatus(terminalStatus));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Manifestor could not finish the build progress item: {exception.Message}");
            }
            finally
            {
                SessionState.EraseInt(ProgressIdKey);
            }
        }

        private static int Create(ManifestorBuildPipelineState state)
        {
            var progressId = Progress.Start(
                GetTitle(state),
                state.message ?? string.Empty,
                Progress.Options.Unmanaged | Progress.Options.Synchronous,
                InvalidProgressId);
            SessionState.SetInt(ProgressIdKey, progressId);
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
            var completedActions = state.nextStepIndex + (state.playerBuildCompleted ? 1 : 0);
            var completedSteps = Math.Max(0, Math.Min(completedActions, totalSteps));
            Progress.Report(progressId, completedSteps, totalSteps, state.message ?? string.Empty);
        }

        private static int GetTotalSteps(ManifestorBuildPipelineState state)
        {
            var stepCount = state?.orderedStepTypeNames?.Count ?? 0;
            if (state?.operation == ManifestorBuildOperation.Build)
            {
                stepCount++;
            }

            return Math.Max(1, stepCount);
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

        private static int GetProgressId()
        {
            return SessionState.GetInt(ProgressIdKey, InvalidProgressId);
        }

        private static void RemoveStaleProgress()
        {
            var progressId = GetProgressId();
            if (progressId != InvalidProgressId && Progress.Exists(progressId))
            {
                Progress.Remove(progressId, forceSynchronous: true);
            }

            SessionState.EraseInt(ProgressIdKey);
        }

        private static void HandleFailure(string operation, Exception exception)
        {
            var progressId = GetProgressId();
            try
            {
                if (progressId != InvalidProgressId && Progress.Exists(progressId))
                {
                    Progress.Remove(progressId, forceSynchronous: true);
                }
            }
            catch
            {
                // Progress UI failures must not affect the build pipeline.
            }
            finally
            {
                SessionState.EraseInt(ProgressIdKey);
            }

            Debug.LogWarning($"Manifestor could not {operation} the build progress item: {exception.Message}");
        }
    }
}
