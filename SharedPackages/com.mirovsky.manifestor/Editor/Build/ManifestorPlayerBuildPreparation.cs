namespace Manifestor.Build
{
    using System;
    using System.Linq;
    using UnityEditor;

    internal static class ManifestorPlayerBuildPreparation
    {
        public static ManifestorBuildStepResult Prepare(
            ManifestorBuildContext context,
            ManifestorBuildStepTargets targets)
        {
            if (context?.profile?.buildProfile == null)
            {
                return ManifestorBuildStepResult.Failed("A manifest profile with a Unity Build Profile is required.");
            }

            if (context.cancellationRequested)
            {
                return ManifestorBuildStepResult.Cancelled("Player build was cancelled before preparation started.");
            }

            try
            {
                var requestedBuildTarget = BuildProfileUtility.GetBuildTarget(context.profile.buildProfile);
                if (!ManifestorApplicator.IsRequestedBuildStateActive(context.profile.buildProfile, requestedBuildTarget))
                {
                    return ManifestorBuildStepResult.Failed(
                        ManifestorApplicator.CreateBuildStateMismatchMessage(
                            context.profile.buildProfile,
                            requestedBuildTarget));
                }

                var buildPlayerOptions = context.buildPlayerOptions;
                if (buildPlayerOptions.target is 0 or BuildTarget.NoTarget)
                {
                    buildPlayerOptions.target = requestedBuildTarget;
                    buildPlayerOptions.subtarget = BuildProfileUtility.GetSubtarget(context.profile.buildProfile);
                }

                if (buildPlayerOptions.targetGroup == BuildTargetGroup.Unknown)
                {
                    buildPlayerOptions.targetGroup = BuildPipeline.GetBuildTargetGroup(buildPlayerOptions.target);
                }

                buildPlayerOptions.scenes ??= GetEnabledEditorBuildSettingsScenes();
                buildPlayerOptions.options |= BuildOptions.DetailedBuildReport;

                if ((targets & ManifestorBuildStepTargets.Standard) != 0 &&
                    string.IsNullOrWhiteSpace(buildPlayerOptions.locationPathName))
                {
                    buildPlayerOptions.locationPathName = EditorUserBuildSettings.GetBuildLocation(buildPlayerOptions.target);
                    if (string.IsNullOrWhiteSpace(buildPlayerOptions.locationPathName))
                    {
                        return ManifestorBuildStepResult.Failed(
                            $"No build location is configured for target '{buildPlayerOptions.target}'.");
                    }
                }

                context.buildPlayerOptions = buildPlayerOptions;
                return ManifestorBuildStepResult.Succeeded("Player build preparation completed.");
            }
            catch (Exception exception)
            {
                return ManifestorBuildStepResult.Failed($"Failed to prepare player build: {exception.Message}");
            }
        }

        private static string[] GetEnabledEditorBuildSettingsScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                .Select(scene => scene.path)
                .ToArray();
        }

        public static void ApplyScenesToEditorBuildSettings(string[] scenes)
        {
            EditorBuildSettings.scenes = (scenes ?? Array.Empty<string>())
                .Select(path => new EditorBuildSettingsScene(path ?? string.Empty, true))
                .ToArray();
        }
    }
}
