namespace Manifestor.Editor.Tests
{
    using Build;
    using Newtonsoft.Json;
    using NUnit.Framework;
    using SerializedData;
    using UnityEditor;
    using UnityEngine;

    public sealed class ManifestorServiceTests
    {
        [Test]
        public void PackageList_AddUpdateAndRemovePackage()
        {
            var packageList = ScriptableObject.CreateInstance<PackagesListSO>();

            Assert.That(packageList.AddPackage(" com.example.package ", " 1.0.0 "), Is.True);
            Assert.That(packageList.AddPackage("com.example.package", "2.0.0"), Is.False);
            Assert.That(packageList.UpdatePackage("com.example.package", "1.0.0", "2.0.0"), Is.True);
            Assert.That(packageList.RemovePackage("com.example.package", "2.0.0"), Is.True);
            Assert.That(packageList.packages, Is.Empty);
        }

        [Test]
        public void ConvertToManifest_NormalizesValuesAndDeduplicatesRegistries()
        {
            var first = CreatePackageList((" com.example.package ", " 1.2.3 "));
            var second = CreatePackageList();
            first.AddScopedRegistry(" Example ", " https://example.test ", new[] { " com.example ", "com.example" });
            second.AddScopedRegistry("Example", "https://example.test", new[] { "com.example" });
            var profile = CreateProfile(" Windows ", first, second);

            var manifest = ManifestorIO.ConvertToManifest(profile);

            Assert.That(manifest.manifestorData.name, Is.EqualTo("Windows"));
            Assert.That(manifest.dependencies["com.example.package"], Is.EqualTo("1.2.3"));
            Assert.AreEqual(manifest.scopedRegistries.Count, 1);
            Assert.That(manifest.scopedRegistries[0].scopes, Is.EqualTo(new[] { "com.example" }));
        }

        [Test]
        public void ProjectManifest_DeserializesReadOnlyCollectionViews()
        {
            const string json = "{\"dependencies\":{\"com.example\":\"1.0.0\"},\"scopedRegistries\":[],\"testables\":[],\"pinnedPackages\":[]}";

            var manifest = JsonConvert.DeserializeObject<ProjectManifest>(json);

            Assert.That(manifest.dependencies["com.example"], Is.EqualTo("1.0.0"));
            Assert.That(manifest.scopedRegistries, Is.Empty);
        }

        [Test]
        public void CustomBuildStepResult_WaitingIsNotSuccessful()
        {
            var result = CustomBuildStepResult.Waiting("Resolving");

            Assert.That(result.outcome, Is.EqualTo(CustomBuildStepOutcome.Waiting));
            Assert.That(result.success, Is.False);
        }

        private static PackagesListSO CreatePackageList(params (string packageName, string location)[] packages)
        {
            var packageList = ScriptableObject.CreateInstance<PackagesListSO>();
            foreach (var package in packages)
            {
                packageList.AddPackage(package.packageName, package.location);
            }

            return packageList;
        }

        private static ManifestProfileSO CreateProfile(string profileName, params PackagesListSO[] packageLists)
        {
            var profile = ScriptableObject.CreateInstance<ManifestProfileSO>();
            var serializedObject = new SerializedObject(profile);
            serializedObject.FindProperty("_profileName").stringValue = profileName;
            var listsProperty = serializedObject.FindProperty("_packageLists");
            listsProperty.arraySize = packageLists.Length;
            for (var index = 0; index < packageLists.Length; index++)
            {
                listsProperty.GetArrayElementAtIndex(index).objectReferenceValue = packageLists[index];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }
    }
}
