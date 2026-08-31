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

        public static bool isActive => ManifestorBuildRunner.isActive;

        public static event Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> completed;

        static ManifestorUnityEditorPipeline()
        {
            if (Runner.Restore())
            {
                Runner.Queue();
            }
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

        public static ManifestorResult Cancel()
        {
            var result = Runner.Cancel();
            if (result.success)
            {
                Runner.Queue();
            }

            return result;
        }

        internal static ManifestorResult Start(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            string outputFolderPath,
            BuildOptions options,
            ManifestorBuildStepTargets targets)
        {
            if (ManifestorBuildRunner.isActive || BuildPipeline.isBuildingPlayer)
            {
                return ManifestorResult.Error("A custom build is already in progress.");
            }

            var planResult = ManifestorBuildExecution.TryCreatePlan(
                profile,
                operation,
                outputFolderPath,
                options,
                targets,
                out var state);
            if (!planResult.success)
            {
                return planResult;
            }

            var startResult = Runner.Start(state);
            if (startResult.success)
            {
                Runner.Queue();
            }

            return startResult;
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
