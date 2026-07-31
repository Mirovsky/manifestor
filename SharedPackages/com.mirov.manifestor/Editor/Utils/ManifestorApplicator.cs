namespace Mirov.Manifestor.Editor
{
    using System;
    using UnityEditor;
    using UnityEditor.Build.Profile;
    using UnityEditor.PackageManager;

    public static class ManifestorApplicator
    {
        public static ManifestorResult Apply(ManifestProfileSO profile)
        {
            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                return validation;
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            try
            {
                BuildProfile.SetActiveBuildProfile(profile.buildProfile);

                var newManifest = ManifestorIO.ConvertToManifest(profile);
                ManifestorIO.SaveManifest(newManifest);

                ManifestorEditorPrefs.SetLastAppliedProfile(profilePath);

                Client.Resolve();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            catch (Exception exception)
            {
                return ManifestorResult.Error($"Failed to apply manifest profile '{profile.name}': {exception.Message}");
            }

            return ManifestorResult.Ok();
        }
    }
}
