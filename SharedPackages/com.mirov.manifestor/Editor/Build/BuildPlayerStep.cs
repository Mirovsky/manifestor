namespace Mirov.Manifestor.Editor
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Build.Reporting;

    [CustomBuildStep(typeof(ApplyManifestBuildStep), CustomBuildStepOrder.After)]
    public sealed class BuildPlayerStep : ICustomBuildStep
    {
        public CustomBuildStepResult Execute(CustomBuildContext context)
        {
            if (context?.profile?.buildProfile == null)
            {
                return CustomBuildStepResult.Failed("A manifest profile with a Unity Build Profile is required.");
            }

            if (string.IsNullOrWhiteSpace(context.outputDirectoryPath))
            {
                return CustomBuildStepResult.Failed("Build output directory cannot be empty.");
            }

            var originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
            CustomBuildStepResult result;
            try
            {
                var buildTarget = BuildProfileUtility.GetBuildTarget(context.profile.buildProfile);
                var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
                if (!SwitchActiveBuildTarget(buildTarget, buildTargetGroup))
                {
                    result = CustomBuildStepResult.Failed(
                        $"Unity could not switch to build target '{buildTarget}'.");
                    return RestoreBuildTargetAfterFailure(result, originalBuildTarget);
                }

                var outputDirectoryPath = Path.GetFullPath(context.outputDirectoryPath);
                var cleanBuild = (context.options & BuildOptions.CleanBuildCache) != 0;
                if (cleanBuild)
                {
                    ValidateCleanBuildDirectory(outputDirectoryPath);
                    DeleteBuildDirectoryContents(outputDirectoryPath);
                }
                else
                {
                    Directory.CreateDirectory(outputDirectoryPath);
                }

                var outputFilePath = GetOutputFilePath(buildTarget, outputDirectoryPath);
                UnityEngine.Debug.Log(
                    $"Custom build target '{buildTarget}' will output to '{outputFilePath}'.");

                var report = BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
                {
                    buildProfile = context.profile.buildProfile,
                    locationPathName = outputFilePath,
                    options = context.options | BuildOptions.DetailedBuildReport
                });

                result = report.summary.result switch
                {
                    BuildResult.Succeeded => CustomBuildStepResult.Succeeded(
                        $"Build succeeded at '{outputFilePath}': {report.summary.totalSize} bytes."),
                    BuildResult.Cancelled => CustomBuildStepResult.Cancelled(
                        $"Build to '{outputFilePath}' was cancelled."),
                    _ => CustomBuildStepResult.Failed(
                        $"Build to '{outputFilePath}' failed with {report.summary.totalErrors} error(s).")
                };
            }
            catch (Exception exception)
            {
                result = CustomBuildStepResult.Failed($"Failed to build player: {exception.Message}");
            }

            return result.success
                ? result
                : RestoreBuildTargetAfterFailure(result, originalBuildTarget);
        }

        private static bool SwitchActiveBuildTarget(BuildTarget buildTarget, BuildTargetGroup buildTargetGroup)
        {
            return EditorUserBuildSettings.activeBuildTarget == buildTarget ||
                   EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
        }

        private static CustomBuildStepResult RestoreBuildTargetAfterFailure(
            CustomBuildStepResult buildResult,
            BuildTarget originalBuildTarget)
        {
            try
            {
                var originalBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(originalBuildTarget);
                if (SwitchActiveBuildTarget(originalBuildTarget, originalBuildTargetGroup))
                {
                    return buildResult;
                }

                return CustomBuildStepResult.Failed(
                    $"{buildResult.message} Unity could not restore build target '{originalBuildTarget}'.");
            }
            catch (Exception exception)
            {
                return CustomBuildStepResult.Failed(
                    $"{buildResult.message} Failed to restore build target '{originalBuildTarget}': {exception.Message}");
            }
        }

        private static string GetOutputFilePath(BuildTarget buildTarget, string outputDirectoryPath)
        {
            var fileName = PlayerSettings.productName;
            var extension = buildTarget switch
            {
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => ".exe",
                BuildTarget.StandaloneOSX => ".app",
                _ => string.Empty
            };

            return Path.Combine(outputDirectoryPath, fileName + extension);
        }

        private static void DeleteBuildDirectoryContents(string outputDirectoryPath)
        {
            if (Directory.Exists(outputDirectoryPath))
            {
                Directory.Delete(outputDirectoryPath, recursive: true);
            }

            Directory.CreateDirectory(outputDirectoryPath);
        }

        private static void ValidateCleanBuildDirectory(string outputDirectoryPath)
        {
            var normalizedOutputPath = NormalizeDirectoryPath(outputDirectoryPath);
            var rootPath = NormalizeDirectoryPath(Path.GetPathRoot(normalizedOutputPath));
            if (string.Equals(normalizedOutputPath, rootPath, GetPathComparison()))
            {
                throw new InvalidOperationException("A drive or filesystem root cannot be used as a clean build directory.");
            }

            var projectPath = NormalizeDirectoryPath(Path.GetDirectoryName(UnityEngine.Application.dataPath));
            var outputPathPrefix = normalizedOutputPath + Path.DirectorySeparatorChar;
            if (string.Equals(normalizedOutputPath, projectPath, GetPathComparison()) ||
                projectPath.StartsWith(outputPathPrefix, GetPathComparison()))
            {
                throw new InvalidOperationException(
                    "The project directory or one of its ancestors cannot be used as a clean build directory.");
            }
        }

        private static string NormalizeDirectoryPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, rootPath, GetPathComparison())
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static StringComparison GetPathComparison()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
