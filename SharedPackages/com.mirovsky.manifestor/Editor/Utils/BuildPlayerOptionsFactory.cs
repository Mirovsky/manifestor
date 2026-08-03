namespace Manifestor.Build
{
    using System.IO;
    using UnityEditor;

    internal static class BuildPlayerOptionsFactory
    {
        public static BuildPlayerOptions Create(
            ManifestProfileSO profile,
            string outputFolderPath,
            BuildOptions options)
        {
            var buildTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            var extension = buildTarget switch
            {
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => ".exe",
                BuildTarget.StandaloneOSX => ".app",
                _ => string.Empty
            };
            var buildLocation = Path.Combine(outputFolderPath, PlayerSettings.productName + extension);

            return new BuildPlayerOptions
            {
                target = buildTarget,
                targetGroup = buildTargetGroup,
                locationPathName = buildLocation,
                assetBundleManifestPath = UnityEditorInternalApi.GetStreamingAssetsBundleManifestPath(),
                options = UnityEditorInternalApi.GetBuildOptions(buildTarget, buildTargetGroup, buildLocation, options) | options
            };
        }
    }
}
