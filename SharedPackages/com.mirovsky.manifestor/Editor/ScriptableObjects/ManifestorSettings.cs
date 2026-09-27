namespace Manifestor
{
    using UnityEditor;
    using UnityEngine;

    [FilePath(relativePath: "UserSettings/ManifestorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class ManifestorSettings : ScriptableSingleton<ManifestorSettings>
    {
        [Header("Manifest Profile")]
        [SerializeField] private string _profilePath;
        [SerializeField] private string _profileFingerprint;

        public ManifestProfileSO appliedProfile => TryGetLastAppliedProfilePath(out var assetPath)
            ? AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(assetPath)
            : null;

        public void SetLastAppliedManifest(string path, string fingerprint)
        {
            _profilePath = path;
            _profileFingerprint = fingerprint;

            Save(saveAsText: true);
            EditorApplication.delayCall += EditorApplication.UpdateMainWindowTitle;
        }

        public bool TryGetLastAppliedProfilePath(out string assetPath)
        {
            assetPath = _profilePath;

            return !string.IsNullOrEmpty(assetPath);
        }

        public bool TryGetLastAppliedProfileFingerprint(out string fingerprint)
        {
            fingerprint = _profileFingerprint;

            return !string.IsNullOrEmpty(_profileFingerprint);
        }

        public void RestoreLastAppliedProfile(
            bool hadProfilePath,
            string path,
            bool hadFingerprint,
            string fingerprint)
        {
            _profilePath = hadProfilePath ? path : string.Empty;
            _profileFingerprint = hadFingerprint ? fingerprint : string.Empty;

            Save(saveAsText: true);
            EditorApplication.delayCall += EditorApplication.UpdateMainWindowTitle;
        }
    }
}
