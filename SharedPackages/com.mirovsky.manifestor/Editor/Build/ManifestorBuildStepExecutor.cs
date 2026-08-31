namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    internal static class ManifestorBuildStepExecutor
    {
        public static ManifestorBuildStepResult Execute(Type stepType, ManifestorBuildContext context)
        {
            try
            {
                var step = (IManifestorBuildStep)Activator.CreateInstance(stepType);
                return step.Tick(context);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                return ManifestorBuildStepResult.Failed(
                    $"Build step '{stepType.FullName}' threw an exception: {exception.Message}");
            }
        }

        public static ManifestorBuildStepResult RunCategorySynchronously(
            ManifestProfileSO profile,
            ManifestorBuildStepCategory category,
            ManifestorBuildStepTargets targets,
            BuildPlayerOptions buildPlayerOptions,
            IReadOnlyDictionary<string, string> userData,
            out BuildPlayerOptions updatedBuildPlayerOptions,
            out IReadOnlyDictionary<string, string> updatedUserData)
        {
            updatedBuildPlayerOptions = buildPlayerOptions;
            updatedUserData = userData ?? new Dictionary<string, string>();
            if (!ManifestorBuildStepOrderResolver.TryResolve(targets, out var orderedSteps, out var orderError))
            {
                return ManifestorBuildStepResult.Failed(orderError);
            }

            var expectedFingerprint = ManifestorProfileFingerprint.Calculate(profile);
            var context = new ManifestorBuildContext(
                profile,
                category == ManifestorBuildStepCategory.Apply
                    ? ManifestorBuildOperation.Apply
                    : ManifestorBuildOperation.Build,
                buildPlayerOptions,
                false,
                string.Empty,
                null,
                userData,
                null);
            foreach (var stepType in ManifestorBuildPlanBuilder.FilterForCategory(orderedSteps, category))
            {
                if (!string.Equals(
                        ManifestorProfileFingerprint.Calculate(profile),
                        expectedFingerprint,
                        StringComparison.Ordinal))
                {
                    return ManifestorBuildStepResult.Failed(
                        $"Manifest profile changed before build step '{stepType.FullName}'.");
                }

                var result = Execute(stepType, context);
                if (result.outcome == ManifestorBuildStepOutcome.Waiting)
                {
                    return ManifestorBuildStepResult.Failed(
                        $"Build step '{stepType.FullName}' returned Waiting in a synchronous build hook.");
                }

                if (!result.success)
                {
                    return result;
                }
            }

            if (!string.Equals(
                    ManifestorProfileFingerprint.Calculate(profile),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return ManifestorBuildStepResult.Failed(
                    $"Manifest profile changed while running {category} steps.");
            }

            updatedBuildPlayerOptions = context.buildPlayerOptions;
            updatedUserData = new Dictionary<string, string>(context.userData, StringComparer.Ordinal);
            return ManifestorBuildStepResult.Succeeded();
        }
    }
}
