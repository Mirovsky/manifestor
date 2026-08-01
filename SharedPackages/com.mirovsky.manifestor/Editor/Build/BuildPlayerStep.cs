namespace Manifestor.Build
{
    using System;
    using System.IO;
    using System.Linq;
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

                var cleanBuild = (context.options & BuildOptions.CleanBuildCache) != 0;
                var outputDirectoryPath = BuildOutputDirectoryUtility.Prepare(
                    context.profile,
                    context.outputDirectoryPath,
                    cleanBuild);

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

    }

    internal static class BuildOutputDirectoryUtility
    {
        private const string OwnershipMarkerFileName = ".manifestor-build-output";

        public static string Prepare(ManifestProfileSO profile, string outputRootDirectoryPath, bool clean)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var ownerId = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new InvalidOperationException("Manifest profile must be saved as a project asset before building.");
            }

            return PrepareOwnedDirectory(profile.profileName, outputRootDirectoryPath, ownerId, clean);
        }

        internal static bool IsValidDirectoryName(string profileName)
        {
            try
            {
                ValidateDirectoryName((profileName ?? string.Empty).Trim());
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string PrepareOwnedDirectory(
            string profileName,
            string outputRootDirectoryPath,
            string ownerId,
            bool clean)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException("Build directory owner ID cannot be empty.", nameof(ownerId));
            }

            var buildDirectoryPath = GetBuildDirectoryPath(profileName, outputRootDirectoryPath);
            var markerPath = Path.Combine(buildDirectoryPath, OwnershipMarkerFileName);
            if (Directory.Exists(buildDirectoryPath))
            {
                var hasContents = Directory.EnumerateFileSystemEntries(buildDirectoryPath).Any();
                var hasValidMarker = File.Exists(markerPath) &&
                                     string.Equals(File.ReadAllText(markerPath).Trim(), ownerId, StringComparison.Ordinal);
                if (hasContents && !hasValidMarker)
                {
                    throw new InvalidOperationException(
                        $"Build directory '{buildDirectoryPath}' is not owned by this manifest profile and cannot be used.");
                }

                if (clean)
                {
                    Directory.Delete(buildDirectoryPath, recursive: true);
                }
            }

            Directory.CreateDirectory(buildDirectoryPath);
            File.WriteAllText(markerPath, ownerId);
            return buildDirectoryPath;
        }

        private static string GetBuildDirectoryPath(string profileName, string outputRootDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(outputRootDirectoryPath))
            {
                throw new ArgumentException("Build output root directory cannot be empty.", nameof(outputRootDirectoryPath));
            }

            var normalizedProfileName = (profileName ?? string.Empty).Trim();
            ValidateDirectoryName(normalizedProfileName);
            var rootPath = NormalizeDirectoryPath(outputRootDirectoryPath);
            var buildDirectoryPath = NormalizeDirectoryPath(Path.Combine(rootPath, normalizedProfileName));
            if (!buildDirectoryPath.StartsWith(rootPath + Path.DirectorySeparatorChar, GetPathComparison()))
            {
                throw new InvalidOperationException("The profile build directory must be a child of the selected output root.");
            }

            return buildDirectoryPath;
        }

        private static void ValidateDirectoryName(string directoryName)
        {
            if (string.IsNullOrWhiteSpace(directoryName) || directoryName == "." || directoryName == ".." ||
                directoryName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                directoryName.Contains(Path.DirectorySeparatorChar) ||
                directoryName.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException($"Profile name '{directoryName}' is not a valid build directory name.");
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
