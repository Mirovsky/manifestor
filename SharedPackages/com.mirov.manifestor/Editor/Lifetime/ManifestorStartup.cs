using System;
using UnityEditor;
using UnityEngine;

namespace Mirov.Manifestor.Editor
{
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

            string currentManifestHash;
            try
            {
                var generatedManifest = ManifestorIO.ConvertToManifest(profile);
                currentManifestHash = ManifestorIO.CalculateManifestHash(generatedManifest);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Manifestor startup apply skipped: {exception.Message}");
                return;
            }

            if (ManifestorEditorPrefs.TryGetLastAppliedManifestHash(out var savedManifestHash) &&
                string.Equals(currentManifestHash, savedManifestHash, StringComparison.Ordinal))
            {
                return;
            }

            var result = ManifestorApplicator.Apply(profile);
            if (!result.success)
            {
                Debug.LogWarning($"Manifestor startup apply skipped: {result.message}");
            }
        }
    }
}
