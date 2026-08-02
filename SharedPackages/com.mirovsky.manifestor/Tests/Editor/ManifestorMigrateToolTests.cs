namespace Manifestor.Editor.Tests
{
    using NUnit.Framework;
    using UnityEngine;
    using UI;

    public sealed class ManifestorMigrateToolTests
    {
        [Test]
        public void BuildRows_GivesMissingPackageAllListsUnchecked()
        {
            var packageLists = new[]
            {
                new PackageListTarget(CreatePackageList("Common"), "Assets/Common.asset"),
                new PackageListTarget(CreatePackageList("Unity"), "Assets/Unity.asset")
            };

            var rows = ManifestorMigrateTool.BuildRows(
                new[] { ManifestPackageDiffEntry.MissingInPackageLists("com.example.missing", "1.0.0") },
                packageLists);

            Assert.That(rows[0].targets, Has.Count.EqualTo(2));
            Assert.That(rows[0].targets[0].packageList.name, Is.EqualTo("Common"));
            Assert.That(rows[0].targets[1].packageList.name, Is.EqualTo("Unity"));
            Assert.That(rows[0].targets[0].selected, Is.False);
            Assert.That(rows[0].targets[1].selected, Is.False);
        }

        [Test]
        public void BuildRows_GivesChangedPackageOnlyMatchingListChecked()
        {
            var common = CreatePackageList("Common", ("com.example.changed", "1.0.0"));
            var rows = ManifestorMigrateTool.BuildRows(
                new[]
                {
                    ManifestPackageDiffEntry.Changed(
                        "com.example.changed", "2.0.0", "1.0.0", ManifestPackageChangeKind.Changed)
                },
                new[]
                {
                    new PackageListTarget(common, "Assets/Common.asset"),
                    new PackageListTarget(CreatePackageList("Empty"), "Assets/Empty.asset")
                });

            Assert.That(rows[0].targets, Has.Count.EqualTo(1));
            Assert.That(rows[0].targets[0].packageList, Is.SameAs(common));
            Assert.That(rows[0].targets[0].selected, Is.True);
        }

        private static PackagesListSO CreatePackageList(
            string name,
            params (string packageName, string location)[] packages)
        {
            var packageList = ScriptableObject.CreateInstance<PackagesListSO>();
            packageList.name = name;
            foreach (var package in packages)
            {
                packageList.AddPackage(package.packageName, package.location);
            }

            return packageList;
        }
    }
}
