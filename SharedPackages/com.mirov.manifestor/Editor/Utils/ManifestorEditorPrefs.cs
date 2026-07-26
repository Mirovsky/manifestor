using UnityEditor;

namespace Mirov.Manifestor.Editor
{
    public static class ManifestorEditorPrefs
    {
        private const string LastAppliedProfilePathKey = "Mirov.Manifestor.LastAppliedProfilePath";
        private const string LastAppliedManifestHashKey = "Mirov.Manifestor.LastAppliedManifestHash";

        public static void SetLastAppliedProfile(string assetPath)
        {
            EditorPrefs.SetString(LastAppliedProfilePathKey, assetPath ?? string.Empty);
        }

        public static void SetLastAppliedManifestHash(string manifestHash)
        {
            EditorPrefs.SetString(LastAppliedManifestHashKey, manifestHash ?? string.Empty);
        }

        public static void ClearLastAppliedProfile()
        {
            EditorPrefs.DeleteKey(LastAppliedProfilePathKey);
            EditorPrefs.DeleteKey(LastAppliedManifestHashKey);
        }

        public static bool TryGetLastAppliedProfilePath(out string assetPath)
        {
            assetPath = EditorPrefs.GetString(LastAppliedProfilePathKey, string.Empty);

            return !string.IsNullOrEmpty(assetPath);
        }

        public static bool TryGetLastAppliedManifestHash(out string manifestHash)
        {
            manifestHash = EditorPrefs.GetString(LastAppliedManifestHashKey, string.Empty);

            return !string.IsNullOrEmpty(manifestHash);
        }
    }
}
