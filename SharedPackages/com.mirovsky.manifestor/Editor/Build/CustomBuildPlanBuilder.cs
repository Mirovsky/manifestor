namespace Manifestor.Build
{
    using System;
    using System.Linq;
    using UnityEditor;

    internal static class CustomBuildPlanBuilder
    {
        public static ManifestorResult TryCreate(
            ManifestProfileSO profile,
            CustomBuildOperation operation,
            string outputFolderPath,
            BuildOptions options,
            out CustomBuildPipelineState state)
        {
            state = null;

            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                return validation;
            }

            if (operation == CustomBuildOperation.Build && string.IsNullOrWhiteSpace(outputFolderPath))
            {
                return ManifestorResult.Error("Build output folder cannot be empty.");
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            var profileGuid = AssetDatabase.AssetPathToGUID(profilePath);
            if (string.IsNullOrEmpty(profilePath) || string.IsNullOrEmpty(profileGuid))
            {
                return ManifestorResult.Error("Manifest profile must be saved as a project asset before building.");
            }

            if (!CustomBuildStepOrderResolver.TryResolve(out var allSteps, out var graphError))
            {
                return ManifestorResult.Error(graphError);
            }

            var orderedSteps = FilterForOperation(allSteps, operation);
            if (orderedSteps.Count == 0)
            {
                return ManifestorResult.Error("No custom build steps are configured to run during apply.");
            }

            try
            {
                var buildPlayerOptions = operation == CustomBuildOperation.Build
                    ? BuildPlayerOptionsFactory.Create(profile, outputFolderPath, options)
                    : default;
                state = new CustomBuildPipelineState
                {
                    isActive = true,
                    status = CustomBuildPipelineStatus.Waiting,
                    operation = operation,
                    message = operation == CustomBuildOperation.Apply
                        ? "Manifest apply queued."
                        : "Custom build queued.",
                    profileGuid = profileGuid,
                    profileFingerprint = ManifestorProfileFingerprint.Calculate(profile),
                    buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions),
                    orderedStepTypeNames = orderedSteps.Select(type => type.AssemblyQualifiedName).ToList(),
                    resumeAfterUtcTicks = DateTime.UtcNow.Ticks
                };
                return ManifestorResult.Ok();
            }
            catch (Exception exception)
            {
                return ManifestorResult.Error($"Failed to create custom build plan: {exception.Message}");
            }
        }

        internal static System.Collections.Generic.List<Type> FilterForOperation(
            System.Collections.Generic.IEnumerable<Type> orderedSteps,
            CustomBuildOperation operation)
        {
            return operation == CustomBuildOperation.Apply
                ? orderedSteps.Where(RunsDuringApply).ToList()
                : orderedSteps.ToList();
        }

        private static bool RunsDuringApply(Type stepType)
        {
            return stepType
                .GetCustomAttributes(typeof(CustomBuildStepAttribute), false)
                .Cast<CustomBuildStepAttribute>()
                .Any(attribute => attribute.runDuringApply);
        }
    }
}
