namespace Manifestor
{
    using System;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.Build;

    internal static class ManifestorProfileMaterializer
    {
        public static void ApplyManifestAndDefines(ManifestProfileSO profile)
        {
            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                throw new InvalidOperationException(validation.message);
            }

            ManifestorIO.SaveManifest(ManifestorIO.ConvertToManifest(profile));
            PlayerSettings.SetScriptingDefineSymbols(GetNamedBuildTarget(profile), GetDefinesString(profile));
        }

        public static bool HasExpectedManifest(ManifestProfileSO profile, out string error)
        {
            var reasons = ManifestorIO.GetGeneratedManifestMismatchReasons(profile);
            error = reasons.Count == 0
                ? string.Empty
                : string.Join("; ", reasons);
            return reasons.Count == 0;
        }

        public static bool HasExpectedDefines(ManifestProfileSO profile, out string error)
        {
            var expectedDefines = GetNormalizedDefines(profile);
            var currentDefines = PlayerSettings.GetScriptingDefineSymbols(GetNamedBuildTarget(profile))
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(define => define.Trim())
                .Where(define => !string.IsNullOrEmpty(define))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(define => define, StringComparer.Ordinal)
                .ToArray();
            if (expectedDefines.SequenceEqual(currentDefines, StringComparer.Ordinal))
            {
                error = string.Empty;
                return true;
            }

            error = $"Expected '{string.Join(";", expectedDefines)}', actual '{string.Join(";", currentDefines)}'.";
            return false;
        }

        public static string GetDefinesString(ManifestProfileSO profile)
        {
            return string.Join(";", GetNormalizedDefines(profile));
        }

        private static string[] GetNormalizedDefines(ManifestProfileSO profile)
        {
            return profile.GetScriptingDefines()
                .Select(define => (define ?? string.Empty).Trim())
                .Where(define => !string.IsNullOrEmpty(define))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(define => define, StringComparer.Ordinal)
                .ToArray();
        }

        private static NamedBuildTarget GetNamedBuildTarget(ManifestProfileSO profile)
        {
            var buildTarget = Build.BuildProfileUtility.GetBuildTarget(profile.buildProfile);
            return NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(buildTarget));
        }
    }
}
