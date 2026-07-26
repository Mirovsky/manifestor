using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mirov.Manifestor.Editor.Tests
{
    public sealed class ManifestPackageDiffUtilityTests
    {
        [Test]
        public void Compare_ReturnsMissingPackageWhenManifestContainsPackageListDoesNot()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.manifest", "1.0.0" } },
                new[] { CreatePackageList(("com.example.list", "1.0.0")) });

            Assert.That(result.MissingInPackageLists.Select(change => change.PackageName), Is.EqualTo(new[] { "com.example.manifest" }));
            Assert.That(result.MissingInPackageLists[0].ManifestValue, Is.EqualTo("1.0.0"));
            Assert.That(result.MissingInPackageLists[0].PackageListValue, Is.Empty);
            Assert.That(result.MissingInPackageLists[0].ChangeKind, Is.EqualTo(ManifestPackageChangeKind.MissingInPackageLists));
        }

        [Test]
        public void Compare_ReturnsRemovedPackageWhenPackageListContainsPackageManifestDoesNot()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.manifest", "1.0.0" } },
                new[] { CreatePackageList(("com.example.removed", "1.0.0")) });

            Assert.That(result.RemovedFromManifest.Select(change => change.PackageName), Is.EqualTo(new[] { "com.example.removed" }));
            Assert.That(result.RemovedFromManifest[0].ManifestValue, Is.Empty);
            Assert.That(result.RemovedFromManifest[0].PackageListValue, Is.EqualTo("1.0.0"));
            Assert.That(result.RemovedFromManifest[0].ChangeKind, Is.EqualTo(ManifestPackageChangeKind.RemovedFromManifest));
        }

        [Test]
        public void Compare_ReturnsNoChangesWhenManifestAndPackageListMatch()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.same", "1.0.0" } },
                new[] { CreatePackageList(("com.example.same", "1.0.0")) });

            Assert.That(result.HasChanges, Is.False);
            Assert.That(result.AllChanges, Is.Empty);
        }

        [Test]
        public void Compare_ClassifiesHigherManifestVersionAsUpdated()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.package", "2.0.0" } },
                new[] { CreatePackageList(("com.example.package", "1.0.0")) });

            Assert.That(result.Changed.Select(change => change.ChangeKind), Is.EqualTo(new[] { ManifestPackageChangeKind.Updated }));
        }

        [Test]
        public void Compare_ClassifiesLowerManifestVersionAsDowngraded()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.package", "1.0.0" } },
                new[] { CreatePackageList(("com.example.package", "2.0.0")) });

            Assert.That(result.Changed.Select(change => change.ChangeKind), Is.EqualTo(new[] { ManifestPackageChangeKind.Downgraded }));
        }

        [Test]
        public void Compare_ClassifiesDifferentNonVersionValuesAsChanged()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.package", "file:../Packages/com.example.package" } },
                new[] { CreatePackageList(("com.example.package", "1.0.0")) });

            Assert.That(result.Changed.Select(change => change.ChangeKind), Is.EqualTo(new[] { ManifestPackageChangeKind.Changed }));
        }

        [Test]
        public void Compare_ReturnsDistinctChangedPackageListValuesForDuplicatePackageNames()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.package", "2.0.0" } },
                new[]
                {
                    CreatePackageList(("com.example.package", "1.0.0")),
                    CreatePackageList(("com.example.package", "2.0.0"))
                });

            Assert.That(result.Changed, Has.Count.EqualTo(1));
            Assert.That(result.Changed[0].PackageListValue, Is.EqualTo("1.0.0"));
            Assert.That(result.Changed[0].ChangeKind, Is.EqualTo(ManifestPackageChangeKind.Updated));
        }

        private static PackagesListSO CreatePackageList(params (string packageName, string location)[] packages)
        {
            var packageList = ScriptableObject.CreateInstance<PackagesListSO>();
            var serializedObject = new SerializedObject(packageList);
            var packagesProperty = serializedObject.FindProperty("_packages");
            packagesProperty.arraySize = packages.Length;

            for (var i = 0; i < packages.Length; i++)
            {
                var element = packagesProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_packageName").stringValue = packages[i].packageName;
                element.FindPropertyRelative("_location").stringValue = packages[i].location;
            }

            serializedObject.ApplyModifiedProperties();
            return packageList;
        }
    }
}
