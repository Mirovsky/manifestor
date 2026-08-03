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
    public static class ManifestorBuildPipeline
    {
        private static readonly ManifestorBuildRunner Runner = new(InvokeCompleted);

        public static bool isActive => ManifestorBuildRunner.isActive;

        public static event Action<ManifestorBuildOperation, ManifestorBuildPipelineStatus> completed;

        static ManifestorBuildPipeline()
        {
            ManifestorBuildScheduler.Initialize(Runner.Tick);
            if (Runner.Restore())
            {
                ManifestorBuildScheduler.Queue();
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
            return Start(profile, ManifestorBuildOperation.Apply, string.Empty, BuildOptions.None);
        }

        public static ManifestorResult Build(
            ManifestProfileSO profile,
            string outputFolderPath,
            BuildOptions options = BuildOptions.None)
        {
            return Start(profile, ManifestorBuildOperation.Build, outputFolderPath, options);
        }

        public static ManifestorResult Cancel()
        {
            var result = Runner.Cancel();
            if (result.success)
            {
                ManifestorBuildScheduler.Queue();
            }

            return result;
        }

        private static ManifestorResult Start(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            string outputFolderPath,
            BuildOptions options)
        {
            if (ManifestorBuildRunner.isActive || BuildPipeline.isBuildingPlayer)
            {
                return ManifestorResult.Error("A custom build is already in progress.");
            }

            var planResult = ManifestorBuildPlanBuilder.TryCreate(
                profile,
                operation,
                outputFolderPath,
                options,
                out var state);
            if (!planResult.success)
            {
                return planResult;
            }

            var startResult = Runner.Start(state);
            if (startResult.success)
            {
                ManifestorBuildScheduler.Queue();
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
                    Debug.LogError("Handler is not of type Action<CustomBuildOperation, CustomBuildPipelineStatus>");
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
