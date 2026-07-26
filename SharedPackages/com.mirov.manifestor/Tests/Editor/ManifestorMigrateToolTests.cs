using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mirov.Manifestor.Editor.Tests
{
    public sealed class ManifestorMigrateToolTests
    {
        [Test]
        public void BuildRows_GivesMissingPackageAllPackageListsUnchecked()
        {
            var packageLists = new[]
            {
                new ManifestorMigrateTool.PackageListTarget(CreatePackageList("CommonPackages"), "Assets/CommonPackages.asset"),
                new ManifestorMigrateTool.PackageListTarget(CreatePackageList("UnityPackages"), "Assets/UnityPackages.asset")
            };
            var diff = ManifestPackageDiffEntry.MissingInPackageLists("com.example.missing", "1.0.0");

            var rows = ManifestorMigrateTool.BuildRows(new[] { diff }, packageLists);

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Targets.Select(target => target.PackageList.name), Is.EqualTo(new[] { "CommonPackages", "UnityPackages" }));
            Assert.That(rows[0].Targets.Select(target => target.Selected), Is.EqualTo(new[] { false, false }));
        }

        [Test]
        public void BuildRows_GivesChangedPackageOnlyExistingPackageListsChecked()
        {
            var common = CreatePackageList("CommonPackages", ("com.example.changed", "1.0.0"));
            var unity = CreatePackageList("UnityPackages");
            var packageLists = new[]
            {
                new ManifestorMigrateTool.PackageListTarget(common, "Assets/CommonPackages.asset"),
                new ManifestorMigrateTool.PackageListTarget(unity, "Assets/UnityPackages.asset")
            };
            var diff = ManifestPackageDiffEntry.Changed(
                "com.example.changed",
                "2.0.0",
                "1.0.0",
                ManifestPackageChangeKind.Updated);

            var rows = ManifestorMigrateTool.BuildRows(new[] { diff }, packageLists);

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Targets.Select(target => target.PackageList.name), Is.EqualTo(new[] { "CommonPackages" }));
            Assert.That(rows[0].Targets.Select(target => target.Selected), Is.EqualTo(new[] { true }));
        }

        [Test]
        public void BuildRows_GivesRemovedPackageOnlyExistingPackageListsChecked()
        {
            var common = CreatePackageList("CommonPackages", ("com.example.removed", "1.0.0"));
            var unity = CreatePackageList("UnityPackages", ("com.example.removed", "1.0.0"));
            var empty = CreatePackageList("EmptyPackages");
            var packageLists = new[]
            {
                new ManifestorMigrateTool.PackageListTarget(common, "Assets/CommonPackages.asset"),
                new ManifestorMigrateTool.PackageListTarget(unity, "Assets/UnityPackages.asset"),
                new ManifestorMigrateTool.PackageListTarget(empty, "Assets/EmptyPackages.asset")
            };
            var diff = ManifestPackageDiffEntry.RemovedFromManifest("com.example.removed", "1.0.0");

            var rows = ManifestorMigrateTool.BuildRows(new[] { diff }, packageLists);

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Targets.Select(target => target.PackageList.name), Is.EqualTo(new[] { "CommonPackages", "UnityPackages" }));
            Assert.That(rows[0].Targets.Select(target => target.Selected), Is.EqualTo(new[] { true, true }));
        }

        private static PackagesListSO CreatePackageList(string name, params (string packageName, string location)[] packages)
        {
            var packageList = ScriptableObject.CreateInstance<PackagesListSO>();
            packageList.name = name;
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
