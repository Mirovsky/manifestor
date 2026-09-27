namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public enum ManifestorBuildPipelineStatus
    {
        Idle,
        Waiting,
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum ManifestorBuildOperation
    {
        Apply,
        Build
    }

    [InitializeOnLoad]
    public static class ManifestorUnityEditorPipeline
    {
        private static readonly ManifestorBuildRunner Runner = new(InvokeCompleted);

        public static bool isActive => Runner.isActive;

        public static event Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> completed;

        static ManifestorUnityEditorPipeline()
        {
            Runner.Restore();
        }

        public static bool TryGetOrderedSteps(out IReadOnlyList<Type> orderedSteps, out string error)
        {
            var success = ManifestorBuildStepOrderResolver.TryResolve(out var steps, out error);
            orderedSteps = steps.AsReadOnly();
            return success;
        }

        public static ManifestorResult Apply(ManifestProfileSO profile)
        {
            return Start(
                profile,
                ManifestorBuildOperation.Apply,
                string.Empty,
                BuildOptions.None,
                ManifestorBuildStepTargets.Standard);
        }

        public static ManifestorResult Build(
            ManifestProfileSO profile,
            string outputFolderPath,
            BuildOptions options = BuildOptions.None)
        {
            return Start(
                profile,
                ManifestorBuildOperation.Build,
                outputFolderPath,
                options,
                ManifestorBuildStepTargets.Standard);
        }

        internal static ManifestorResult BuildFromBuildProfiles(
            ManifestProfileSO profile,
            BuildPlayerOptions buildPlayerOptions)
        {
            return Runner.Start(profile, buildPlayerOptions);
        }

        public static ManifestorResult Cancel()
        {
            return Runner.Cancel();
        }

        internal static ManifestorResult Start(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            string outputFolderPath,
            BuildOptions options,
            ManifestorBuildStepTargets targets)
        {
            return Runner.Start(
                profile,
                operation,
                outputFolderPath,
                options,
                targets);
        }

        private static void InvokeCompleted(
            ManifestorBuildOperation operation,
            ManifestorBuildPipelineStatus status)
        {
            var handlers = completed;
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                if (handler is not Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> buildHandler)
                {
                    Debug.LogError(
                        $"Handler is not of type Action<{nameof(ManifestorBuildOperation)}, " +
                        $"{nameof(ManifestorBuildPipelineStatus)}>.");
                    continue;
                }

                try
                {
                    buildHandler(operation, status);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
