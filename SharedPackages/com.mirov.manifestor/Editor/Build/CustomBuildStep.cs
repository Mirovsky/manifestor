namespace Mirov.Manifestor.Editor
{
    using System;
    using UnityEditor;

    public enum CustomBuildStepOrder
    {
        Before,
        After
    }

    public enum CustomBuildStepOutcome
    {
        Succeeded,
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
        public ManifestProfileSO profile { get; set; }
        public string outputDirectoryPath { get; set; }
        public BuildOptions options { get; set; }

        public CustomBuildContext(ManifestProfileSO profile, string outputDirectoryPath, BuildOptions options)
        {
            this.profile = profile;
            this.outputDirectoryPath = outputDirectoryPath;
            this.options = options;
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

        public static CustomBuildStepResult Cancelled(string message = null)
        {
            return new CustomBuildStepResult(CustomBuildStepOutcome.Cancelled, message);
        }
    }
}
