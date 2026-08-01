namespace Manifestor
{
    using System;
    using UnityEditor;

    public static class ManifestorProfileFingerprint
    {
        public static string Calculate(ManifestProfileSO profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(profilePath))
            {
                throw new InvalidOperationException("Manifest profile must be saved as a project asset.");
            }

            return AssetDatabase.GetAssetDependencyHash(profilePath).ToString();
        }
    }
}
