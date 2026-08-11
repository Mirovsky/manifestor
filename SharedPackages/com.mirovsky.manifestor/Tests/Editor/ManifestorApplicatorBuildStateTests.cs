namespace Manifestor.Editor.Tests
{
    using System.Collections.Generic;
    using Build;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.Build.Profile;
    using UnityEngine;

    public sealed class ManifestorApplicatorBuildStateTests
    {
        private const string TestAssetFolder = "Assets/__ManifestorApplicatorBuildStateTests";
        private const string PreviousBuildProfilePath = TestAssetFolder + "/PreviousBuildProfile.asset";

        private BuildProfile _requestedBuildProfile;
        private BuildProfile _previousBuildProfile;
        private ManifestProfileSO _manifestProfile;
        private FakeEditorBuildState _buildState;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.CreateFolder("Assets", "__ManifestorApplicatorBuildStateTests");

            _requestedBuildProfile = ScriptableObject.CreateInstance<BuildProfile>();
            SetBuildTarget(_requestedBuildProfile, BuildTarget.StandaloneWindows64);

            _previousBuildProfile = ScriptableObject.CreateInstance<BuildProfile>();
            SetBuildTarget(_previousBuildProfile, BuildTarget.Android);
            AssetDatabase.CreateAsset(_previousBuildProfile, PreviousBuildProfilePath);

            _manifestProfile = ScriptableObject.CreateInstance<ManifestProfileSO>();
            var serializedProfile = new SerializedObject(_manifestProfile);
            serializedProfile.FindProperty("_buildProfile").objectReferenceValue = _requestedBuildProfile;
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();

            _buildState = new FakeEditorBuildState
            {
                activeBuildProfile = _previousBuildProfile,
                activeBuildTarget = BuildTarget.Android
            };
            ManifestorEditorBuildState.SetCurrentForTests(_buildState);
        }

        [TearDown]
        public void TearDown()
        {
            ManifestorEditorBuildState.ResetForTests();
            Object.DestroyImmediate(_manifestProfile);
            Object.DestroyImmediate(_requestedBuildProfile);
            AssetDatabase.DeleteAsset(TestAssetFolder);
        }

        [Test]
        public void ApplyBuildState_WhenTargetDiffers_ActivatesProfileAndTarget()
        {
            var success = ManifestorApplicator.TryActivateBuildState(
                _requestedBuildProfile,
                BuildTarget.StandaloneWindows64,
                out var error);

            Assert.That(success, Is.True, error);
            Assert.That(_buildState.activeBuildProfile, Is.SameAs(_requestedBuildProfile));
            Assert.That(_buildState.activeBuildTarget, Is.EqualTo(BuildTarget.StandaloneWindows64));
            Assert.That(_buildState.switchCallCount, Is.EqualTo(1));
        }

        [Test]
        public void AlreadyAppliedBuildState_WithStaleTarget_IsRejected()
        {
            _buildState.activeBuildProfile = _requestedBuildProfile;
            _buildState.activeBuildTarget = BuildTarget.Android;

            var isActive = ManifestorApplicator.IsRequestedBuildStateActive(
                _requestedBuildProfile,
                BuildTarget.StandaloneWindows64);

            Assert.That(isActive, Is.False);
        }

        [Test]
        public void DomainReloadDuringTargetSwitch_PreservesCheckpointAndRequestedState()
        {
            var checkpoint = JsonUtility.ToJson(new ManifestorApplicator.ApplyState
            {
                isActive = true,
                previousBuildProfilePath = PreviousBuildProfilePath,
                hasPreviousBuildTarget = true,
                previousBuildTarget = (int)BuildTarget.Android
            });
            Assert.That(ManifestorApplicator.TryActivateBuildState(
                _requestedBuildProfile,
                BuildTarget.StandaloneWindows64,
                out var activateError), Is.True, activateError);

            var reloadedBuildState = new FakeEditorBuildState
            {
                activeBuildProfile = _buildState.activeBuildProfile,
                activeBuildTarget = _buildState.activeBuildTarget
            };
            ManifestorEditorBuildState.SetCurrentForTests(reloadedBuildState);
            var loaded = ManifestorApplicator.TryLoadState(checkpoint, out var restoredState, out var loadError);

            Assert.That(loaded, Is.True, loadError);
            Assert.That(restoredState.isActive, Is.True);
            Assert.That((BuildTarget)restoredState.previousBuildTarget, Is.EqualTo(BuildTarget.Android));
            Assert.That(ManifestorApplicator.IsRequestedBuildStateActive(
                _requestedBuildProfile,
                BuildTarget.StandaloneWindows64), Is.True);
        }

        [Test]
        public void ApplyBuildState_WhenTargetSwitchFails_ReportsRequestedAndActualTargets()
        {
            _buildState.switchSucceeds = false;

            var success = ManifestorApplicator.TryActivateBuildState(
                _requestedBuildProfile,
                BuildTarget.StandaloneWindows64,
                out var error);

            Assert.That(success, Is.False);
            StringAssert.Contains("Unity rejected the target switch", error);
            StringAssert.Contains(nameof(BuildTarget.StandaloneWindows64), error);
            StringAssert.Contains(nameof(BuildTarget.Android), error);
        }

        [Test]
        public void ApplyRollback_RestoresPreviousProfileAndTarget()
        {
            Assert.That(ManifestorApplicator.TryActivateBuildState(
                _requestedBuildProfile,
                BuildTarget.StandaloneWindows64,
                out var activateError), Is.True, activateError);
            var errors = new List<string>();

            ManifestorApplicator.RestoreBuildState(CreatePreviousState(), errors);

            Assert.That(errors, Is.Empty);
            Assert.That(_buildState.activeBuildProfile, Is.SameAs(_previousBuildProfile));
            Assert.That(_buildState.activeBuildTarget, Is.EqualTo(BuildTarget.Android));
        }

        [Test]
        public void InterruptedApplyRecovery_RestoresPreviousProfileAndTargetFromCheckpoint()
        {
            var checkpoint = JsonUtility.ToJson(CreatePreviousState());
            _buildState.activeBuildProfile = _requestedBuildProfile;
            _buildState.activeBuildTarget = BuildTarget.StandaloneWindows64;
            Assert.That(ManifestorApplicator.TryLoadState(
                checkpoint,
                out var restoredState,
                out var loadError), Is.True, loadError);
            var errors = new List<string>();

            ManifestorApplicator.RestoreBuildState(restoredState, errors);

            Assert.That(errors, Is.Empty);
            Assert.That(_buildState.activeBuildProfile, Is.SameAs(_previousBuildProfile));
            Assert.That(_buildState.activeBuildTarget, Is.EqualTo(BuildTarget.Android));
        }

        [Test]
        public void InterruptedApplyRecovery_WhenPreviousProfileWasClassic_RestoresNullProfile()
        {
            _buildState.activeBuildProfile = _requestedBuildProfile;
            _buildState.activeBuildTarget = BuildTarget.StandaloneWindows64;
            var state = CreatePreviousState();
            state.previousBuildProfilePath = string.Empty;
            var errors = new List<string>();

            ManifestorApplicator.RestoreBuildState(state, errors);

            Assert.That(errors, Is.Empty);
            Assert.That(_buildState.activeBuildProfile, Is.Null);
            Assert.That(_buildState.activeBuildTarget, Is.EqualTo(BuildTarget.Android));
        }

        [Test]
        public void ApplyRollback_WhenTargetRestoreFails_StillRestoresProfileAndAggregatesError()
        {
            _buildState.activeBuildProfile = _requestedBuildProfile;
            _buildState.activeBuildTarget = BuildTarget.StandaloneWindows64;
            _buildState.switchSucceeds = false;
            var errors = new List<string>();

            ManifestorApplicator.RestoreBuildState(CreatePreviousState(), errors);

            Assert.That(errors, Has.Count.EqualTo(1));
            StringAssert.StartsWith("build target:", errors[0]);
            Assert.That(_buildState.activeBuildProfile, Is.SameAs(_previousBuildProfile));
        }

        [Test]
        public void BuildPlayerStep_WhenApplyInvariantIsBroken_FailsWithoutSwitchingTarget()
        {
            _buildState.activeBuildProfile = _requestedBuildProfile;
            _buildState.activeBuildTarget = BuildTarget.Android;
            var context = new ManifestorBuildContext(
                _manifestProfile,
                ManifestorBuildOperation.Build,
                default,
                false,
                string.Empty,
                null);

            var result = new BuildPlayerStep().Tick(context);

            Assert.That(result.outcome, Is.EqualTo(ManifestorBuildStepOutcome.Failed));
            StringAssert.Contains(nameof(BuildTarget.StandaloneWindows64), result.message);
            StringAssert.Contains(nameof(BuildTarget.Android), result.message);
            Assert.That(_buildState.switchCallCount, Is.Zero);
        }

        private static void SetBuildTarget(BuildProfile buildProfile, BuildTarget buildTarget)
        {
            var serializedProfile = new SerializedObject(buildProfile);
            serializedProfile.FindProperty("m_BuildTarget").intValue = (int)buildTarget;
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ManifestorApplicator.ApplyState CreatePreviousState()
        {
            return new ManifestorApplicator.ApplyState
            {
                isActive = true,
                previousBuildProfilePath = PreviousBuildProfilePath,
                hasPreviousBuildTarget = true,
                previousBuildTarget = (int)BuildTarget.Android
            };
        }

        private sealed class FakeEditorBuildState : IManifestorEditorBuildState
        {
            public BuildProfile activeBuildProfile { get; set; }
            public BuildTarget activeBuildTarget { get; set; }
            public bool switchSucceeds { get; set; } = true;
            public int switchCallCount { get; private set; }

            public void SetActiveBuildProfile(BuildProfile buildProfile)
            {
                activeBuildProfile = buildProfile;
            }

            public bool SwitchActiveBuildTarget(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget)
            {
                switchCallCount++;
                if (switchSucceeds)
                {
                    activeBuildTarget = buildTarget;
                }

                return switchSucceeds;
            }
        }
    }
}
