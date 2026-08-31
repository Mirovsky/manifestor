namespace Manifestor.Build
{
    using System;
    using UnityEditor;
    using UnityEditor.Build.Reporting;
    using UnityEditor.SceneManagement;

    internal static class ManifestorPlayerBuilder
    {
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

            var loadedSceneCount = 0;
            var activeLoadedSceneCount = 0;
            foreach (var scene in sceneSetup)
            {
                if (scene.isLoaded)
                {
                    loadedSceneCount++;
                    if (scene.isActive)
                    {
                        activeLoadedSceneCount++;
                    }
                }
            }

            return loadedSceneCount > 0 &&
                   activeLoadedSceneCount == 1;
        }
    }
}
