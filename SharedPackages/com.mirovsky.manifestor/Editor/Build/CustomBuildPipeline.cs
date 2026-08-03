namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    public enum CustomBuildPipelineStatus
    {
        Idle,
        Waiting,
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    public enum CustomBuildOperation
    {
        Apply,
        Build
    }

    [InitializeOnLoad]
    public static class CustomBuildPipeline
    {
        private static readonly CustomBuildRunner Runner = new(InvokeCompleted);

        public static bool isActive => CustomBuildRunner.isActive;

        public static event Action<CustomBuildOperation, CustomBuildPipelineStatus> completed;

        static CustomBuildPipeline()
        {
            CustomBuildScheduler.Initialize(Runner.Tick);
            if (Runner.Restore())
            {
                CustomBuildScheduler.Queue();
            }
        }

        public static bool TryGetOrderedSteps(out IReadOnlyList<Type> orderedSteps, out string error)
        {
            var success = CustomBuildStepOrderResolver.TryResolve(out var steps, out error);
            orderedSteps = steps.AsReadOnly();
            return success;
        }

        public static ManifestorResult Apply(ManifestProfileSO profile)
        {
            return Start(profile, CustomBuildOperation.Apply, string.Empty, BuildOptions.None);
        }

        public static ManifestorResult Build(
            ManifestProfileSO profile,
            string outputFolderPath,
            BuildOptions options = BuildOptions.None)
        {
            return Start(profile, CustomBuildOperation.Build, outputFolderPath, options);
        }

        public static ManifestorResult Cancel()
        {
            var result = Runner.Cancel();
            if (result.success)
            {
                CustomBuildScheduler.Queue();
            }

            return result;
        }

        private static ManifestorResult Start(
            ManifestProfileSO profile,
            CustomBuildOperation operation,
            string outputFolderPath,
            BuildOptions options)
        {
            if (CustomBuildRunner.isActive || BuildPipeline.isBuildingPlayer)
            {
                return ManifestorResult.Error("A custom build is already in progress.");
            }

            var planResult = CustomBuildPlanBuilder.TryCreate(
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
                CustomBuildScheduler.Queue();
            }

            return startResult;
        }

        private static void InvokeCompleted(
            CustomBuildOperation operation,
            CustomBuildPipelineStatus status)
        {
            var handlers = completed;
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                if (handler is not Action<CustomBuildOperation, CustomBuildPipelineStatus> buildHandler)
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
