namespace Mirov.Manifestor.Editor
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
            var success = TryCreateExecutionOrder(out var steps, out error, out _);
            orderedSteps = steps.AsReadOnly();
            return success;
        }

        public static ManifestorResult Apply(ManifestProfileSO profile)
        {
            return Start(profile, CustomBuildOperation.Apply, string.Empty, BuildOptions.None);
        }

        public static ManifestorResult Build(
            ManifestProfileSO profile,
            string outputDirectoryPath,
            BuildOptions options = BuildOptions.None)
        {
            return Start(profile, CustomBuildOperation.Build, outputDirectoryPath, options);
        }

        private static ManifestorResult Start(
            ManifestProfileSO profile,
            CustomBuildOperation operation,
            string outputDirectoryPath,
            BuildOptions options)
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

            if (operation == CustomBuildOperation.Build && string.IsNullOrWhiteSpace(outputDirectoryPath))
            {
                return ManifestorResult.Error("Build output directory cannot be empty.");
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(profilePath))
            {
                return ManifestorResult.Error("Manifest profile must be saved as a project asset before building.");
            }

            if (!TryCreateExecutionOrder(out var orderedSteps, out var graphError, out var isAmbiguous))
            {
                if (isAmbiguous)
                {
                    Debug.LogWarning(graphError);
                }

                return ManifestorResult.Error(graphError);
            }

            if (operation == CustomBuildOperation.Apply)
            {
                orderedSteps = orderedSteps.Where(RunsDuringApply).ToList();
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
                outputDirectoryPath = outputDirectoryPath,
                options = (int)options,
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
            var context = new CustomBuildContext(profile, state.outputDirectoryPath, (BuildOptions)state.options);
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
            state.outputDirectoryPath = context.outputDirectoryPath;
            state.options = (int)context.options;
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

        private static bool TryCreateExecutionOrder(
            out List<Type> orderedSteps,
            out string error,
            out bool isAmbiguous)
        {
            orderedSteps = new List<Type>();
            error = string.Empty;
            isAmbiguous = false;

            var stepTypes = TypeCache.GetTypesWithAttribute<CustomBuildStepAttribute>()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();
            if (stepTypes.Count == 0)
            {
                error = "No custom build steps were discovered.";
                return false;
            }

            var stepTypeSet = new HashSet<Type>(stepTypes);
            var outgoingEdges = stepTypes.ToDictionary(type => type, _ => new HashSet<Type>());
            var incomingCounts = stepTypes.ToDictionary(type => type, _ => 0);
            var processedSteps = new HashSet<Type>();

            foreach (var stepType in stepTypes)
            {
                if (!TryValidateStepType(stepType, out error))
                {
                    return false;
                }

                var attributes = stepType
                    .GetCustomAttributes(typeof(CustomBuildStepAttribute), false)
                    .Cast<CustomBuildStepAttribute>();
                foreach (var attribute in attributes)
                {
                    if (!attribute.hasConstraint)
                    {
                        continue;
                    }

                    var relativeType = attribute.relativeStepType;
                    if (relativeType == stepType)
                    {
                        error = $"Build step '{stepType.FullName}' cannot be ordered relative to itself.";
                        return false;
                    }

                    if (!stepTypeSet.Contains(relativeType))
                    {
                        error = $"Build step '{stepType.FullName}' references undiscovered step '{relativeType.FullName}'.";
                        return false;
                    }

                    var before = attribute.order == CustomBuildStepOrder.Before ? stepType : relativeType;
                    var after = attribute.order == CustomBuildStepOrder.Before ? relativeType : stepType;
                    if (outgoingEdges[before].Add(after))
                    {
                        incomingCounts[after]++;
                    }
                }
            }

            while (orderedSteps.Count < stepTypes.Count)
            {
                var availableSteps = stepTypes
                    .Where(type => incomingCounts[type] == 0 && !processedSteps.Contains(type))
                    .ToList();
                if (availableSteps.Count == 0)
                {
                    error = "Custom build step ordering contains a dependency cycle.";
                    return false;
                }

                if (availableSteps.Count > 1)
                {
                    isAmbiguous = true;
                    error = "Custom build step ordering is ambiguous. Add explicit ordering between: " +
                            string.Join(", ", availableSteps.Select(type => type.FullName));
                    return false;
                }

                var nextStep = availableSteps[0];
                orderedSteps.Add(nextStep);
                processedSteps.Add(nextStep);
                foreach (var dependentStep in outgoingEdges[nextStep])
                {
                    incomingCounts[dependentStep]--;
                }
            }

            return true;
        }

        private static bool TryValidateStepType(Type stepType, out string error)
        {
            if (!stepType.IsClass || stepType.IsAbstract || stepType.ContainsGenericParameters)
            {
                error = $"Custom build step '{stepType.FullName}' must be a concrete, non-generic class.";
                return false;
            }

            if (!typeof(ICustomBuildStep).IsAssignableFrom(stepType))
            {
                error = $"Custom build step '{stepType.FullName}' must implement {nameof(ICustomBuildStep)}.";
                return false;
            }

            if (stepType.GetConstructor(Type.EmptyTypes) == null)
            {
                error = $"Custom build step '{stepType.FullName}' must have a public parameterless constructor.";
                return false;
            }

            error = string.Empty;
            return true;
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

            if (terminalStatus == CustomBuildPipelineStatus.Succeeded)
            {
                ManifestorEditorPrefs.SetLastAppliedProfileFingerprint(state.profileFingerprint);
            }

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
            public string outputDirectoryPath;
            public int options;
            public List<string> orderedStepTypeNames = new();
            public int nextStepIndex;
            public string currentStepTypeName;
            public long resumeAfterUtcTicks;
        }
    }
}
