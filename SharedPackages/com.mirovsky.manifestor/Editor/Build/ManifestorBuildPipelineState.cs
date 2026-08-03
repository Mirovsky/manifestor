namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    [Serializable]
    internal sealed class ManifestorBuildPipelineState
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public bool isActive;
        public ManifestorBuildPipelineStatus status;
        public ManifestorBuildOperation operation;
        public string message;
        public string profileGuid;
        public string profileFingerprint;
        public SerializableBuildPlayerOptions buildPlayerOptions = new();
        public List<string> orderedStepTypeNames = new();
        public int nextStepIndex;
        public string currentStepTypeName;
        public string stepState;
        public bool cancellationRequested;
        public long resumeAfterUtcTicks;
    }

    [Serializable]
    internal sealed class SerializableBuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public string assetBundleManifestPath;
        public int targetGroup;
        public int target;
        public int subtarget;
        public int options;
        public string[] extraScriptingDefines;

        public static SerializableBuildPlayerOptions From(BuildPlayerOptions buildPlayerOptions)
        {
            return new SerializableBuildPlayerOptions
            {
                scenes = buildPlayerOptions.scenes,
                locationPathName = buildPlayerOptions.locationPathName,
                assetBundleManifestPath = buildPlayerOptions.assetBundleManifestPath,
                targetGroup = (int)buildPlayerOptions.targetGroup,
                target = (int)buildPlayerOptions.target,
                subtarget = buildPlayerOptions.subtarget,
                options = (int)buildPlayerOptions.options,
                extraScriptingDefines = buildPlayerOptions.extraScriptingDefines
            };
        }

        public BuildPlayerOptions ToBuildPlayerOptions()
        {
            return new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                assetBundleManifestPath = assetBundleManifestPath,
                targetGroup = (BuildTargetGroup)targetGroup,
                target = (BuildTarget)target,
                subtarget = subtarget,
                options = (BuildOptions)options,
                extraScriptingDefines = extraScriptingDefines
            };
        }
    }

    internal static class ManifestorBuildPipelineStateStore
    {
        internal const string StateKey = "Manifestor.CustomBuildPipeline.State";

        public static ManifestorBuildPipelineState Load()
        {
            var json = SessionState.GetString(StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new ManifestorBuildPipelineState();
            }

            try
            {
                var state = JsonUtility.FromJson<ManifestorBuildPipelineState>(json);
                if (state == null || state.version != ManifestorBuildPipelineState.CurrentVersion)
                {
                    SessionState.EraseString(StateKey);
                    return FailedState("Custom build state has an unsupported version and was cleared.");
                }

                return state;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to restore custom build state: {exception.Message}");
                SessionState.EraseString(StateKey);
                return FailedState("Failed to restore custom build state.");
            }
        }

        public static void Save(ManifestorBuildPipelineState state)
        {
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        private static ManifestorBuildPipelineState FailedState(string message)
        {
            return new ManifestorBuildPipelineState
            {
                status = ManifestorBuildPipelineStatus.Failed,
                message = message
            };
        }
    }
}
