namespace Manifestor.Build
{
    using System;
    using System.Linq;
    using UnityEditor;
    using UnityEditor.Build.Reporting;

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

            var originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            ManifestorBuildStepResult result;
            try
            {
                var buildPlayerOptions = ApplyDefaults(context);
                if (!SwitchActiveBuildTarget(buildPlayerOptions.target, buildPlayerOptions.targetGroup))
                {
                    result = ManifestorBuildStepResult.Failed(
                        $"Unity could not switch to build target '{buildPlayerOptions.target}'.");
                    return RestoreBuildTargetAfterFailure(result, originalBuildTarget);
                }

                UnityEngine.Debug.Log(
                    $"Custom build target '{buildPlayerOptions.target}' will output to " +
                    $"'{buildPlayerOptions.locationPathName}'.");

                buildPlayerOptions.options |= BuildOptions.DetailedBuildReport;
                context.buildPlayerOptions = buildPlayerOptions;
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

            return result.success
                ? result
                : RestoreBuildTargetAfterFailure(result, originalBuildTarget);
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

        private static bool SwitchActiveBuildTarget(BuildTarget buildTarget, BuildTargetGroup buildTargetGroup)
        {
            return EditorUserBuildSettings.activeBuildTarget == buildTarget ||
                   EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
        }

        private static ManifestorBuildStepResult RestoreBuildTargetAfterFailure(
            ManifestorBuildStepResult buildResult,
            BuildTarget originalBuildTarget)
        {
            try
            {
                var originalBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(originalBuildTarget);
                if (SwitchActiveBuildTarget(originalBuildTarget, originalBuildTargetGroup))
                {
                    return buildResult;
                }

                return ManifestorBuildStepResult.Failed(
                    $"{buildResult.message} Unity could not restore build target '{originalBuildTarget}'.");
            }
            catch (Exception exception)
            {
                return ManifestorBuildStepResult.Failed(
                    $"{buildResult.message} Failed to restore build target '{originalBuildTarget}': {exception.Message}");
            }
        }

    }
}
