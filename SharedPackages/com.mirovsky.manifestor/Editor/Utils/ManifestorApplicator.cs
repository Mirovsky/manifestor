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
        private static ListRequest _resolveRequest;

        public static CustomBuildStepResult Tick(CustomBuildContext context)
        {
            if (context?.profile == null)
            {
                return CustomBuildStepResult.Failed("Manifest profile is required.");
            }

            if (!TryLoadState(context.persistedState, out var state, out var stateError))
            {
                return CustomBuildStepResult.Failed(stateError);
            }

            if (context.cancellationRequested)
            {
                return state.isActive
                    ? RollBack(context, state, "Manifest apply was cancelled.", cancelled: true)
                    : CustomBuildStepResult.Cancelled("Manifest apply was cancelled before it started.");
            }

            if (!state.isActive)
            {
                var beginResult = Begin(context, out state);
                if (beginResult.outcome != CustomBuildStepOutcome.Waiting)
                {
                    return beginResult;
                }
            }
            else if (AssetDatabase.GetAssetPath(context.profile) != state.profilePath)
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
                    return RollBack(context, state, $"Unity Package Manager failed to resolve the manifest: {error}");
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                ManifestorSettings.instance.SetLastAppliedManifest(state.profilePath, state.profileFingerprint);
                ClearState(context);

                return CustomBuildStepResult.Succeeded($"Applied manifest profile '{context.profile.profileName}'.");
            }
            catch (Exception exception)
            {
                return RollBack(context, state, $"Failed to apply manifest profile: {exception.Message}");
            }
        }

        public static CustomBuildStepResult HandleInterruption(CustomBuildContext context)
        {
            if (context == null)
            {
                return CustomBuildStepResult.Failed("Manifest apply context is required.");
            }

            if (!TryLoadState(context.persistedState, out var state, out var stateError))
            {
                return CustomBuildStepResult.Failed(stateError);
            }

            return state.isActive
                ? RollBack(context, state, "Interrupted manifest apply was rolled back.", cancelled: true)
                : CustomBuildStepResult.Cancelled("Manifest apply was interrupted before project state changed.");
        }

        private static CustomBuildStepResult Begin(CustomBuildContext context, out ApplyState state)
        {
            state = new ApplyState();
            var profile = context.profile;

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
                    previousDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget),
                    hadPreviousAppliedProfile = ManifestorSettings.instance.TryGetLastAppliedProfilePath(out state.previousAppliedProfilePath),
                    hadPreviousFingerprint = ManifestorSettings.instance.TryGetLastAppliedProfileFingerprint(out state.previousFingerprint)
                };

                context.SaveCheckpoint(JsonUtility.ToJson(state));

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
                    ? RollBack(context, state, $"Failed to begin manifest apply: {exception.Message}")
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

        private static CustomBuildStepResult RollBack(
            CustomBuildContext context,
            ApplyState state,
            string failureMessage,
            bool cancelled = false)
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

            ClearState(context);
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
            return cancelled
                ? CustomBuildStepResult.Cancelled(failureMessage + rollbackSuffix)
                : CustomBuildStepResult.Failed(failureMessage + rollbackSuffix);
        }

        private static bool TryLoadState(string json, out ApplyState state, out string error)
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

        private static void ClearState(CustomBuildContext context)
        {
            _resolveRequest = null;
            context.SaveCheckpoint(string.Empty);
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
