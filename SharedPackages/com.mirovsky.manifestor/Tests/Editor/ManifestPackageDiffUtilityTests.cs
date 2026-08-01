namespace Mirov.Manifestor.Editor.Tests
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class ManifestPackageDiffUtilityTests
    {
        [Test]
        public void Compare_ClassifiesMissingRemovedAndChangedPackages()
        {
            var packageList = CreatePackageList(
                ("com.example.changed", "1.0.0"),
                ("com.example.removed", "1.0.0"));
            var manifest = new Dictionary<string, string>
            {
                { "com.example.changed", "2.0.0" },
                { "com.example.missing", "1.0.0" }
            };

            var result = ManifestPackageDiffUtility.Compare(manifest, new[] { packageList });

            Assert.That(result.hasChanges, Is.True);
            Assert.That(result.missingInPackageLists[0].packageTechnicalName, Is.EqualTo("com.example.missing"));
            Assert.That(result.removedFromManifest[0].packageTechnicalName, Is.EqualTo("com.example.removed"));
            Assert.That(result.changed[0].packageTechnicalName, Is.EqualTo("com.example.changed"));
            Assert.That(result.changed[0].changeKind, Is.EqualTo(ManifestPackageChangeKind.Changed));
        }

        [Test]
        public void Compare_ReturnsNoChangesForEquivalentNormalizedValues()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.same", "1.0.0" } },
                new[] { CreatePackageList((" com.example.same ", " 1.0.0 ")) });

            Assert.That(result.hasChanges, Is.False);
            Assert.That(result.allChanges, Is.Empty);
        }

        [Test]
        public void Compare_ReturnsOneChangeForEachDistinctPackageListValue()
        {
            var result = ManifestPackageDiffUtility.Compare(
                new Dictionary<string, string> { { "com.example.package", "3.0.0" } },
                new[]
                {
                    CreatePackageList(("com.example.package", "1.0.0")),
                    CreatePackageList(("com.example.package", "2.0.0")),
                    CreatePackageList(("com.example.package", "2.0.0"))
                });

            Assert.That(result.changed, Has.Count.EqualTo(2));
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
    }
}
