namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    [Serializable]
    internal sealed class ManifestorBuildPipelineState
    {
        public const int CurrentVersion = 2;

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
        public SerializableBuildUserData userData = new();
        public bool cancellationRequested;
        public long resumeAfterUtcTicks;
    }

    [Serializable]
    internal sealed class SerializableBuildUserData
    {
        public List<SerializableBuildUserDataEntry> entries = new();

        public static SerializableBuildUserData From(IReadOnlyDictionary<string, string> userData)
        {
            var serializedData = new SerializableBuildUserData();
            if (userData == null)
            {
                return serializedData;
            }

            foreach (var pair in userData)
            {
                serializedData.entries.Add(new SerializableBuildUserDataEntry
                {
                    key = pair.Key,
                    value = pair.Value ?? string.Empty
                });
            }

            serializedData.entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.key, right.key));
            return serializedData;
        }

        public IReadOnlyDictionary<string, string> ToDictionary()
        {
            var userData = new Dictionary<string, string>(StringComparer.Ordinal);
            if (entries == null)
            {
                return userData;
            }

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    continue;
                }

                userData[entry.key] = entry.value ?? string.Empty;
            }

            return userData;
        }
    }

    [Serializable]
    internal sealed class SerializableBuildUserDataEntry
    {
        public string key;
        public string value;
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
