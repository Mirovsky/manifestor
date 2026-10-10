namespace Manifestor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Build;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEditor.Build.Profile;
    using UnityEditor.PackageManager;
    using UnityEditor.PackageManager.Requests;
    using UnityEngine;

    public static class ManifestorApplicator
    {
        private const int ApplyJournalVersion = 1;
        private const string ApplyJournalRelativePath = "Library/Manifestor/apply-journal.json";
        private static ListRequest _resolveRequest;

        internal static bool hasPendingRecovery => File.Exists(GetApplyJournalPath());

        internal static ManifestorResult RecoverInterruptedApplyIfNeeded(bool sessionStateIsActive)
        {
            if (!hasPendingRecovery || sessionStateIsActive)
            {
                return ManifestorResult.Ok();
            }

            ApplyJournal journal;
            try
            {
                journal = JsonUtility.FromJson<ApplyJournal>(File.ReadAllText(GetApplyJournalPath()));
                if (journal == null || journal.version != ApplyJournalVersion || journal.state == null ||
                    !journal.state.isActive)
                {
                    return ManifestorResult.Error("The manifest apply recovery journal is invalid.");
                }
            }
            catch (Exception exception)
            {
                return ManifestorResult.Error($"Could not read the manifest apply recovery journal: {exception.Message}");
            }

            var errors = new List<string>();
            RestorePreviousState(journal.state, errors);
            ResolveRestoredManifest(errors);
            if (errors.Count == 0)
            {
                try
                {
                    DeleteApplyJournal();
                }
                catch (Exception exception)
                {
                    errors.Add($"recovery journal: {exception.Message}");
                }
            }

            if (errors.Count > 0)
            {
                return ManifestorResult.Error("Manifest apply recovery failed for " + string.Join(", ", errors) + ".");
            }

            Debug.LogWarning("An interrupted manifest apply was rolled back from its recovery journal.");
            return ManifestorResult.Ok();
        }

        public static ManifestorBuildStepResult Apply(ManifestorBuildContext context)
        {
            if (context?.profile == null)
            {
                return ManifestorBuildStepResult.Failed("Manifest profile is required.");
            }

            if (!TryLoadState(context.persistedState, out var state, out var stateError))
            {
                return ManifestorBuildStepResult.Failed(stateError);
            }

            if (context.cancellationRequested)
            {
                return state.isActive
                    ? RollBack(context, state, "Manifest apply was cancelled.", cancelled: true)
                    : ManifestorBuildStepResult.Cancelled("Manifest apply was cancelled before it started.");
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return ManifestorBuildStepResult.Waiting("Waiting for the Unity Editor to finish updating.");
            }

            if (!state.isActive)
            {
                var beginResult = Begin(context, out state);
                if (beginResult.outcome != ManifestorBuildStepOutcome.Waiting)
                {
                    return beginResult;
                }
            }
            else if (AssetDatabase.GetAssetPath(context.profile) != state.profilePath)
            {
                return ManifestorBuildStepResult.Failed("A different manifest profile apply transaction is already active.");
            }

            try
            {
                if (_resolveRequest == null)
                {
                    if (!state.resolveIssued)
                    {
                        Client.Resolve();
                        state.resolveIssued = true;
                        context.SaveCheckpoint(JsonUtility.ToJson(state));
                    }

                    _resolveRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
                }
                if (!_resolveRequest.IsCompleted)
                {
                    return ManifestorBuildStepResult.Waiting("Waiting for Unity Package Manager to resolve the manifest.");
                }

                if (_resolveRequest.Status != StatusCode.Success)
                {
                    var error = _resolveRequest.Error?.message ?? "Unknown package resolution error.";
                    return RollBack(context, state, $"Unity Package Manager failed to resolve the manifest: {error}");
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                var requestedBuildTarget = BuildProfileUtility.GetBuildTarget(context.profile.buildProfile);
                if (!IsRequestedBuildStateActive(context.profile.buildProfile, requestedBuildTarget))
                {
                    return RollBack(
                        context,
                        state,
                        CreateBuildStateMismatchMessage(context.profile.buildProfile, requestedBuildTarget));
                }

                ManifestorSettings.instance.SetLastAppliedManifest(state.profilePath, state.profileFingerprint);
                ClearState(context);
                DeleteApplyJournal();

                return ManifestorBuildStepResult.Succeeded($"Applied manifest profile '{context.profile.profileName}'.");
            }
            catch (Exception exception)
            {
                return RollBack(context, state, $"Failed to apply manifest profile: {exception.Message}");
            }
        }

        public static ManifestorBuildStepResult HandleInterruption(ManifestorBuildContext context)
        {
            if (context == null)
            {
                return ManifestorBuildStepResult.Failed("Manifest apply context is required.");
            }

            if (!TryLoadState(context.persistedState, out var state, out var stateError))
            {
                return ManifestorBuildStepResult.Failed(stateError);
            }

            return state.isActive
                ? RollBack(context, state, "Interrupted manifest apply was rolled back.", cancelled: true)
                : ManifestorBuildStepResult.Cancelled("Manifest apply was interrupted before project state changed.");
        }

        private static ManifestorBuildStepResult Begin(ManifestorBuildContext context, out ApplyState state)
        {
            state = new ApplyState();
            if (hasPendingRecovery)
            {
                return ManifestorBuildStepResult.Failed(
                    "An unfinished manifest apply recovery journal must be resolved before applying another profile.");
            }

            var profile = context.profile;

            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                return ManifestorBuildStepResult.Failed(validation.message);
            }

            try
            {
                var profilePath = AssetDatabase.GetAssetPath(profile);
                var buildTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
                var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(buildTarget));
                var activeBuildProfile = ManifestorEditorBuildState.activeBuildProfile;
                var profileFingerprint = ManifestorProfileFingerprint.Calculate(profile);
                var applyReasons = GetApplyReasons(
                    profile,
                    profilePath,
                    profileFingerprint,
                    activeBuildProfile,
                    buildTarget);
                if (applyReasons.Count == 0)
                {
                    ClearState(context);
                    return ManifestorBuildStepResult.Succeeded(
                        $"Manifest profile '{profile.profileName}' is already applied.");
                }

                Debug.Log(
                    $"Manifest profile '{profile.profileName}' must be applied because: " +
                    $"{string.Join("; ", applyReasons)}.");

                state = new ApplyState
                {
                    isActive = true,
                    profilePath = profilePath,
                    profileFingerprint = profileFingerprint,
                    previousManifestExisted = ManifestorIO.ManifestExists(),
                    previousManifest = ManifestorIO.LoadManifestText(),
                    previousBuildProfilePath = activeBuildProfile == null
                        ? string.Empty
                        : AssetDatabase.GetAssetPath(activeBuildProfile),
                    hasPreviousBuildTarget = true,
                    previousBuildTarget = (int)ManifestorEditorBuildState.activeBuildTarget,
                    definesBuildTarget = (int)buildTarget,
                    previousDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget),
                    hadPreviousAppliedProfile = ManifestorSettings.instance.TryGetLastAppliedProfilePath(out state.previousAppliedProfilePath),
                    hadPreviousFingerprint = ManifestorSettings.instance.TryGetLastAppliedProfileFingerprint(out state.previousFingerprint)
                };

                SaveApplyJournal(state);
                context.SaveCheckpoint(JsonUtility.ToJson(state));

                if (!TryActivateBuildState(profile.buildProfile, buildTarget, out var buildStateError))
                {
                    return RollBack(context, state, buildStateError);
                }

                ManifestorProfileMaterializer.ApplyManifestAndDefines(profile);
                Client.Resolve();
                state.resolveIssued = true;
                context.SaveCheckpoint(JsonUtility.ToJson(state));
                _resolveRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
                return ManifestorBuildStepResult.Waiting("Waiting for Unity Package Manager to resolve the manifest.");
            }
            catch (Exception exception)
            {
                return state.isActive
                    ? RollBack(context, state, $"Failed to begin manifest apply: {exception.Message}")
                    : ManifestorBuildStepResult.Failed($"Failed to begin manifest apply: {exception.Message}");
            }
        }

        private static IReadOnlyList<string> GetApplyReasons(
            ManifestProfileSO profile,
            string profilePath,
            string profileFingerprint,
            BuildProfile activeBuildProfile,
            BuildTarget requestedBuildTarget)
        {
            var reasons = new List<string>();
            if (!ManifestorSettings.instance.TryGetLastAppliedProfilePath(out var appliedProfilePath))
            {
                reasons.Add("no manifest profile is recorded as applied");
            }
            else if (!string.Equals(appliedProfilePath, profilePath, StringComparison.Ordinal))
            {
                reasons.Add($"a different manifest profile is recorded as applied ('{appliedProfilePath}')");
            }

            if (!ManifestorSettings.instance.TryGetLastAppliedProfileFingerprint(out var appliedFingerprint))
            {
                reasons.Add("the applied profile fingerprint is missing");
            }
            else if (!string.Equals(appliedFingerprint, profileFingerprint, StringComparison.Ordinal))
            {
                reasons.Add("the profile or its referenced dependencies changed");
            }

            if (activeBuildProfile != profile.buildProfile)
            {
                var activeBuildProfilePath = activeBuildProfile == null
                    ? "<classic>"
                    : AssetDatabase.GetAssetPath(activeBuildProfile);
                reasons.Add($"the active Build Profile is '{activeBuildProfilePath}'");
            }

            if (ManifestorEditorBuildState.activeBuildTarget != requestedBuildTarget)
            {
                reasons.Add(
                    $"the active build target is '{ManifestorEditorBuildState.activeBuildTarget}' instead of " +
                    $"'{requestedBuildTarget}'");
            }

            reasons.AddRange(ManifestorIO.GetGeneratedManifestMismatchReasons(profile));

            if (!ManifestorProfileMaterializer.HasExpectedDefines(profile, out _))
            {
                reasons.Add("the scripting define symbols changed");
            }

            return reasons;
        }

        internal static bool TryActivateBuildState(
            BuildProfile buildProfile,
            BuildTarget requestedBuildTarget,
            out string error)
        {
            ManifestorEditorBuildState.SetActiveBuildProfile(buildProfile);

            var requestedBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(requestedBuildTarget);
            if (ManifestorEditorBuildState.activeBuildTarget != requestedBuildTarget &&
                !ManifestorEditorBuildState.SwitchActiveBuildTarget(requestedBuildTargetGroup, requestedBuildTarget))
            {
                error = CreateBuildStateMismatchMessage(
                    buildProfile,
                    requestedBuildTarget,
                    "Unity rejected the target switch.");
                return false;
            }

            if (!IsRequestedBuildStateActive(buildProfile, requestedBuildTarget))
            {
                error = CreateBuildStateMismatchMessage(buildProfile, requestedBuildTarget);
                return false;
            }

            error = string.Empty;
            return true;
        }

        internal static bool IsRequestedBuildStateActive(BuildProfile buildProfile, BuildTarget requestedBuildTarget)
        {
            return ManifestorEditorBuildState.activeBuildProfile == buildProfile &&
                   ManifestorEditorBuildState.activeBuildTarget == requestedBuildTarget;
        }

        internal static string CreateBuildStateMismatchMessage(
            BuildProfile buildProfile,
            BuildTarget requestedBuildTarget,
            string reason = null)
        {
            var requestedProfilePath = AssetDatabase.GetAssetPath(buildProfile);
            var activeProfile = ManifestorEditorBuildState.activeBuildProfile;
            var activeProfilePath = activeProfile == null ? "<classic>" : AssetDatabase.GetAssetPath(activeProfile);
            var reasonPrefix = string.IsNullOrEmpty(reason) ? string.Empty : reason + " ";
            return $"{reasonPrefix}Manifest apply requires Build Profile '{requestedProfilePath}' and target " +
                   $"'{requestedBuildTarget}', but the active Build Profile is '{activeProfilePath}' and target is " +
                   $"'{ManifestorEditorBuildState.activeBuildTarget}'.";
        }

        private static ManifestorBuildStepResult RollBack(
            ManifestorBuildContext context,
            ApplyState state,
            string failureMessage,
            bool cancelled = false)
        {
            var rollbackErrors = new List<string>();
            RestorePreviousState(state, rollbackErrors);
            ResolveRestoredManifest(rollbackErrors);
            try
            {
                ClearState(context);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"apply checkpoint: {exception.Message}");
            }

            if (rollbackErrors.Count == 0)
            {
                try
                {
                    DeleteApplyJournal();
                }
                catch (Exception exception)
                {
                    rollbackErrors.Add($"recovery journal: {exception.Message}");
                }
            }

            var rollbackSuffix = rollbackErrors.Count == 0
                ? string.Empty
                : " Rollback also failed for " + string.Join(", ", rollbackErrors) + ".";
            return cancelled
                ? ManifestorBuildStepResult.Cancelled(failureMessage + rollbackSuffix)
                : ManifestorBuildStepResult.Failed(failureMessage + rollbackSuffix);
        }

        private static void RestorePreviousState(ApplyState state, ICollection<string> rollbackErrors)
        {
            try
            {
                if (state.previousManifestExisted)
                {
                    ManifestorIO.SaveManifestTextAtomic(state.previousManifest);
                }
                else
                {
                    ManifestorIO.DeleteManifest();
                }
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"manifest: {exception.Message}");
            }

            RestoreBuildState(state, rollbackErrors);

            try
            {
                PlayerSettings.SetScriptingDefineSymbols(
                    NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup((BuildTarget)state.definesBuildTarget)),
                    state.previousDefines ?? string.Empty);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"scripting defines: {exception.Message}");
            }

            try
            {
                ManifestorSettings.instance.RestoreLastAppliedProfile(
                    state.hadPreviousAppliedProfile,
                    state.previousAppliedProfilePath,
                    state.hadPreviousFingerprint,
                    state.previousFingerprint);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"editor preferences: {exception.Message}");
            }
        }

        private static void ResolveRestoredManifest(ICollection<string> rollbackErrors)
        {
            _resolveRequest = null;
            try
            {
                Client.Resolve();
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"package rollback resolve: {exception.Message}");
            }
        }

        private static void SaveApplyJournal(ApplyState state)
        {
            var path = GetApplyJournalPath();
            if (File.Exists(path))
            {
                throw new InvalidOperationException("An unfinished manifest apply recovery journal already exists.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var temporaryPath = path + ".tmp";
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonUtility.ToJson(new ApplyJournal { version = ApplyJournalVersion, state = state }),
                    new UTF8Encoding(false));
                File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void DeleteApplyJournal()
        {
            var path = GetApplyJournalPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string GetApplyJournalPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("Could not find the Unity project root for apply recovery.");
            }

            return Path.Combine(projectRoot, ApplyJournalRelativePath);
        }

        internal static void RestoreBuildState(
            ApplyState state,
            ICollection<string> rollbackErrors)
        {
            try
            {
                var previousProfile = string.IsNullOrEmpty(state.previousBuildProfilePath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<BuildProfile>(state.previousBuildProfilePath);
                if (!string.IsNullOrEmpty(state.previousBuildProfilePath) && previousProfile == null)
                {
                    throw new InvalidOperationException(
                        $"Build Profile could not be loaded from '{state.previousBuildProfilePath}'.");
                }

                ManifestorEditorBuildState.SetActiveBuildProfile(previousProfile);
                if (ManifestorEditorBuildState.activeBuildProfile != previousProfile)
                {
                    var actualProfile = ManifestorEditorBuildState.activeBuildProfile;
                    throw new InvalidOperationException(
                        $"Expected '{state.previousBuildProfilePath}', actual " +
                        $"'{(actualProfile == null ? "<classic>" : AssetDatabase.GetAssetPath(actualProfile))}'.");
                }
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"build profile: {exception.Message}");
            }

            try
            {
                if (!state.hasPreviousBuildTarget)
                {
                    throw new InvalidOperationException("The apply checkpoint does not contain the previous build target.");
                }

                var previousBuildTarget = (BuildTarget)state.previousBuildTarget;
                if (ManifestorEditorBuildState.activeBuildTarget != previousBuildTarget &&
                    (!ManifestorEditorBuildState.SwitchActiveBuildTarget(
                         BuildPipeline.GetBuildTargetGroup(previousBuildTarget),
                         previousBuildTarget) ||
                     ManifestorEditorBuildState.activeBuildTarget != previousBuildTarget))
                {
                    throw new InvalidOperationException(
                        $"Expected '{previousBuildTarget}', actual '{ManifestorEditorBuildState.activeBuildTarget}'.");
                }
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"build target: {exception.Message}");
            }
        }

        internal static bool TryLoadState(string json, out ApplyState state, out string error)
        {
            if (string.IsNullOrEmpty(json))
            {
                state = new ApplyState();
                error = string.Empty;
                return true;
            }

            try
            {
                state = JsonUtility.FromJson<ApplyState>(json);
                if (state == null)
                {
                    error = "Manifest apply checkpoint was empty.";
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                state = new ApplyState();
                error = $"Failed to restore manifest apply checkpoint: {exception.Message}";
                return false;
            }
        }

        private static void ClearState(ManifestorBuildContext context)
        {
            _resolveRequest = null;
            context.SaveCheckpoint(string.Empty);
        }

        [Serializable]
        private sealed class ApplyJournal
        {
            public int version;
            public ApplyState state;
        }

        [Serializable]
        internal sealed class ApplyState
        {
            public bool isActive;
            public bool resolveIssued;
            public string profilePath;
            public string profileFingerprint;
            public bool previousManifestExisted;
            public string previousManifest;
            public string previousBuildProfilePath;
            public bool hasPreviousBuildTarget;
            public int previousBuildTarget;
            public int definesBuildTarget;
            public string previousDefines;
            public bool hadPreviousAppliedProfile;
            public string previousAppliedProfilePath;
            public bool hadPreviousFingerprint;
            public string previousFingerprint;
        }
    }
}
