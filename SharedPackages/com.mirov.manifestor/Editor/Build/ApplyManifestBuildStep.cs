namespace Mirov.Manifestor.Editor
{
    using System;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.Build;

    [CustomBuildStep(runDuringApply = true)]
    public sealed class ApplyManifestBuildStep : ICustomBuildStep
    {
        public CustomBuildStepResult Execute(CustomBuildContext context)
        {
            if (context?.profile == null)
            {
                return CustomBuildStepResult.Failed("Manifest profile is required.");
            }

            var result = ManifestorApplicator.Apply(context.profile);
            if (!result.success)
            {
                return CustomBuildStepResult.Failed(result.message);
            }

            try
            {
                ApplyPersistentScriptingDefines(context.profile);
                return CustomBuildStepResult.Succeeded(
                    $"Applied manifest profile '{context.profile.profileName}'.");
            }
            catch (Exception exception)
            {
                return CustomBuildStepResult.Failed(
                    $"Manifest profile was applied, but its scripting defines could not be applied: {exception.Message}");
            }
        }

        private static void ApplyPersistentScriptingDefines(ManifestProfileSO profile)
        {
            var buildTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
            var existingDefines = NormalizeScriptingDefines(
                PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget).Split(';'));
            var profileDefines = NormalizeScriptingDefines(
                profile.packagesLists.SelectMany(packageList => packageList.defines ?? Array.Empty<string>()));
            var mergedDefines = existingDefines
                .Concat(profileDefines)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (existingDefines.SequenceEqual(mergedDefines, StringComparer.Ordinal))
            {
                return;
            }

            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", mergedDefines));
        }

        private static string[] NormalizeScriptingDefines(System.Collections.Generic.IEnumerable<string> defines)
        {
            return (defines ?? Array.Empty<string>())
                .Select(define => (define ?? string.Empty).Trim())
                .Where(define => !string.IsNullOrEmpty(define))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
