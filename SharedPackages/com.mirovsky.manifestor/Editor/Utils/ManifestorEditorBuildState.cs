namespace Manifestor
{
    using UnityEditor;
    using UnityEditor.Build.Profile;

    internal interface IManifestorEditorBuildState
    {
        BuildProfile activeBuildProfile { get; }
        BuildTarget activeBuildTarget { get; }

        void SetActiveBuildProfile(BuildProfile buildProfile);
        bool SwitchActiveBuildTarget(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget);
    }

    internal static class ManifestorEditorBuildState
    {
        private static readonly IManifestorEditorBuildState UnityState = new UnityManifestorEditorBuildState();
        private static IManifestorEditorBuildState _current = UnityState;

        public static BuildProfile activeBuildProfile => _current.activeBuildProfile;
        public static BuildTarget activeBuildTarget => _current.activeBuildTarget;

        public static void SetActiveBuildProfile(BuildProfile buildProfile)
        {
            _current.SetActiveBuildProfile(buildProfile);
        }

        public static bool SwitchActiveBuildTarget(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget)
        {
            return _current.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
        }

        internal static void SetCurrentForTests(IManifestorEditorBuildState state)
        {
            _current = state ?? UnityState;
        }

        internal static void ResetForTests()
        {
            _current = UnityState;
        }

        private sealed class UnityManifestorEditorBuildState : IManifestorEditorBuildState
        {
            public BuildProfile activeBuildProfile => BuildProfile.GetActiveBuildProfile();
            public BuildTarget activeBuildTarget => EditorUserBuildSettings.activeBuildTarget;

            public void SetActiveBuildProfile(BuildProfile buildProfile)
            {
                BuildProfile.SetActiveBuildProfile(buildProfile);
            }

            public bool SwitchActiveBuildTarget(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget)
            {
                return EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
            }
        }
    }
}
