namespace Manifestor.Editor.Tests
{
    using System.Linq;
    using Build;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class CustomBuildPipelineTests
    {
        [TearDown]
        public void TearDown()
        {
            SessionState.EraseString(CustomBuildPipelineStateStore.StateKey);
        }

        [Test]
        public void Resolve_FullOrderPlacesConstrainedStepsBeforeBuildPlayer()
        {
            var success = CustomBuildStepOrderResolver.TryResolve(
                new[] { typeof(BuildPlayerStep), typeof(ApplyManifestBuildStep) },
                out var steps,
                out var error);

            Assert.That(success, Is.True, error);
            Assert.That(steps.IndexOf(typeof(ApplyManifestBuildStep)), Is.LessThan(steps.IndexOf(typeof(BuildPlayerStep))));
        }

        [Test]
        public void FilterForApply_PreservesResolvedSubsequence()
        {
            var resolved = new[]
            {
                typeof(ApplyManifestBuildStep),
                typeof(BuildPlayerStep)
            };

            var applySteps = CustomBuildPlanBuilder.FilterForOperation(resolved, CustomBuildOperation.Apply);

            Assert.That(applySteps, Is.EqualTo(resolved.Take(1)));
        }

        [Test]
        public void Waiting_StoresRequestedRetryDelay()
        {
            var result = CustomBuildStepResult.Waiting("Resolving", 2.5d);

            Assert.That(result.outcome, Is.EqualTo(CustomBuildStepOutcome.Waiting));
            Assert.That(result.retryAfterSeconds, Is.EqualTo(2.5d));
            Assert.That(result.success, Is.False);
        }

        [Test]
        public void Context_SaveCheckpointPersistsStateAndBuildOptions()
        {
            string savedState = null;
            BuildPlayerOptions savedOptions = default;
            var context = new CustomBuildContext(
                null,
                CustomBuildOperation.Build,
                new BuildPlayerOptions { locationPathName = "initial" },
                false,
                string.Empty,
                (state, options) =>
                {
                    savedState = state;
                    savedOptions = options;
                });
            context.buildPlayerOptions = new BuildPlayerOptions { locationPathName = "updated" };

            context.SaveCheckpoint("checkpoint");

            Assert.That(context.persistedState, Is.EqualTo("checkpoint"));
            Assert.That(savedState, Is.EqualTo("checkpoint"));
            Assert.That(savedOptions.locationPathName, Is.EqualTo("updated"));
        }

        [Test]
        public void Restore_RunningStateFailsWithoutExecutingStep()
        {
            CustomBuildPipelineStateStore.Save(new CustomBuildPipelineState
            {
                isActive = true,
                status = CustomBuildPipelineStatus.Running,
                currentStepTypeName = typeof(ApplyManifestBuildStep).AssemblyQualifiedName
            });
            var completed = false;
            var runner = new CustomBuildRunner((_, status) =>
            {
                completed = status == CustomBuildPipelineStatus.Failed;
            });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("was interrupted"));

            var shouldResume = runner.Restore();

            Assert.That(shouldResume, Is.False);
            Assert.That(completed, Is.True);
            Assert.That(CustomBuildPipelineStateStore.Load().isActive, Is.False);
            Assert.That(CustomBuildPipelineStateStore.Load().status, Is.EqualTo(CustomBuildPipelineStatus.Failed));
        }

        [Test]
        public void Cancel_RecordsRequestForActivePipeline()
        {
            CustomBuildPipelineStateStore.Save(new CustomBuildPipelineState
            {
                isActive = true,
                status = CustomBuildPipelineStatus.Waiting
            });
            var runner = new CustomBuildRunner(null);

            var result = runner.Cancel();

            Assert.That(result.success, Is.True);
            Assert.That(CustomBuildPipelineStateStore.Load().cancellationRequested, Is.True);
        }

        [Test]
        public void Load_UnsupportedStateVersionIsClearedAsFailure()
        {
            SessionState.SetString(
                CustomBuildPipelineStateStore.StateKey,
                JsonUtility.ToJson(new CustomBuildPipelineState { version = CustomBuildPipelineState.CurrentVersion + 1 }));

            var state = CustomBuildPipelineStateStore.Load();

            Assert.That(state.isActive, Is.False);
            Assert.That(state.status, Is.EqualTo(CustomBuildPipelineStatus.Failed));
            Assert.That(SessionState.GetString(CustomBuildPipelineStateStore.StateKey, string.Empty), Is.Empty);
        }
    }
}
