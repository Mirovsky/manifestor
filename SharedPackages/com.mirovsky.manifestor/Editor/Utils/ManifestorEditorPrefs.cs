using UnityEditor;

namespace Manifestor
{
    public static class ManifestorEditorPrefs
    {
        private const string LastAppliedProfilePathKey = "Mirov.Manifestor.LastAppliedProfilePath";
        private const string LastAppliedProfileFingerprintKey = "Mirov.Manifestor.LastAppliedProfileFingerprint";
        private const string LegacyLastAppliedManifestHashKey = "Mirov.Manifestor.LastAppliedManifestHash";

        public static void SetLastAppliedProfile(string assetPath)
        {
            EditorPrefs.SetString(LastAppliedProfilePathKey, assetPath ?? string.Empty);
        }

        public static void SetLastAppliedProfileFingerprint(string profileFingerprint)
        {
            EditorPrefs.SetString(LastAppliedProfileFingerprintKey, profileFingerprint ?? string.Empty);
        }

        public static void ClearLastAppliedProfile()
        {
            EditorPrefs.DeleteKey(LastAppliedProfilePathKey);
            EditorPrefs.DeleteKey(LastAppliedProfileFingerprintKey);
            EditorPrefs.DeleteKey(LegacyLastAppliedManifestHashKey);
        }

        public static bool TryGetLastAppliedProfilePath(out string assetPath)
        {
            assetPath = EditorPrefs.GetString(LastAppliedProfilePathKey, string.Empty);

            return !string.IsNullOrEmpty(assetPath);
        }

        public static bool TryGetLastAppliedProfileFingerprint(out string profileFingerprint)
        {
            profileFingerprint = EditorPrefs.GetString(LastAppliedProfileFingerprintKey, string.Empty);

            return !string.IsNullOrEmpty(profileFingerprint);
        }

        internal static void RestoreLastAppliedProfile(
            bool hadProfilePath,
            string profilePath,
            bool hadFingerprint,
            string fingerprint)
        {
            if (hadProfilePath)
            {
                SetLastAppliedProfile(profilePath);
            }
            else
            {
                EditorPrefs.DeleteKey(LastAppliedProfilePathKey);
            }

            if (hadFingerprint)
            {
                SetLastAppliedProfileFingerprint(fingerprint);
            }
            else
            {
                EditorPrefs.DeleteKey(LastAppliedProfileFingerprintKey);
            }
        }
    }
}
