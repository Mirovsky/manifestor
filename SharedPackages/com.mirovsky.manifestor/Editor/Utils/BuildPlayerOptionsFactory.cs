namespace Manifestor.Build
{
    using System;
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
            var buildLocation = Path.Combine(outputFolderPath, PlayerSettings.productName);

            return new BuildPlayerOptions
            {
                target = buildTarget,
                targetGroup = buildTargetGroup,
                subtarget = BuildProfileUtility.GetSubtarget(profile.buildProfile),
                locationPathName = buildLocation,
                assetBundleManifestPath = UnityEditorInternalApi.GetStreamingAssetsBundleManifestPath(),
                options = UnityEditorInternalApi.GetBuildOptions(buildTarget, buildTargetGroup, buildLocation, options) | options
            };
        }

        public static void FinalizeBuildLocation(ref BuildPlayerOptions buildPlayerOptions)
        {
            var extension = UnityEditorInternalApi.GetBuildExtension(
                buildPlayerOptions.target,
                buildPlayerOptions.subtarget,
                buildPlayerOptions.options);
            if (string.IsNullOrEmpty(extension) ||
                string.Equals(Path.GetExtension(buildPlayerOptions.locationPathName), "." + extension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            buildPlayerOptions.locationPathName += "." + extension;
        }
    }
}
