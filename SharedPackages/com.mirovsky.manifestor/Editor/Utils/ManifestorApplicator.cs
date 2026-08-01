namespace Manifestor
{
    using System;
    using System.Linq;
    using Build;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEditor.Build.Profile;
    using UnityEditor.PackageManager;
    using UnityEditor.PackageManager.Requests;
    using UnityEngine;

    public static class ManifestorApplicator
    {
        private const string StateKey = "Mirov.Manifestor.ManifestorApplicator.State";

        private static ListRequest _resolveRequest;

        public static bool isActive => LoadState().isActive;

        public static CustomBuildStepResult Execute(ManifestProfileSO profile)
        {
            var state = LoadState();
            if (!state.isActive)
            {
                var beginResult = Begin(profile, out state);
                if (beginResult.outcome != CustomBuildStepOutcome.Waiting)
                {
                    return beginResult;
                }
            }
            else if (profile == null || AssetDatabase.GetAssetPath(profile) != state.profilePath)
            {
                return CustomBuildStepResult.Failed("A different manifest profile apply transaction is already active.");
            }

            try
            {
                if (_resolveRequest == null)
                {
                    Client.Resolve();
                    _resolveRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
                }
                if (!_resolveRequest.IsCompleted)
                {
                    return CustomBuildStepResult.Waiting("Waiting for Unity Package Manager to resolve the manifest.");
                }

                if (_resolveRequest.Status != StatusCode.Success)
                {
                    var error = _resolveRequest.Error?.message ?? "Unknown package resolution error.";
                    return RollBack(state, $"Unity Package Manager failed to resolve the manifest: {error}");
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                ManifestorEditorPrefs.SetLastAppliedProfile(state.profilePath);
                ManifestorEditorPrefs.SetLastAppliedProfileFingerprint(state.profileFingerprint);
                ClearState();
                return CustomBuildStepResult.Succeeded($"Applied manifest profile '{profile.profileName}'.");
            }
            catch (Exception exception)
            {
                return RollBack(state, $"Failed to apply manifest profile: {exception.Message}");
            }
        }

        private static CustomBuildStepResult Begin(ManifestProfileSO profile, out ApplyState state)
        {
            state = new ApplyState();
            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                return CustomBuildStepResult.Failed(validation.message);
            }

            try
            {
                var profilePath = AssetDatabase.GetAssetPath(profile);
                var buildTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
                var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(buildTarget));
                var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
                state = new ApplyState
                {
                    isActive = true,
                    profilePath = profilePath,
                    profileFingerprint = ManifestorProfileFingerprint.Calculate(profile),
                    previousManifestExisted = ManifestorIO.ManifestExists(),
                    previousManifest = ManifestorIO.LoadManifestText(),
                    previousBuildProfilePath = activeBuildProfile == null
                        ? string.Empty
                        : AssetDatabase.GetAssetPath(activeBuildProfile),
                    definesBuildTarget = (int)buildTarget,
                    previousDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget)
                };
                state.hadPreviousAppliedProfile = ManifestorEditorPrefs.TryGetLastAppliedProfilePath(
                    out state.previousAppliedProfilePath);
                state.hadPreviousFingerprint = ManifestorEditorPrefs.TryGetLastAppliedProfileFingerprint(
                    out state.previousFingerprint);

                SaveState(state);
                BuildProfile.SetActiveBuildProfile(profile.buildProfile);
                ManifestorIO.SaveManifest(ManifestorIO.ConvertToManifest(profile));
                ApplyExactScriptingDefines(profile, namedBuildTarget);
                Client.Resolve();
                _resolveRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);
                return CustomBuildStepResult.Waiting("Waiting for Unity Package Manager to resolve the manifest.");
            }
            catch (Exception exception)
            {
                return state.isActive
                    ? RollBack(state, $"Failed to begin manifest apply: {exception.Message}")
                    : CustomBuildStepResult.Failed($"Failed to begin manifest apply: {exception.Message}");
            }
        }

        private static void ApplyExactScriptingDefines(ManifestProfileSO profile, NamedBuildTarget namedBuildTarget)
        {
            var defines = profile.packagesLists
                .SelectMany(packageList => packageList.defines ?? Array.Empty<string>())
                .Select(define => (define ?? string.Empty).Trim())
                .Where(define => !string.IsNullOrEmpty(define))
                .Distinct(StringComparer.Ordinal);
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, string.Join(";", defines));
        }

        private static CustomBuildStepResult RollBack(ApplyState state, string failureMessage)
        {
            var rollbackErrors = new System.Collections.Generic.List<string>();
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

            try
            {
                var previousProfile = string.IsNullOrEmpty(state.previousBuildProfilePath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<BuildProfile>(state.previousBuildProfilePath);
                BuildProfile.SetActiveBuildProfile(previousProfile);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"build profile: {exception.Message}");
            }

            try
            {
                PlayerSettings.SetScriptingDefineSymbols(
                    NamedBuildTarget.FromBuildTargetGroup(
                        BuildPipeline.GetBuildTargetGroup((BuildTarget)state.definesBuildTarget)),
                    state.previousDefines ?? string.Empty);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"scripting defines: {exception.Message}");
            }

            try
            {
                ManifestorEditorPrefs.RestoreLastAppliedProfile(
                    state.hadPreviousAppliedProfile,
                    state.previousAppliedProfilePath,
                    state.hadPreviousFingerprint,
                    state.previousFingerprint);
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"editor preferences: {exception.Message}");
            }

            ClearState();
            try
            {
                Client.Resolve();
            }
            catch (Exception exception)
            {
                rollbackErrors.Add($"package rollback resolve: {exception.Message}");
            }

            var rollbackSuffix = rollbackErrors.Count == 0
                ? string.Empty
                : " Rollback also failed for " + string.Join(", ", rollbackErrors) + ".";
            return CustomBuildStepResult.Failed(failureMessage + rollbackSuffix);
        }

        private static ApplyState LoadState()
        {
            var json = SessionState.GetString(StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new ApplyState();
            }

            try
            {
                return JsonUtility.FromJson<ApplyState>(json) ?? new ApplyState();
            }
            catch (Exception)
            {
                SessionState.EraseString(StateKey);
                return new ApplyState();
            }
        }

        private static void SaveState(ApplyState state)
        {
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        private static void ClearState()
        {
            _resolveRequest = null;
            SessionState.EraseString(StateKey);
        }

        [Serializable]
        private sealed class ApplyState
        {
            public bool isActive;
            public string profilePath;
            public string profileFingerprint;
            public bool previousManifestExisted;
            public string previousManifest;
            public string previousBuildProfilePath;
            public int definesBuildTarget;
            public string previousDefines;
            public bool hadPreviousAppliedProfile;
            public string previousAppliedProfilePath;
            public bool hadPreviousFingerprint;
            public string previousFingerprint;
        }
    }
}
