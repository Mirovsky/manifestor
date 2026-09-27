namespace Manifestor.Build
{
    using System.Linq;
    using UnityEditor;
    using UnityEditor.Build.Profile;
    using UnityEngine;

    [InitializeOnLoad]
    internal static class ManifestorBuildProfilesHandler
    {
        static ManifestorBuildProfilesHandler()
        {
            if (ManifestorBuildProfilesSettings.instance.enabled)
            {
                BuildPlayerWindow.RegisterBuildPlayerHandler(HandleBuild);
            }
        }

        private static void HandleBuild(BuildPlayerOptions buildPlayerOptions)
        {
            if (ManifestorUnityEditorPipeline.isActive)
            {
                AbortBuild("A Manifestor build is already in progress.");
                return;
            }

            var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
            if (activeBuildProfile == null)
            {
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(buildPlayerOptions);
                return;
            }

            var matches = ManifestProfileAssets.FindAll()
                .Where(profile => profile.buildProfile == activeBuildProfile)
                .ToArray();
            if (matches.Length == 0)
            {
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(buildPlayerOptions);
                return;
            }

            ManifestProfileSO profile;
            if (matches.Length == 1)
            {
                profile = matches[0];
            }
            else
            {
                var lastAppliedProfile = ManifestorSettings.instance.appliedProfile;
                profile = matches.FirstOrDefault(candidate => candidate == lastAppliedProfile);
                if (profile == null)
                {
                    AbortBuild(
                        $"Multiple Manifestor profiles reference the active Build Profile '{activeBuildProfile.name}'. " +
                        "Apply one of them in Manifestor before building.");
                    return;
                }
            }

            var result = ManifestorUnityEditorPipeline.BuildFromBuildProfiles(profile, buildPlayerOptions);
            if (!result.success)
            {
                AbortBuild(result.message);
            }
        }

        private static void AbortBuild(string message)
        {
            Debug.LogError($"Manifestor Build Profiles build could not start: {message}");
            throw new BuildPlayerWindow.BuildMethodException();
        }
    }
}
