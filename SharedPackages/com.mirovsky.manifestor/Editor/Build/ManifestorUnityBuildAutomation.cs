namespace Manifestor.Build
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEngine;

    [InitializeOnLoad]
    public static class ManifestorUnityBuildAutomation
    {
        public const string ProfilePathEnvironmentVariable = "MANIFESTOR_PROFILE_PATH";

        private const int ReceiptVersion = 3;
        private const string ReceiptRelativePath = "Library/Manifestor/unity-build-automation.json";
        private const string PackageName = "com.mirovsky.manifestor";
        private const string ApplyingPhase = "Applying";
        private const string AppliedPhase = "Applied";
        private const string PreBuiltPhase = "PreBuilt";

        internal static bool isApplyPending =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProfilePathEnvironmentVariable)) &&
            File.Exists(GetReceiptPath());

        static ManifestorUnityBuildAutomation()
        {
            if (!Application.isBatchMode || !TryLoadReceipt(out var receipt) || receipt.phase != ApplyingPhase)
            {
                return;
            }

            SubscribeToApplyCompletion();
        }

        public static void Apply()
        {
            if (!Application.isBatchMode)
            {
                FailApply("Apply can only run from a batch-mode Unity process.");
                return;
            }

            try
            {
                var profilePath = GetProfilePathFromEnvironment();
                var profile = LoadAndValidateProfile(profilePath);
                EnsureManifestorRemainsAvailable(profile);
                SaveReceipt(new UnityBuildAutomationReceipt
                {
                    version = ReceiptVersion,
                    phase = ApplyingPhase,
                    profilePath = profilePath,
                    profileFingerprint = ManifestorProfileFingerprint.Calculate(profile)
                });

                SubscribeToApplyCompletion();
                var result = ManifestorUnityEditorPipeline.Start(
                    profile,
                    ManifestorBuildOperation.Apply,
                    string.Empty,
                    BuildOptions.None,
                    ManifestorBuildStepTargets.UnityBuildAutomation);
                if (!result.success)
                {
                    FailApply(result.message);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                FailApply(exception.Message);
            }
        }

        public static void PreBuild()
        {
            UnityBuildAutomationReceipt receipt = null;
            try
            {
                receipt = LoadReceipt();
                var profile = VerifyReceipt(receipt, AppliedPhase);
                ActivateBuildProfile(profile);
                VerifyApplyState(profile, receipt);
                RunCategory(profile, receipt, ManifestorBuildStepCategory.PreBuild);
                PreparePlayerBuild(profile, receipt);
                receipt.originalEditorBuildSettingsScenes = SerializableEditorBuildSettingsScene.From(
                    EditorBuildSettings.scenes);
                receipt.hasOriginalEditorBuildSettingsScenes = true;
                SaveReceipt(receipt);
                ManifestorPlayerBuildPreparation.ApplyScenesToEditorBuildSettings(
                    receipt.buildPlayerOptions?.scenes);
                receipt.phase = PreBuiltPhase;
                SaveReceipt(receipt);
                Debug.Log($"Manifestor UBA pre-build completed for '{receipt.profilePath}'.");
            }
            catch (Exception exception)
            {
                TryRestoreEditorBuildSettingsScenes(receipt);
                DeleteReceipt();
                throw exception is BuildFailedException
                    ? exception
                    : new BuildFailedException($"Manifestor UBA pre-build failed: {exception.Message}");
            }
        }

        public static void PostBuild(string exportPath)
        {
            UnityBuildAutomationReceipt receipt = null;
            try
            {
                receipt = LoadReceipt();
                var profile = VerifyReceipt(receipt, PreBuiltPhase);
                var buildPlayerOptions = receipt.buildPlayerOptions?.ToBuildPlayerOptions() ?? default;
                buildPlayerOptions.locationPathName = exportPath ?? string.Empty;
                receipt.buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions);
                RunCategory(profile, receipt, ManifestorBuildStepCategory.PostBuild);
                ManifestorSettings.instance.SetLastAppliedManifest(receipt.profilePath, receipt.profileFingerprint);
                RestoreEditorBuildSettingsScenes(receipt);
                DeleteReceipt();
                Debug.Log($"Manifestor UBA post-build completed for '{receipt.profilePath}'.");
            }
            catch (Exception exception)
            {
                TryRestoreEditorBuildSettingsScenes(receipt);
                DeleteReceipt();
                throw exception is BuildFailedException
                    ? exception
                    : new BuildFailedException($"Manifestor UBA post-build failed: {exception.Message}");
            }
        }

        private static void SubscribeToApplyCompletion()
        {
            ManifestorUnityEditorPipeline.completed -= HandleApplyCompleted;
            ManifestorUnityEditorPipeline.completed += HandleApplyCompleted;
        }

        private static void HandleApplyCompleted(
            ManifestorBuildOperation operation,
            ManifestorBuildPipelineStatus status)
        {
            if (operation != ManifestorBuildOperation.Apply ||
                !TryLoadReceipt(out var receipt) ||
                receipt.phase != ApplyingPhase)
            {
                return;
            }

            ManifestorUnityEditorPipeline.completed -= HandleApplyCompleted;
            if (status != ManifestorBuildPipelineStatus.Succeeded)
            {
                FailApply($"The shared Apply pipeline finished with status {status}.");
                return;
            }

            try
            {
                var profile = LoadAndValidateProfile(receipt.profilePath);
                var pipelineState = ManifestorBuildPipelineStateStore.Load();
                var buildTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
                receipt.phase = AppliedPhase;
                receipt.profileFingerprint = ManifestorProfileFingerprint.Calculate(profile);
                receipt.manifestFingerprint = CalculateFingerprint(ManifestorIO.LoadManifestText());
                receipt.definesFingerprint = CalculateFingerprint(ManifestorProfileMaterializer.GetDefinesString(profile));
                receipt.buildTarget = (int)buildTarget;
                var buildPlayerOptions = pipelineState.buildPlayerOptions?.ToBuildPlayerOptions() ?? default;
                if (buildPlayerOptions.target is 0 or BuildTarget.NoTarget)
                {
                    buildPlayerOptions.target = buildTarget;
                    buildPlayerOptions.subtarget = BuildProfileUtility.GetSubtarget(profile.buildProfile);
                }

                if (buildPlayerOptions.targetGroup == BuildTargetGroup.Unknown)
                {
                    buildPlayerOptions.targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
                }

                receipt.buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions);
                receipt.userData = pipelineState.userData ?? new SerializableBuildUserData();
                SaveReceipt(receipt);
                Debug.Log($"Manifestor prepared profile '{receipt.profilePath}' for Unity Build Automation.");
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                FailApply(exception.Message);
            }
        }

        private static void RunCategory(
            ManifestProfileSO profile,
            UnityBuildAutomationReceipt receipt,
            ManifestorBuildStepCategory category)
        {
            var result = ManifestorBuildStepExecutor.RunCategorySynchronously(
                profile,
                category,
                ManifestorBuildStepTargets.UnityBuildAutomation,
                receipt.buildPlayerOptions?.ToBuildPlayerOptions() ?? default,
                receipt.userData?.ToDictionary(),
                out var buildPlayerOptions,
                out var userData);
            if (!result.success)
            {
                throw new BuildFailedException(
                    string.IsNullOrEmpty(result.message)
                        ? $"Manifestor {category} steps failed."
                        : result.message);
            }

            receipt.buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions);
            receipt.userData = SerializableBuildUserData.From(userData);
        }

        private static void PreparePlayerBuild(
            ManifestProfileSO profile,
            UnityBuildAutomationReceipt receipt)
        {
            var context = new ManifestorBuildContext(
                profile,
                ManifestorBuildOperation.Build,
                receipt.buildPlayerOptions?.ToBuildPlayerOptions() ?? default,
                false,
                string.Empty,
                null,
                receipt.userData?.ToDictionary());
            var result = ManifestorPlayerBuildPreparation.Prepare(
                context,
                ManifestorBuildStepTargets.UnityBuildAutomation);
            if (!result.success)
            {
                throw new BuildFailedException(
                    string.IsNullOrEmpty(result.message)
                        ? "Manifestor player build preparation failed."
                        : result.message);
            }

            receipt.buildPlayerOptions = SerializableBuildPlayerOptions.From(context.buildPlayerOptions);
        }

        private static void RestoreEditorBuildSettingsScenes(UnityBuildAutomationReceipt receipt)
        {
            if (receipt == null || !receipt.hasOriginalEditorBuildSettingsScenes)
            {
                return;
            }

            EditorBuildSettings.scenes = SerializableEditorBuildSettingsScene.ToEditorBuildSettingsScenes(
                receipt.originalEditorBuildSettingsScenes);
            receipt.hasOriginalEditorBuildSettingsScenes = false;
        }

        private static void TryRestoreEditorBuildSettingsScenes(UnityBuildAutomationReceipt receipt)
        {
            try
            {
                RestoreEditorBuildSettingsScenes(receipt);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Manifestor could not restore Editor Build Settings scenes after UBA: {exception.Message}");
            }
        }

        private static ManifestProfileSO VerifyReceipt(
            UnityBuildAutomationReceipt receipt,
            string expectedPhase)
        {
            var profilePath = GetProfilePathFromEnvironment();
            if (receipt.phase != expectedPhase)
            {
                throw new InvalidOperationException(
                    $"Expected UBA phase '{expectedPhase}', actual '{receipt.phase}'.");
            }

            if (!string.Equals(profilePath, receipt.profilePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"UBA Apply prepared '{receipt.profilePath}', but the environment selects '{profilePath}'.");
            }

            var profile = LoadAndValidateProfile(profilePath);
            VerifyApplyState(profile, receipt);
            return profile;
        }

        private static string GetProfilePathFromEnvironment()
        {
            var configuredPath = Environment.GetEnvironmentVariable(ProfilePathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidOperationException(
                    $"Environment variable {ProfilePathEnvironmentVariable} is required.");
            }

            var assetPath = configuredPath.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(assetPath) ||
                !assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(assetPath), ".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ProfilePathEnvironmentVariable} must be a project-relative .asset path under Assets.");
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var fullAssetPath = Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, assetPath));
            var assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            if (!fullAssetPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ProfilePathEnvironmentVariable} resolves outside the Assets directory.");
            }

            return "Assets/" + Path.GetRelativePath(Application.dataPath, fullAssetPath).Replace('\\', '/');
        }

        private static ManifestProfileSO LoadAndValidateProfile(string profilePath)
        {
            var profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(profilePath);
            if (profile == null)
            {
                throw new InvalidOperationException($"Manifest profile could not be loaded from '{profilePath}'.");
            }

            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                throw new InvalidOperationException(validation.message);
            }

            return profile;
        }

        private static void EnsureManifestorRemainsAvailable(ManifestProfileSO profile)
        {
            var embeddedPackagePath = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                "Packages",
                PackageName,
                "package.json");
            var generatedManifest = ManifestorIO.ConvertToManifest(profile);
            if (!File.Exists(embeddedPackagePath) &&
                (generatedManifest.dependencies == null || !generatedManifest.dependencies.ContainsKey(PackageName)))
            {
                throw new InvalidOperationException(
                    $"Profile '{AssetDatabase.GetAssetPath(profile)}' removes '{PackageName}', which UBA still needs.");
            }
        }

        private static void VerifyApplyState(ManifestProfileSO profile, UnityBuildAutomationReceipt receipt)
        {
            if (receipt.version != ReceiptVersion)
            {
                throw new InvalidOperationException($"UBA receipt version '{receipt.version}' is unsupported.");
            }

            if (!string.Equals(
                    ManifestorProfileFingerprint.Calculate(profile),
                    receipt.profileFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected manifest profile changed after UBA Apply.");
            }

            if (!ManifestorProfileMaterializer.HasExpectedManifest(profile, out var manifestError) ||
                !string.Equals(
                    CalculateFingerprint(ManifestorIO.LoadManifestText()),
                    receipt.manifestFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The package manifest changed after UBA Apply: {manifestError}");
            }

            if (!ManifestorProfileMaterializer.HasExpectedDefines(profile, out var definesError) ||
                !string.Equals(
                    CalculateFingerprint(ManifestorProfileMaterializer.GetDefinesString(profile)),
                    receipt.definesFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Scripting defines changed after UBA Apply: {definesError}");
            }

            var expectedTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
            if ((int)expectedTarget != receipt.buildTarget || ManifestorEditorBuildState.activeBuildTarget != expectedTarget)
            {
                throw new InvalidOperationException(
                    $"UBA target '{ManifestorEditorBuildState.activeBuildTarget}' does not match '{expectedTarget}'.");
            }
        }

        private static void ActivateBuildProfile(ManifestProfileSO profile)
        {
            var expectedTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
            ManifestorEditorBuildState.SetActiveBuildProfile(profile.buildProfile);
            if (!ManifestorApplicator.IsRequestedBuildStateActive(profile.buildProfile, expectedTarget))
            {
                throw new InvalidOperationException(
                    ManifestorApplicator.CreateBuildStateMismatchMessage(profile.buildProfile, expectedTarget));
            }
        }

        private static UnityBuildAutomationReceipt LoadReceipt()
        {
            if (!TryLoadReceipt(out var receipt))
            {
                throw new InvalidOperationException(
                    "The UBA receipt is missing or invalid. Configure ManifestorUnityBuildAutomation.Apply as the pre-build shell step.");
            }

            return receipt;
        }

        private static bool TryLoadReceipt(out UnityBuildAutomationReceipt receipt)
        {
            receipt = null;
            var path = GetReceiptPath();
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                receipt = JsonUtility.FromJson<UnityBuildAutomationReceipt>(File.ReadAllText(path));
                return receipt != null && receipt.version == ReceiptVersion;
            }
            catch
            {
                return false;
            }
        }

        private static void SaveReceipt(UnityBuildAutomationReceipt receipt)
        {
            var path = GetReceiptPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            var temporaryPath = path + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(receipt, true), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void DeleteReceipt()
        {
            var path = GetReceiptPath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string GetReceiptPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? ReceiptRelativePath
                : Path.Combine(projectRoot, ReceiptRelativePath);
        }

        private static string CalculateFingerprint(string value)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void FailApply(string message)
        {
            DeleteReceipt();
            Debug.LogError($"Manifestor UBA Apply failed: {message}");
            EditorApplication.Exit(1);
        }

        [Serializable]
        private sealed class UnityBuildAutomationReceipt
        {
            public int version;
            public string phase;
            public string profilePath;
            public string profileFingerprint;
            public string manifestFingerprint;
            public string definesFingerprint;
            public int buildTarget;
            public SerializableBuildPlayerOptions buildPlayerOptions = new();
            public SerializableBuildUserData userData = new();
            public bool hasOriginalEditorBuildSettingsScenes;
            public SerializableEditorBuildSettingsScene[] originalEditorBuildSettingsScenes =
                Array.Empty<SerializableEditorBuildSettingsScene>();
        }

        [Serializable]
        private sealed class SerializableEditorBuildSettingsScene
        {
            public string path;
            public bool enabled;

            public static SerializableEditorBuildSettingsScene[] From(EditorBuildSettingsScene[] scenes)
            {
                if (scenes == null)
                {
                    return Array.Empty<SerializableEditorBuildSettingsScene>();
                }

                var serializedScenes = new SerializableEditorBuildSettingsScene[scenes.Length];
                for (var index = 0; index < scenes.Length; index++)
                {
                    serializedScenes[index] = new SerializableEditorBuildSettingsScene
                    {
                        path = scenes[index].path,
                        enabled = scenes[index].enabled
                    };
                }

                return serializedScenes;
            }

            public static EditorBuildSettingsScene[] ToEditorBuildSettingsScenes(
                SerializableEditorBuildSettingsScene[] scenes)
            {
                if (scenes == null)
                {
                    return Array.Empty<EditorBuildSettingsScene>();
                }

                var editorScenes = new EditorBuildSettingsScene[scenes.Length];
                for (var index = 0; index < scenes.Length; index++)
                {
                    var scene = scenes[index];
                    editorScenes[index] = new EditorBuildSettingsScene(
                        scene?.path ?? string.Empty,
                        scene?.enabled ?? false);
                }

                return editorScenes;
            }
        }
    }
}
