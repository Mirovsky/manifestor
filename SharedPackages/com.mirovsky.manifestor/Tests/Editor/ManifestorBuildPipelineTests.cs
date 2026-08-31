namespace Manifestor.Editor.Tests
{
    using System.Linq;
    using Build;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class ManifestorBuildPipelineTests
    {
        [TearDown]
        public void TearDown()
        {
            SessionState.EraseString(ManifestorBuildPipelineStateStore.StateKey);
        }

        [Test]
        public void Resolve_FullOrderPlacesApplyBeforePostBuild()
        {
            var success = ManifestorBuildStepOrderResolver.TryResolve(
                new[] { typeof(PostBuildTestStep), typeof(ApplyManifestBuildStep) },
                out var steps,
                out var error);

            Assert.That(success, Is.True, error);
            Assert.That(steps.IndexOf(typeof(ApplyManifestBuildStep)), Is.LessThan(steps.IndexOf(typeof(PostBuildTestStep))));
        }

        [Test]
        public void FilterForApply_PreservesResolvedSubsequence()
        {
            var resolved = new[]
            {
                typeof(ApplyManifestBuildStep),
                typeof(PostBuildTestStep)
            };

            var applySteps = ManifestorBuildExecution.FilterForOperation(resolved, ManifestorBuildOperation.Apply);

            Assert.That(applySteps, Is.EqualTo(resolved.Take(1)));
        }

        [Test]
        public void Waiting_StoresRequestedRetryDelay()
        {
            var result = ManifestorBuildStepResult.Waiting("Resolving", 2.5d);

            Assert.That(result.outcome, Is.EqualTo(ManifestorBuildStepOutcome.Waiting));
            Assert.That(result.retryAfterSeconds, Is.EqualTo(2.5d));
            Assert.That(result.success, Is.False);
        }

        [Test]
        public void Context_SaveCheckpointPersistsStateAndBuildOptions()
        {
            string savedState = null;
            BuildPlayerOptions savedOptions = default;
            var context = new ManifestorBuildContext(
                null,
                ManifestorBuildOperation.Build,
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
            ManifestorBuildPipelineStateStore.Save(new ManifestorBuildPipelineState
            {
                isActive = true,
                status = ManifestorBuildPipelineStatus.Running,
                actions = new System.Collections.Generic.List<ManifestorBuildAction>
                {
                    ManifestorBuildAction.Step(typeof(ApplyManifestBuildStep))
                },
                currentActionStarted = true
            });
            var completed = false;
            var runner = new ManifestorBuildRunner((_, status) =>
            {
                completed = status == ManifestorBuildPipelineStatus.Failed;
            });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("was interrupted"));

            runner.Restore();

            Assert.That(completed, Is.True);
            Assert.That(ManifestorBuildPipelineStateStore.Load().isActive, Is.False);
            Assert.That(ManifestorBuildPipelineStateStore.Load().status, Is.EqualTo(ManifestorBuildPipelineStatus.Failed));
        }

        [Test]
        public void Cancel_RecordsRequestForActivePipeline()
        {
            ManifestorBuildPipelineStateStore.Save(new ManifestorBuildPipelineState
            {
                isActive = true,
                status = ManifestorBuildPipelineStatus.Waiting
            });
            var runner = new ManifestorBuildRunner(null);

            var result = runner.Cancel();

            Assert.That(result.success, Is.True);
            Assert.That(ManifestorBuildPipelineStateStore.Load().cancellationRequested, Is.True);
            LogAssert.Expect(LogType.Warning, "Custom build was cancelled.");
            runner.Tick();
        }

        [Test]
        public void Load_UnsupportedStateVersionIsClearedAsFailure()
        {
            SessionState.SetString(
                ManifestorBuildPipelineStateStore.StateKey,
                JsonUtility.ToJson(new ManifestorBuildPipelineState { version = ManifestorBuildPipelineState.CurrentVersion + 1 }));

            var state = ManifestorBuildPipelineStateStore.Load();

            Assert.That(state.isActive, Is.False);
            Assert.That(state.status, Is.EqualTo(ManifestorBuildPipelineStatus.Failed));
            Assert.That(SessionState.GetString(ManifestorBuildPipelineStateStore.StateKey, string.Empty), Is.Empty);
        }

        [ManifestorBuildStep(ManifestorBuildStepCategory.PostBuild)]
        private sealed class PostBuildTestStep : IManifestorBuildStep
        {
            public PostBuildTestStep()
            {
            }

            public ManifestorBuildStepResult Tick(ManifestorBuildContext context)
            {
                return ManifestorBuildStepResult.Succeeded();
            }
        }
    }
}
