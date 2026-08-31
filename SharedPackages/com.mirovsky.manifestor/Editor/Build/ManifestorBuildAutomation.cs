namespace Manifestor.Build
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEngine;

    public static class ManifestorBuildAutomation
    {
        public const string ProfilePathEnvironmentVariable = "MANIFESTOR_PROFILE_PATH";

        private const int ReceiptVersion = 1;
        private const string ReceiptRelativePath = "Library/Manifestor/build-automation.json";
        private const string PackageName = "com.mirovsky.manifestor";

        internal static bool isBootstrapPending =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProfilePathEnvironmentVariable)) &&
            File.Exists(GetReceiptPath());

        public static void Bootstrap()
        {
            if (!Application.isBatchMode)
            {
                throw new InvalidOperationException(
                    $"{nameof(Bootstrap)} can only run from a batch-mode Unity process.");
            }

            try
            {
                var profilePath = GetProfilePathFromEnvironment();
                var profile = LoadAndValidateProfile(profilePath);
                EnsureManifestorRemainsAvailable(profile);

                var profileFingerprint = ManifestorProfileFingerprint.Calculate(profile);
                var buildTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
                ManifestorProfileMaterializer.ApplyManifestAndDefines(profile);
                AssetDatabase.SaveAssets();

                var receipt = new BuildAutomationReceipt
                {
                    version = ReceiptVersion,
                    profilePath = profilePath,
                    profileFingerprint = profileFingerprint,
                    manifestFingerprint = CalculateFingerprint(ManifestorIO.LoadManifestText()),
                    definesFingerprint = CalculateFingerprint(ManifestorProfileMaterializer.GetDefinesString(profile)),
                    buildTarget = (int)buildTarget
                };
                SaveReceipt(receipt);

                Debug.Log($"Manifestor prepared profile '{profilePath}' for Unity Build Automation.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Manifestor Build Automation bootstrap failed: {exception.Message}");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void PreExport()
        {
            try
            {
                var profilePath = GetProfilePathFromEnvironment();
                var receipt = LoadReceipt();
                if (!string.Equals(profilePath, receipt.profilePath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Bootstrap prepared profile '{receipt.profilePath}', but " +
                        $"{ProfilePathEnvironmentVariable} now selects '{profilePath}'.");
                }

                var profile = LoadAndValidateProfile(profilePath);
                VerifyBootstrapState(profile, receipt);
                ActivateBuildProfile(profile);
                RunPreBuildSteps(profile, receipt.profileFingerprint);

                ManifestorSettings.instance.SetLastAppliedManifest(profilePath, receipt.profileFingerprint);
                DeleteReceipt();
                Debug.Log($"Manifestor pre-export completed for profile '{profilePath}'.");
            }
            catch (BuildFailedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new BuildFailedException($"Manifestor pre-export failed: {exception.Message}");
            }
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
                (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) &&
                 !string.Equals(assetPath, "Assets", StringComparison.Ordinal)) ||
                !string.Equals(Path.GetExtension(assetPath), ".asset", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{ProfilePathEnvironmentVariable} must be a project-relative .asset path under Assets. " +
                    $"Actual value: '{configuredPath}'.");
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new InvalidOperationException("Could not determine the Unity project root.");
            }

            var fullAssetPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
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
                throw new InvalidOperationException(
                    $"Manifest profile could not be loaded from '{profilePath}'.");
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
                    $"Profile '{AssetDatabase.GetAssetPath(profile)}' removes '{PackageName}'. " +
                    "Build Automation requires Manifestor to remain installed for the second Unity process, " +
                    "unless it is embedded under Packages.");
            }
        }

        private static void VerifyBootstrapState(ManifestProfileSO profile, BuildAutomationReceipt receipt)
        {
            if (receipt.version != ReceiptVersion)
            {
                throw new InvalidOperationException(
                    $"Bootstrap receipt version '{receipt.version}' is unsupported.");
            }

            var profileFingerprint = ManifestorProfileFingerprint.Calculate(profile);
            if (!string.Equals(profileFingerprint, receipt.profileFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected manifest profile changed after bootstrap.");
            }

            if (!ManifestorProfileMaterializer.HasExpectedManifest(profile, out var manifestError))
            {
                throw new InvalidOperationException($"The package manifest does not match the selected profile: {manifestError}");
            }

            var manifestFingerprint = CalculateFingerprint(ManifestorIO.LoadManifestText());
            if (!string.Equals(manifestFingerprint, receipt.manifestFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Packages/manifest.json changed after bootstrap.");
            }

            if (!ManifestorProfileMaterializer.HasExpectedDefines(profile, out var definesError))
            {
                throw new InvalidOperationException($"Scripting defines do not match the selected profile: {definesError}");
            }

            var definesFingerprint = CalculateFingerprint(ManifestorProfileMaterializer.GetDefinesString(profile));
            if (!string.Equals(definesFingerprint, receipt.definesFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected profile's scripting defines changed after bootstrap.");
            }

            var expectedTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
            if ((int)expectedTarget != receipt.buildTarget)
            {
                throw new InvalidOperationException("The selected profile's build target changed after bootstrap.");
            }

            if (ManifestorEditorBuildState.activeBuildTarget != expectedTarget)
            {
                throw new InvalidOperationException(
                    $"Unity Build Automation started target '{ManifestorEditorBuildState.activeBuildTarget}', " +
                    $"but profile '{AssetDatabase.GetAssetPath(profile)}' requires '{expectedTarget}'.");
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

            if (!ManifestorProfileMaterializer.HasExpectedManifest(profile, out var manifestError))
            {
                throw new InvalidOperationException(
                    $"Activating the Unity Build Profile changed package state: {manifestError}");
            }

            if (!ManifestorProfileMaterializer.HasExpectedDefines(profile, out var definesError))
            {
                throw new InvalidOperationException(
                    $"Activating the Unity Build Profile changed compile-time defines: {definesError}");
            }
        }

        private static void RunPreBuildSteps(ManifestProfileSO profile, string profileFingerprint)
        {
            if (!ManifestorBuildStepOrderResolver.TryResolve(out var orderedSteps, out var orderError))
            {
                throw new InvalidOperationException(orderError);
            }

            var preBuildSteps = ManifestorBuildPlanBuilder.FilterForCategory(
                orderedSteps,
                ManifestorBuildStepCategory.PreBuild);
            var context = new ManifestorBuildContext(
                profile,
                ManifestorBuildOperation.PreBuild,
                default,
                false,
                string.Empty,
                null);

            foreach (var stepType in preBuildSteps)
            {
                var currentFingerprint = ManifestorProfileFingerprint.Calculate(profile);
                if (!string.Equals(currentFingerprint, profileFingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Manifest profile changed before pre-build step '{stepType.FullName}'.");
                }

                ManifestorBuildStepResult result;
                try
                {
                    var step = (IManifestorBuildStep)Activator.CreateInstance(stepType);
                    result = step.Tick(context);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Pre-build step '{stepType.FullName}' threw an exception: {exception.Message}",
                        exception);
                }

                if (result.outcome == ManifestorBuildStepOutcome.Waiting)
                {
                    throw new InvalidOperationException(
                        $"Pre-build step '{stepType.FullName}' returned Waiting. " +
                        "Unity Build Automation pre-export steps must complete synchronously.");
                }

                if (!result.success)
                {
                    var message = string.IsNullOrEmpty(result.message)
                        ? "No failure message was provided."
                        : result.message;
                    throw new InvalidOperationException(
                        $"Pre-build step '{stepType.FullName}' returned {result.outcome}: {message}");
                }
            }
        }

        private static BuildAutomationReceipt LoadReceipt()
        {
            var receiptPath = GetReceiptPath();
            if (!File.Exists(receiptPath))
            {
                throw new InvalidOperationException(
                    "The Build Automation bootstrap receipt is missing. " +
                    "Configure the Manifestor bootstrap as the UBA Pre-Build Script.");
            }

            try
            {
                var receipt = JsonUtility.FromJson<BuildAutomationReceipt>(File.ReadAllText(receiptPath));
                return receipt ?? throw new InvalidOperationException("The bootstrap receipt is empty.");
            }
            catch (Exception exception) when (exception is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Could not read the Build Automation bootstrap receipt: {exception.Message}",
                    exception);
            }
        }

        private static void SaveReceipt(BuildAutomationReceipt receipt)
        {
            var receiptPath = GetReceiptPath();
            var directory = Path.GetDirectoryName(receiptPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = receiptPath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(receipt, prettyPrint: true), new UTF8Encoding(false));
                if (File.Exists(receiptPath))
                {
                    File.Replace(temporaryPath, receiptPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporaryPath, receiptPath);
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
            var receiptPath = GetReceiptPath();
            if (File.Exists(receiptPath))
            {
                File.Delete(receiptPath);
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

        [Serializable]
        private sealed class BuildAutomationReceipt
        {
            public int version;
            public string profilePath;
            public string profileFingerprint;
            public string manifestFingerprint;
            public string definesFingerprint;
            public int buildTarget;
        }
    }
}
