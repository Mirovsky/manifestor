using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mirov.Manifestor.Editor.Tests
{
    public sealed class ManifestorServiceTests
    {
        [Test]
        public void ResolveProfile_UsesCommonPackagesAndPlatformOverrides()
        {
            var unityCommon = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            unityCommon.Packages.Add(new ManifestPackageEntry("com.unity.inputsystem", "1.18.0"));

            var common = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            common.Packages.Add(new ManifestPackageEntry("com.unity.inputsystem", "1.19.0"));
            common.Packages.Add(new ManifestPackageEntry("com.example.shared", "1.0.0", new[] { "SHARED_PACKAGE" }));
            common.Defines.Add("COMMON_DEFINE");

            var platform = ScriptableObject.CreateInstance<ManifestPlatformProfile>();
            platform.UnityPackagesProfile = unityCommon;
            platform.CommonProfile = common;
            platform.Packages.Add(new ManifestPackageEntry("com.example.shared", "2.0.0", new[] { "OVERRIDE_PACKAGE" }));
            platform.Packages.Add(new ManifestPackageEntry("com.example.disabled", "1.0.0") { Enabled = false });
            platform.Defines.Add("PLATFORM_DEFINE");

            var resolved = ManifestProfileResolver.Resolve(platform);

            Assert.That(resolved.Dependencies, Is.EqualTo(new Dictionary<string, string>
            {
                { "com.unity.inputsystem", "1.19.0" },
                { "com.example.shared", "2.0.0" }
            }));
            Assert.That(resolved.GeneratedDefines, Is.EquivalentTo(new[]
            {
                "COMMON_DEFINE",
                "PLATFORM_DEFINE",
                "OVERRIDE_PACKAGE"
            }));
        }

        [Test]
        public void ResolveProfile_UsesUnityCommonThenOtherCommonThenPlatformOverrides()
        {
            var unityCommon = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            unityCommon.Packages.Add(new ManifestPackageEntry("com.unity.render-pipelines.core", "17.0.0"));
            unityCommon.Packages.Add(new ManifestPackageEntry("com.example.shared", "1.0.0"));

            var otherCommon = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            otherCommon.Packages.Add(new ManifestPackageEntry("com.example.shared", "2.0.0"));
            otherCommon.Packages.Add(new ManifestPackageEntry("com.example.other", "1.0.0"));

            var platform = ScriptableObject.CreateInstance<ManifestPlatformProfile>();
            platform.UnityPackagesProfile = unityCommon;
            platform.CommonProfile = otherCommon;
            platform.Packages.Add(new ManifestPackageEntry("com.example.other", "3.0.0"));

            var resolved = ManifestProfileResolver.Resolve(platform);

            Assert.That(resolved.Dependencies, Is.EqualTo(new Dictionary<string, string>
            {
                { "com.unity.render-pipelines.core", "17.0.0" },
                { "com.example.shared", "2.0.0" },
                { "com.example.other", "3.0.0" }
            }));
        }

        [Test]
        public void ResolveProfile_UsesPackageLocationWhenPresent()
        {
            var common = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            common.Packages.Add(new ManifestPackageEntry("com.example.local", "1.0.0")
            {
                Location = "file:Packages/com.example.local"
            });

            var platform = ScriptableObject.CreateInstance<ManifestPlatformProfile>();
            platform.CommonProfile = common;

            var resolved = ManifestProfileResolver.Resolve(platform);

            Assert.That(resolved.Dependencies["com.example.local"], Is.EqualTo("file:Packages/com.example.local"));
        }

        [Test]
        public void ManifestGenerator_WritesMarkerAndDetectsHashMismatch()
        {
            const string existing = "{\"dependencies\":{\"com.old\":\"1.0.0\"},\"enableLockFile\":true}";
            var resolved = new ResolvedManifestProfile(
                "profile-guid",
                new Dictionary<string, string> { { "com.example.package", "file:Packages/com.example.package" } },
                new HashSet<string>());

            var generated = ManifestJsonGenerator.Generate(existing, resolved, "2026-06-16T08:00:00Z");
            var validation = ManifestJsonGenerator.ValidateGeneratedManifest(generated);
            var changedValidation = ManifestJsonGenerator.ValidateGeneratedManifest(generated.Replace("com.example.package", "com.example.changed"));

            StringAssert.Contains("\"manifestor\"", generated);
            StringAssert.Contains("\"enableLockFile\": true", generated);
            StringAssert.Contains("\"com.example.package\": \"file:Packages/com.example.package\"", generated);
            Assert.That(validation.CanOverwrite, Is.True);
            Assert.That(changedValidation.CanOverwrite, Is.False);
        }

        [Test]
        public void DefineGenerator_ReplacesOnlyManifestorGeneratedDefines()
        {
            var merged = ManifestDefineGenerator.MergeDefines(
                "USER_DEFINE;MANIFESTOR_OLD;OTHER_DEFINE",
                new[] { "NEW_DEFINE", "PACKAGE_DEFINE" });

            Assert.That(merged, Is.EqualTo("USER_DEFINE;OTHER_DEFINE;MANIFESTOR_NEW_DEFINE;MANIFESTOR_PACKAGE_DEFINE"));
        }

        [Test]
        public void EditorPrefsStore_RoundTripsLastAppliedProfile()
        {
            ManifestorEditorPrefs.SetLastAppliedProfile("Assets/ManifestorProfiles/Windows.asset");

            Assert.That(ManifestorEditorPrefs.TryGetLastAppliedProfile(out var path), Is.True);
            Assert.That(path, Is.EqualTo("Assets/ManifestorProfiles/Windows.asset"));

            ManifestorEditorPrefs.ClearLastAppliedProfile();

            Assert.That(ManifestorEditorPrefs.TryGetLastAppliedProfile(out _), Is.False);
        }

        [Test]
        public void DependencyManifestResolver_ResolvesLocalFilePackageManifest()
        {
            var path = DependencyManifestResolver.ResolvePackageManifestPath(
                "com.example.package",
                "file:Packages/com.example.package");

            Assert.That(path, Is.EqualTo("Packages/com.example.package/package.json"));
        }

        [Test]
        public void DependencyManifestResolver_ReturnsNullForUnavailablePackage()
        {
            var path = DependencyManifestResolver.ResolvePackageManifestPath(
                "com.example.missing",
                "1.0.0");

            Assert.That(path, Is.Null);
        }

        [Test]
        public void ValidateProfile_BlocksEnabledPackagesWithMissingValues()
        {
            var common = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            common.Packages.Add(new ManifestPackageEntry("com.example.valid", "1.0.0"));
            common.Packages.Add(new ManifestPackageEntry(string.Empty, "1.0.0"));

            var profile = ScriptableObject.CreateInstance<ManifestPlatformProfile>();
            profile.CommonProfile = common;
            profile.BuildProfile = ScriptableObject.CreateInstance<UnityEditor.Build.Profile.BuildProfile>();
            profile.NamedBuildTarget = "Standalone";

            var result = ManifestorApplier.ValidateProfile(profile);

            Assert.That(result.Success, Is.False);
            StringAssert.Contains("empty package name", result.Message);
        }

        [Test]
        public void ValidateProfile_AllowsMissingUnityPackagesProfile()
        {
            var common = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            common.Packages.Add(new ManifestPackageEntry("com.example.valid", "1.0.0"));

            var profile = ScriptableObject.CreateInstance<ManifestPlatformProfile>();
            profile.CommonProfile = common;
            profile.BuildProfile = ScriptableObject.CreateInstance<UnityEditor.Build.Profile.BuildProfile>();
            profile.NamedBuildTarget = "Standalone";

            var result = ManifestorApplier.ValidateProfile(profile);

            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void ValidateProfile_ValidatesUnityPackagesProfileWhenPresent()
        {
            var unityCommon = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            unityCommon.Packages.Add(new ManifestPackageEntry(string.Empty, "1.0.0"));

            var common = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            common.Packages.Add(new ManifestPackageEntry("com.example.valid", "1.0.0"));

            var profile = ScriptableObject.CreateInstance<ManifestPlatformProfile>();
            profile.UnityPackagesProfile = unityCommon;
            profile.CommonProfile = common;
            profile.BuildProfile = ScriptableObject.CreateInstance<UnityEditor.Build.Profile.BuildProfile>();
            profile.NamedBuildTarget = "Standalone";

            var result = ManifestorApplier.ValidateProfile(profile);

            Assert.That(result.Success, Is.False);
            StringAssert.Contains("unity packages profile", result.Message);
        }

        [Test]
        public void VersionSelector_UsesRecommendedVersionBeforeOtherVersions()
        {
            var versions = new ManifestPackageVersions(
                "1.0.0",
                new[] { "0.9.0", "1.0.0", "2.0.0" },
                "1.5.0",
                "1.6.0",
                "2.0.0");

            var selected = ManifestPackageVersionSelector.SelectRecommendedVersion(versions, "0.1.0");

            Assert.That(selected, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void VersionSelector_FallsBackToPackageVersionWhenMetadataIsUnavailable()
        {
            var versions = new ManifestPackageVersions(null, null, null, null, null);

            var selected = ManifestPackageVersionSelector.SelectRecommendedVersion(versions, "3.0.0");

            Assert.That(selected, Is.EqualTo("3.0.0"));
        }

        [Test]
        public void PackageListSerializedPropertyUtility_AddsNewPackageEntry()
        {
            var common = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            var serializedObject = new SerializedObject(common);
            var packages = serializedObject.FindProperty(nameof(ManifestCommonProfile.Packages));

            ManifestPackageListSerializedPropertyUtility.AddOrUpdate(
                packages,
                new ManifestPackageSelection("com.example.new", "Example New", "1.2.3", "Unity", "registry"));
            serializedObject.ApplyModifiedProperties();

            Assert.That(common.Packages, Has.Count.EqualTo(1));
            Assert.That(common.Packages[0].Enabled, Is.True);
            Assert.That(common.Packages[0].PackageName, Is.EqualTo("com.example.new"));
            Assert.That(common.Packages[0].Version, Is.EqualTo("1.2.3"));
            Assert.That(common.Packages[0].Location, Is.Empty);
            Assert.That(common.Packages[0].Defines, Is.Empty);
        }

        [Test]
        public void PackageListSerializedPropertyUtility_UpdatesExistingPackageEntryAndPreservesDefines()
        {
            var common = ScriptableObject.CreateInstance<ManifestCommonProfile>();
            common.Packages.Add(new ManifestPackageEntry("com.example.existing", "1.0.0")
            {
                Location = "file:../local",
                Defines = new List<string> { "KEEP_DEFINE" }
            });
            var serializedObject = new SerializedObject(common);
            var packages = serializedObject.FindProperty(nameof(ManifestCommonProfile.Packages));

            ManifestPackageListSerializedPropertyUtility.AddOrUpdate(
                packages,
                new ManifestPackageSelection("com.example.existing", "Example Existing", "2.0.0", "Unity", "registry"));
            serializedObject.ApplyModifiedProperties();

            Assert.That(common.Packages, Has.Count.EqualTo(1));
            Assert.That(common.Packages[0].Enabled, Is.True);
            Assert.That(common.Packages[0].PackageName, Is.EqualTo("com.example.existing"));
            Assert.That(common.Packages[0].Version, Is.EqualTo("2.0.0"));
            Assert.That(common.Packages[0].Location, Is.Empty);
            Assert.That(common.Packages[0].Defines, Is.EqualTo(new[] { "KEEP_DEFINE" }));
        }

        [Test]
        public void BuiltInPackageEntryFactory_CreatesSortedEntriesForBuiltInPackagesOnly()
        {
            var entries = ManifestBuiltInPackageEntryFactory.CreateEntries(new[]
            {
                new ManifestPackageSelection("com.example.registry", "Registry", "2.0.0", "Unity", "Registry"),
                new ManifestPackageSelection("com.unity.second", "Second", string.Empty, "Unity", "BuiltIn"),
                new ManifestPackageSelection("com.unity.first", "First", "1.2.3", "Unity", "BuiltIn")
            });

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].PackageName, Is.EqualTo("com.unity.first"));
            Assert.That(entries[0].Version, Is.EqualTo("1.2.3"));
            Assert.That(entries[0].Location, Is.Empty);
            Assert.That(entries[0].Defines, Is.Empty);
            Assert.That(entries[1].PackageName, Is.EqualTo("com.unity.second"));
            Assert.That(entries[1].Version, Is.EqualTo("1.0.0"));
        }
    }
}
