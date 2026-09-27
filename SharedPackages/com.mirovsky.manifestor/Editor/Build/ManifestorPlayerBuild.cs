namespace Manifestor.Build
{
    using System;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.Build.Reporting;
    using UnityEditor.SceneManagement;

    internal static class ManifestorPlayerBuild
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
                var optionsValidation = ManifestorBuildExecution.NormalizeBuildPlayerOptions(
                    context.profile, ref buildPlayerOptions);
                if (!optionsValidation.success)
                {
                    return ManifestorBuildStepResult.Failed(optionsValidation.message);
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

        public static ManifestorBuildStepResult Build(ManifestorBuildContext context)
        {
            if (context == null)
            {
                return ManifestorBuildStepResult.Failed("A prepared player build context is required.");
            }

            if (context.cancellationRequested)
            {
                return ManifestorBuildStepResult.Cancelled("Player build was cancelled before it started.");
            }

            SceneSetup[] originalSceneSetup = null;
            ManifestorBuildStepResult result;
            try
            {
                var buildPlayerOptions = context.buildPlayerOptions;
                UnityEngine.Debug.Log(
                    $"Custom build target '{buildPlayerOptions.target}' will output to " +
                    $"'{buildPlayerOptions.locationPathName}'.");

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

        public static void ApplyScenesToEditorBuildSettings(string[] scenes)
        {
            EditorBuildSettings.scenes = (scenes ?? Array.Empty<string>())
                .Select(path => new EditorBuildSettingsScene(path ?? string.Empty, true))
                .ToArray();
        }

        private static string[] GetEnabledEditorBuildSettingsScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                .Select(scene => scene.path)
                .ToArray();
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
                    "Manifestor could not restore the editor scene setup after the player build: " +
                    exception.Message);
            }

            return buildResult;
        }

        private static bool CanRestoreSceneSetup(SceneSetup[] sceneSetup)
        {
            if (sceneSetup == null || sceneSetup.Length == 0)
            {
                return false;
            }

            var loadedSceneCount = 0;
            var activeLoadedSceneCount = 0;
            foreach (var scene in sceneSetup)
            {
                if (!scene.isLoaded)
                {
                    continue;
                }

                loadedSceneCount++;
                if (scene.isActive)
                {
                    activeLoadedSceneCount++;
                }
            }

            return loadedSceneCount > 0 && activeLoadedSceneCount == 1;
        }
    }
}
