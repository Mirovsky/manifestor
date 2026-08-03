namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;

    public enum ManifestorBuildStepOrder
    {
        Before,
        After
    }

    public enum ManifestorBuildStepOutcome
    {
        Succeeded,
        Waiting,
        Failed,
        Cancelled
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ManifestorBuildStepAttribute : Attribute
    {
        public Type relativeStepType { get; }
        public ManifestorBuildStepOrder order { get; }
        public bool hasConstraint => relativeStepType != null;
        public bool runDuringApply { get; set; }

        public ManifestorBuildStepAttribute()
        {
        }

        public ManifestorBuildStepAttribute(Type relativeStepType, ManifestorBuildStepOrder order)
        {
            this.relativeStepType = relativeStepType ?? throw new ArgumentNullException(nameof(relativeStepType));
            this.order = order;
        }
    }

    public interface IManifestorBuildStep
    {
        ManifestorBuildStepResult Tick(ManifestorBuildContext context);
    }

    public interface IManifestorBuildStepInterruptionHandler
    {
        ManifestorBuildStepResult HandleInterruption(ManifestorBuildContext context);
    }

    public sealed class ManifestorBuildContext
    {
        private readonly Action<string, BuildPlayerOptions> _saveCheckpoint;
        private readonly Action<IReadOnlyDictionary<string, string>> _saveUserData;
        private readonly Dictionary<string, string> _userData;

        public ManifestProfileSO profile { get; }
        public ManifestorBuildOperation operation { get; }
        public bool cancellationRequested { get; }
        public string persistedState { get; private set; }
        public BuildPlayerOptions buildPlayerOptions { get; set; }

        internal ManifestorBuildContext(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            BuildPlayerOptions buildPlayerOptions,
            bool cancellationRequested,
            string persistedState,
            Action<string, BuildPlayerOptions> saveCheckpoint)
            : this(
                profile,
                operation,
                buildPlayerOptions,
                cancellationRequested,
                persistedState,
                saveCheckpoint,
                null,
                null)
        {
        }

        internal ManifestorBuildContext(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            BuildPlayerOptions buildPlayerOptions,
            bool cancellationRequested,
            string persistedState,
            Action<string, BuildPlayerOptions> saveCheckpoint,
            IReadOnlyDictionary<string, string> userData,
            Action<IReadOnlyDictionary<string, string>> saveUserData)
        {
            this.profile = profile;
            this.operation = operation;
            this.buildPlayerOptions = buildPlayerOptions;
            this.cancellationRequested = cancellationRequested;
            this.persistedState = persistedState ?? string.Empty;
            _saveCheckpoint = saveCheckpoint;
            _saveUserData = saveUserData;
            _userData = userData == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(userData, StringComparer.Ordinal);
        }

        public void SaveCheckpoint(string state)
        {
            persistedState = state ?? string.Empty;
            _saveCheckpoint?.Invoke(persistedState, buildPlayerOptions);
        }

        public void SetUserData(string key, string value)
        {
            ValidateUserDataKey(key);
            _userData[key] = value ?? string.Empty;
            _saveUserData?.Invoke(_userData);
        }

        public bool TryGetUserData(string key, out string value)
        {
            ValidateUserDataKey(key);
            return _userData.TryGetValue(key, out value);
        }

        public bool RemoveUserData(string key)
        {
            ValidateUserDataKey(key);
            if (!_userData.Remove(key))
            {
                return false;
            }

            _saveUserData?.Invoke(_userData);
            return true;
        }

        private static void ValidateUserDataKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("User-data key cannot be empty.", nameof(key));
            }
        }
    }

    public readonly struct ManifestorBuildStepResult
    {
        public readonly ManifestorBuildStepOutcome outcome;
        public readonly string message;
        public readonly double retryAfterSeconds;

        public bool success => outcome == ManifestorBuildStepOutcome.Succeeded;

        private ManifestorBuildStepResult(
            ManifestorBuildStepOutcome outcome,
            string message,
            double retryAfterSeconds = 0d)
        {
            this.outcome = outcome;
            this.message = message ?? string.Empty;
            this.retryAfterSeconds = retryAfterSeconds;
        }

        public static ManifestorBuildStepResult Succeeded(string message = null)
        {
            return new ManifestorBuildStepResult(ManifestorBuildStepOutcome.Succeeded, message);
        }

        public static ManifestorBuildStepResult Failed(string message)
        {
            return new ManifestorBuildStepResult(ManifestorBuildStepOutcome.Failed, message);
        }

        public static ManifestorBuildStepResult Waiting(string message = null, double retryAfterSeconds = 1d)
        {
            if (retryAfterSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(retryAfterSeconds));
            }

            return new ManifestorBuildStepResult(ManifestorBuildStepOutcome.Waiting, message, retryAfterSeconds);
        }

        public static ManifestorBuildStepResult Cancelled(string message = null)
        {
            return new ManifestorBuildStepResult(ManifestorBuildStepOutcome.Cancelled, message);
        }
    }

    internal static class ManifestorBuildStepOrderResolver
    {
        public static bool TryResolve(out List<Type> orderedSteps, out string error)
        {
            return TryResolve(
                TypeCache.GetTypesWithAttribute<ManifestorBuildStepAttribute>(),
                out orderedSteps,
                out error);
        }

        internal static bool TryResolve(
            IEnumerable<Type> discoveredTypes,
            out List<Type> orderedSteps,
            out string error)
        {
            orderedSteps = new List<Type>();
            error = string.Empty;

            var stepTypes = discoveredTypes
                .Distinct()
                .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal)
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
                    .GetCustomAttributes(typeof(ManifestorBuildStepAttribute), false)
                    .Cast<ManifestorBuildStepAttribute>();
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
                        continue;
                    }

                    var before = attribute.order == ManifestorBuildStepOrder.Before ? stepType : relativeType;
                    var after = attribute.order == ManifestorBuildStepOrder.Before ? relativeType : stepType;
                    if (outgoingEdges[before].Add(after))
                    {
                        incomingCounts[after]++;
                    }
                }
            }

            while (orderedSteps.Count < stepTypes.Count)
            {
                var nextStep = stepTypes.FirstOrDefault(
                    type => incomingCounts[type] == 0 && !processedSteps.Contains(type));
                if (nextStep == null)
                {
                    error = "Custom build step ordering contains a dependency cycle.";
                    return false;
                }

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

            if (!typeof(IManifestorBuildStep).IsAssignableFrom(stepType))
            {
                error = $"Custom build step '{stepType.FullName}' must implement {nameof(IManifestorBuildStep)}.";
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
    }
}
