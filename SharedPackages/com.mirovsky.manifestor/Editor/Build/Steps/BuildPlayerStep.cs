namespace Manifestor.Build
{
    using System;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.Build.Reporting;
    using UnityEditor.SceneManagement;

    [ManifestorBuildStep(typeof(ApplyManifestBuildStep), ManifestorBuildStepOrder.After)]
    public sealed class BuildPlayerStep : IManifestorBuildStep
    {
        public ManifestorBuildStepResult Tick(ManifestorBuildContext context)
        {
            if (context?.profile?.buildProfile == null)
            {
                return ManifestorBuildStepResult.Failed("A manifest profile with a Unity Build Profile is required.");
            }

            if (context.cancellationRequested)
            {
                return ManifestorBuildStepResult.Cancelled("Player build was cancelled before it started.");
            }

            SceneSetup[] originalSceneSetup = null;
            ManifestorBuildStepResult result;
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

                var buildPlayerOptions = ApplyDefaults(context);

                UnityEngine.Debug.Log(
                    $"Custom build target '{buildPlayerOptions.target}' will output to " +
                    $"'{buildPlayerOptions.locationPathName}'.");

                buildPlayerOptions.options |= BuildOptions.DetailedBuildReport;
                context.buildPlayerOptions = buildPlayerOptions;
                originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
                var report = BuildPipeline.BuildPlayer(buildPlayerOptions);

                result = report.summary.result switch
                {
                    BuildResult.Succeeded => ManifestorBuildStepResult.Succeeded(
                        $"Build succeeded at '{buildPlayerOptions.locationPathName}': " +
                        $"{report.summary.totalSize} bytes."),
                    BuildResult.Cancelled => ManifestorBuildStepResult.Cancelled(
                        $"Build to '{buildPlayerOptions.locationPathName}' was cancelled."),
                    _ => ManifestorBuildStepResult.Failed(
                        $"Build to '{buildPlayerOptions.locationPathName}' failed with " +
                        $"{report.summary.totalErrors} error(s).")
                };
            }
            catch (Exception exception)
            {
                result = ManifestorBuildStepResult.Failed($"Failed to build player: {exception.Message}");
            }

            return RestoreSceneSetup(result, originalSceneSetup);
        }

        private static BuildPlayerOptions ApplyDefaults(ManifestorBuildContext context)
        {
            var buildPlayerOptions = context.buildPlayerOptions;
            var usesDefaultTarget = buildPlayerOptions.target is 0 or BuildTarget.NoTarget;
            if (usesDefaultTarget)
            {
                buildPlayerOptions.target = BuildProfileUtility.GetBuildTarget(context.profile.buildProfile);
                buildPlayerOptions.subtarget = BuildProfileUtility.GetSubtarget(context.profile.buildProfile);
            }

            if (buildPlayerOptions.targetGroup == BuildTargetGroup.Unknown)
            {
                buildPlayerOptions.targetGroup = BuildPipeline.GetBuildTargetGroup(buildPlayerOptions.target);
            }

            buildPlayerOptions.scenes ??= EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (string.IsNullOrWhiteSpace(buildPlayerOptions.locationPathName))
            {
                buildPlayerOptions.locationPathName = EditorUserBuildSettings.GetBuildLocation(buildPlayerOptions.target);
            }

            if (string.IsNullOrWhiteSpace(buildPlayerOptions.locationPathName))
            {
                throw new InvalidOperationException($"No build location is configured for target '{buildPlayerOptions.target}'.");
            }

            return buildPlayerOptions;
        }

        private static ManifestorBuildStepResult RestoreSceneSetup(
            ManifestorBuildStepResult buildResult,
            SceneSetup[] originalSceneSetup)
        {
            if (!CanRestoreSceneSetup(originalSceneSetup))
            {
                return buildResult;
            }

            try
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(
                    $"Manifestor could not restore the editor scene setup after the player build: " +
                    $"{exception.Message}");
            }

            return buildResult;
        }

        private static bool CanRestoreSceneSetup(SceneSetup[] sceneSetup)
        {
            if (sceneSetup == null || sceneSetup.Length == 0)
            {
                return false;
            }

            var loadedSceneCount = sceneSetup.Count(scene => scene.isLoaded);
            var activeScenes = sceneSetup.Where(scene => scene.isActive).ToArray();
            return loadedSceneCount > 0 &&
                   activeScenes.Length == 1 &&
                   activeScenes[0].isLoaded;
        }
    }
}
