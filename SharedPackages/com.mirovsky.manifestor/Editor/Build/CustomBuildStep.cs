namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;

    public enum CustomBuildStepOrder
    {
        Before,
        After
    }

    public enum CustomBuildStepOutcome
    {
        Succeeded,
        Waiting,
        Failed,
        Cancelled
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class CustomBuildStepAttribute : Attribute
    {
        public Type relativeStepType { get; }
        public CustomBuildStepOrder order { get; }
        public bool hasConstraint => relativeStepType != null;
        public bool runDuringApply { get; set; }

        public CustomBuildStepAttribute()
        {
        }

        public CustomBuildStepAttribute(Type relativeStepType, CustomBuildStepOrder order)
        {
            this.relativeStepType = relativeStepType ?? throw new ArgumentNullException(nameof(relativeStepType));
            this.order = order;
        }
    }

    public interface ICustomBuildStep
    {
        CustomBuildStepResult Execute(CustomBuildContext context);
    }

    public sealed class CustomBuildContext
    {
        public ManifestProfileSO profile { get; }
        public BuildPlayerOptions buildPlayerOptions { get; set; }

        public CustomBuildContext(ManifestProfileSO profile, BuildPlayerOptions buildPlayerOptions)
        {
            this.profile = profile;
            this.buildPlayerOptions = buildPlayerOptions;
        }
    }

    public readonly struct CustomBuildStepResult
    {
        public readonly CustomBuildStepOutcome outcome;
        public readonly string message;

        public bool success => outcome == CustomBuildStepOutcome.Succeeded;

        private CustomBuildStepResult(CustomBuildStepOutcome outcome, string message)
        {
            this.outcome = outcome;
            this.message = message ?? string.Empty;
        }

        public static CustomBuildStepResult Succeeded(string message = null)
        {
            return new CustomBuildStepResult(CustomBuildStepOutcome.Succeeded, message);
        }

        public static CustomBuildStepResult Failed(string message)
        {
            return new CustomBuildStepResult(CustomBuildStepOutcome.Failed, message);
        }

        public static CustomBuildStepResult Waiting(string message = null)
        {
            return new CustomBuildStepResult(CustomBuildStepOutcome.Waiting, message);
        }

        public static CustomBuildStepResult Cancelled(string message = null)
        {
            return new CustomBuildStepResult(CustomBuildStepOutcome.Cancelled, message);
        }
    }

    internal static class CustomBuildStepOrderResolver
    {
        public static bool TryResolve(
            Func<Type, bool> includeStep,
            out List<Type> orderedSteps,
            out string error)
        {
            orderedSteps = new List<Type>();
            error = string.Empty;

            var stepTypes = TypeCache.GetTypesWithAttribute<CustomBuildStepAttribute>()
                .Where(type => includeStep == null || includeStep(type))
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
                        continue;
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
    }
}
