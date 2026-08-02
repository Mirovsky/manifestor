namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;

    public enum CustomBuildPipelineStatus
    {
        Idle,
        Waiting,
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum CustomBuildOperation
    {
        Apply,
        Build
    }

    [InitializeOnLoad]
    public static class CustomBuildPipeline
    {
        private const string StateKey = "Mirov.Manifestor.CustomBuildPipeline.State";
        private const long StepDelayTicks = TimeSpan.TicksPerSecond;

        private static bool _isUpdateQueued;

        public static bool isActive => LoadState().isActive;

        public static event Action<CustomBuildOperation, CustomBuildPipelineStatus> completed;

        static CustomBuildPipeline()
        {
            var state = LoadState();
            if (state.isActive)
            {
                QueueUpdate();
            }
        }

        public static bool TryGetOrderedSteps(out IReadOnlyList<Type> orderedSteps, out string error)
        {
            var success = CustomBuildStepOrderResolver.TryResolve(includeStep: null, out var steps, out error);
            orderedSteps = steps.AsReadOnly();
            return success;
        }

        public static ManifestorResult Apply(ManifestProfileSO profile)
        {
            return Start(profile, CustomBuildOperation.Apply, default);
        }

        public static ManifestorResult Build(
            ManifestProfileSO profile,
            BuildPlayerOptions buildPlayerOptions = default)
        {
            return Start(profile, CustomBuildOperation.Build, buildPlayerOptions);
        }

        private static ManifestorResult Start(
            ManifestProfileSO profile,
            CustomBuildOperation operation,
            BuildPlayerOptions buildPlayerOptions)
        {
            var currentState = LoadState();
            if (currentState.isActive || BuildPipeline.isBuildingPlayer)
            {
                return ManifestorResult.Error("A custom build is already in progress.");
            }

            var profileValidation = ManifestorProfileValidator.Validate(profile);
            if (!profileValidation.success)
            {
                return profileValidation;
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(profilePath))
            {
                return ManifestorResult.Error("Manifest profile must be saved as a project asset before building.");
            }

            Func<Type, bool> includeStep = operation == CustomBuildOperation.Apply ? RunsDuringApply : null;
            if (!CustomBuildStepOrderResolver.TryResolve(includeStep, out var orderedSteps, out var graphError))
            {
                return ManifestorResult.Error(graphError);
            }

            if (operation == CustomBuildOperation.Apply)
            {
                if (orderedSteps.Count == 0)
                {
                    return ManifestorResult.Error("No custom build steps are configured to run during apply.");
                }
            }

            string profileFingerprint;
            try
            {
                profileFingerprint = ManifestorProfileFingerprint.Calculate(profile);
            }
            catch (Exception exception)
            {
                return ManifestorResult.Error($"Failed to calculate manifest profile fingerprint: {exception.Message}");
            }

            var state = new PipelineState
            {
                isActive = true,
                status = CustomBuildPipelineStatus.Waiting,
                operation = operation,
                message = operation == CustomBuildOperation.Apply
                    ? "Manifest apply queued."
                    : "Custom build queued.",
                profilePath = profilePath,
                profileFingerprint = profileFingerprint,
                buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions),
                orderedStepTypeNames = orderedSteps.Select(type => type.AssemblyQualifiedName).ToList(),
                nextStepIndex = 0,
                resumeAfterUtcTicks = DateTime.UtcNow.Ticks
            };

            SaveState(state);
            QueueUpdate();
            return ManifestorResult.Ok();
        }

        private static void QueueUpdate()
        {
            if (_isUpdateQueued)
            {
                return;
            }

            _isUpdateQueued = true;
            EditorApplication.update += ProcessNextStep;
        }

        private static void ProcessNextStep()
        {
            var state = LoadState();
            if (!state.isActive)
            {
                StopUpdating();
                return;
            }

            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                BuildPipeline.isBuildingPlayer ||
                DateTime.UtcNow.Ticks < state.resumeAfterUtcTicks)
            {
                return;
            }

            StopUpdating();

            if (state.orderedStepTypeNames == null || state.nextStepIndex >= state.orderedStepTypeNames.Count)
            {
                var message = state.operation == CustomBuildOperation.Apply
                    ? "Manifest apply completed successfully."
                    : "Custom build completed successfully.";
                Complete(state, CustomBuildPipelineStatus.Succeeded, message);
                return;
            }

            var stepTypeName = state.orderedStepTypeNames[state.nextStepIndex];
            var stepType = Type.GetType(stepTypeName);
            if (stepType == null)
            {
                Complete(state, CustomBuildPipelineStatus.Failed, $"Build step type '{stepTypeName}' could not be loaded.");
                return;
            }

            var profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(state.profilePath);
            if (profile == null)
            {
                Complete(state, CustomBuildPipelineStatus.Failed, $"Manifest profile could not be loaded at '{state.profilePath}'.");
                return;
            }

            state.status = CustomBuildPipelineStatus.Running;
            state.currentStepTypeName = stepTypeName;
            state.message = $"Running build step '{stepType.FullName}'.";
            SaveState(state);

            CustomBuildStepResult result;
            var context = new CustomBuildContext(
                profile,
                state.buildPlayerOptions?.ToBuildPlayerOptions() ?? default);
            try
            {
                var step = (ICustomBuildStep)Activator.CreateInstance(stepType);
                result = step.Execute(context);
            }
            catch (Exception exception)
            {
                result = CustomBuildStepResult.Failed(
                    $"Build step '{stepType.FullName}' threw an exception: {exception.Message}");
            }

            if (result.outcome == CustomBuildStepOutcome.Waiting)
            {
                state.buildPlayerOptions = SerializableBuildPlayerOptions.From(context.buildPlayerOptions);
                state.status = CustomBuildPipelineStatus.Waiting;
                state.message = string.IsNullOrEmpty(result.message)
                    ? $"Build step '{stepType.FullName}' is waiting."
                    : result.message;
                state.currentStepTypeName = string.Empty;
                state.resumeAfterUtcTicks = DateTime.UtcNow.Ticks + StepDelayTicks;
                SaveState(state);
                QueueUpdate();
                return;
            }

            if (!result.success)
            {
                var terminalStatus = result.outcome == CustomBuildStepOutcome.Cancelled
                    ? CustomBuildPipelineStatus.Cancelled
                    : CustomBuildPipelineStatus.Failed;
                Complete(state, terminalStatus, CreateStepMessage(stepType, result.message));
                return;
            }

            var updatedProfilePath = AssetDatabase.GetAssetPath(context.profile);
            if (string.IsNullOrEmpty(updatedProfilePath))
            {
                Complete(state, CustomBuildPipelineStatus.Failed,
                    $"Build step '{stepType.FullName}' left the context without a saved manifest profile.");
                return;
            }

            state.profilePath = updatedProfilePath;
            state.buildPlayerOptions = SerializableBuildPlayerOptions.From(context.buildPlayerOptions);
            state.nextStepIndex++;
            state.currentStepTypeName = string.Empty;
            state.status = CustomBuildPipelineStatus.Waiting;
            state.message = string.IsNullOrEmpty(result.message)
                ? $"Build step '{stepType.FullName}' completed."
                : result.message;
            state.resumeAfterUtcTicks = DateTime.UtcNow.Ticks + StepDelayTicks;
            SaveState(state);
            QueueUpdate();
        }

        private static bool RunsDuringApply(Type stepType)
        {
            return stepType
                .GetCustomAttributes(typeof(CustomBuildStepAttribute), false)
                .Cast<CustomBuildStepAttribute>()
                .Any(attribute => attribute.runDuringApply);
        }

        private static void Complete(
            PipelineState state,
            CustomBuildPipelineStatus terminalStatus,
            string message)
        {
            state.isActive = false;
            state.status = terminalStatus;
            state.message = message;
            state.currentStepTypeName = string.Empty;

            SaveState(state);
            StopUpdating();

            if (terminalStatus == CustomBuildPipelineStatus.Succeeded)
            {
                Debug.Log(message);
            }
            else if (terminalStatus == CustomBuildPipelineStatus.Cancelled)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.LogError(message);
            }

            completed?.Invoke(state.operation, terminalStatus);
        }

        private static string CreateStepMessage(Type stepType, string message)
        {
            return string.IsNullOrEmpty(message)
                ? $"Build step '{stepType.FullName}' did not complete successfully."
                : $"Build step '{stepType.FullName}' did not complete successfully: {message}";
        }

        private static void StopUpdating()
        {
            if (!_isUpdateQueued)
            {
                return;
            }

            EditorApplication.update -= ProcessNextStep;
            _isUpdateQueued = false;
        }

        private static PipelineState LoadState()
        {
            var json = SessionState.GetString(StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new PipelineState();
            }

            try
            {
                return JsonUtility.FromJson<PipelineState>(json) ?? new PipelineState();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to restore custom build state: {exception.Message}");
                SessionState.EraseString(StateKey);
                return new PipelineState
                {
                    status = CustomBuildPipelineStatus.Failed,
                    message = "Failed to restore custom build state."
                };
            }
        }

        private static void SaveState(PipelineState state)
        {
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        [Serializable]
        private sealed class PipelineState
        {
            public bool isActive;
            public CustomBuildPipelineStatus status;
            public CustomBuildOperation operation;
            public string message;
            public string profilePath;
            public string profileFingerprint;
            public SerializableBuildPlayerOptions buildPlayerOptions = new();
            public List<string> orderedStepTypeNames = new();
            public int nextStepIndex;
            public string currentStepTypeName;
            public long resumeAfterUtcTicks;
        }

        [Serializable]
        private sealed class SerializableBuildPlayerOptions
        {
            public string[] scenes;
            public string locationPathName;
            public string assetBundleManifestPath;
            public int targetGroup;
            public int target;
            public int subtarget;
            public int options;
            public string[] extraScriptingDefines;

            public static SerializableBuildPlayerOptions From(BuildPlayerOptions buildPlayerOptions)
            {
                return new SerializableBuildPlayerOptions
                {
                    scenes = buildPlayerOptions.scenes,
                    locationPathName = buildPlayerOptions.locationPathName,
                    assetBundleManifestPath = buildPlayerOptions.assetBundleManifestPath,
                    targetGroup = (int)buildPlayerOptions.targetGroup,
                    target = (int)buildPlayerOptions.target,
                    subtarget = buildPlayerOptions.subtarget,
                    options = (int)buildPlayerOptions.options,
                    extraScriptingDefines = buildPlayerOptions.extraScriptingDefines
                };
            }

            public BuildPlayerOptions ToBuildPlayerOptions()
            {
                return new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = locationPathName,
                    assetBundleManifestPath = assetBundleManifestPath,
                    targetGroup = (BuildTargetGroup)targetGroup,
                    target = (BuildTarget)target,
                    subtarget = subtarget,
                    options = (BuildOptions)options,
                    extraScriptingDefines = extraScriptingDefines
                };
            }
        }
    }
}
