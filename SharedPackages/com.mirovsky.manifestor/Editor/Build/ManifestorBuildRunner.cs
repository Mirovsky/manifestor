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

            if (state.orderedStepTypeNames == null || state.nextStepIndex >= state.orderedStepTypeNames.Count)
            {
                Complete(
                    state,
                    ManifestorBuildPipelineStatus.Succeeded,
                    state.operation == ManifestorBuildOperation.Apply
                        ? "Manifest apply completed successfully."
                        : "Custom build completed successfully.");
                return;
            }

            var stepTypeName = state.orderedStepTypeNames[state.nextStepIndex];
            var stepType = Type.GetType(stepTypeName);
            if (stepType == null)
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
                    $"Failed to validate manifest profile before step '{stepType.FullName}': {exception.Message}");
                return;
            }

            state.status = ManifestorBuildPipelineStatus.Running;
            state.currentStepTypeName = stepTypeName;
            state.message = $"Running build step '{stepType.FullName}'.";

            ManifestorBuildPipelineStateStore.Save(state);

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
                });

            ManifestorBuildStepResult result;
            try
            {
                var step = (IManifestorBuildStep)Activator.CreateInstance(stepType);
                result = step.Tick(context);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                result = ManifestorBuildStepResult.Failed(
                    $"Build step '{stepType.FullName}' threw an exception: {exception.Message}");
            }

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
        }

        private static string CreateStepMessage(Type stepType, string message)
        {
            return string.IsNullOrEmpty(message)
                ? $"Build step '{stepType.FullName}' did not complete successfully."
                : $"Build step '{stepType.FullName}' did not complete successfully: {message}";
        }
    }
}
