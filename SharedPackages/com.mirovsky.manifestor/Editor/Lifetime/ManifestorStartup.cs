using System;
using UnityEditor;
using UnityEngine;

namespace Manifestor
{
    using Build;

    public static class ManifestorStartup
    {
        [InitializeOnLoadMethod]
        private static void QueueStartupApply()
        {
            EditorApplication.delayCall += ApplySavedProfileIfChanged;
        }

        private static void ApplySavedProfileIfChanged()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueStartupApply();
                return;
            }

            if (CustomBuildPipeline.isActive)
            {
                return;
            }

            if (!ManifestorEditorPrefs.TryGetLastAppliedProfilePath(out var profilePath))
            {
                return;
            }

            var profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(profilePath);
            if (profile == null)
            {
                Debug.LogWarning($"Manifestor startup profile could not be loaded at '{profilePath}'.");
                return;
            }

            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                Debug.LogWarning($"Manifestor startup apply skipped: {validation.message}");
                return;
            }

            string currentProfileFingerprint;
            try
            {
                currentProfileFingerprint = ManifestorProfileFingerprint.Calculate(profile);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Manifestor startup apply skipped: {exception.Message}");
                return;
            }

            if (ManifestorEditorPrefs.TryGetLastAppliedProfileFingerprint(out var savedProfileFingerprint) &&
                string.Equals(currentProfileFingerprint, savedProfileFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            var result = CustomBuildPipeline.Apply(profile);
            if (!result.success)
            {
                Debug.LogWarning($"Manifestor startup apply skipped: {result.message}");
            }
        }
    }
}
