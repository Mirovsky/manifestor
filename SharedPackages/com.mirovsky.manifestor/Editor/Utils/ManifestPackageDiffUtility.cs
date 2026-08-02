namespace Manifestor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;

    public static class ManifestPackageDiffUtility
    {
        public static ManifestPackageDiffResult CreateManifestDiff()
        {
            var packageLists = AssetDatabase.FindAssets("t:PackagesListSO")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<PackagesListSO>)
                .Where(packageList => packageList != null)
                .ToArray();

            return Compare(ManifestorIO.LoadExistingManifest()?.dependencies, packageLists);
        }

        internal static ManifestPackageDiffResult Compare(
            IReadOnlyDictionary<string, string> manifestDependencies,
            IEnumerable<PackagesListSO> packageLists)
        {
            var manifest = NormalizeManifestDependencies(manifestDependencies);
            var packageListDependencies = NormalizePackageListDependencies(packageLists);

            var missing = manifest
                .Where(d => !packageListDependencies.ContainsKey(d.Key))
                .Select(d => ManifestPackageDiffEntry.MissingInPackageLists(d.Key, d.Value))
                .ToList();
            var changed = manifest
                .Where(d => packageListDependencies.ContainsKey(d.Key))
                .SelectMany(d => packageListDependencies[d.Key]
                    .Where(packageListValue => packageListValue != d.Value)
                    .Select(packageListValue => ManifestPackageDiffEntry.Changed(d.Key, d.Value, packageListValue, ManifestPackageChangeKind.Changed)))
                .ToList();
            var removed = packageListDependencies
                .Where(d => !manifest.ContainsKey(d.Key))
                .SelectMany(d => d.Value.Select(packageListValue => ManifestPackageDiffEntry.RemovedFromManifest(d.Key, packageListValue)))
                .ToList();

            return new ManifestPackageDiffResult(
                SortByPackageName(missing),
                SortByPackageName(removed),
                SortByPackageName(changed)
            );
        }

        private static Dictionary<string, string> NormalizeManifestDependencies(IReadOnlyDictionary<string, string> manifestDependencies)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (manifestDependencies == null)
            {
                return result;
            }

            foreach (var dependency in manifestDependencies)
            {
                var packageName = StringUtils.Normalize(dependency.Key);
                if (string.IsNullOrEmpty(packageName))
                {
                    continue;
                }

                result[packageName] = StringUtils.Normalize(dependency.Value);
            }

            return result;
        }

        private static Dictionary<string, IReadOnlyList<string>> NormalizePackageListDependencies(IEnumerable<PackagesListSO> packageLists)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var packageList in packageLists ?? Array.Empty<PackagesListSO>())
            {
                if (packageList?.packages == null)
                {
                    continue;
                }

                foreach (var package in packageList.packages)
                {
                    if (package == null)
                    {
                        continue;
                    }

                    var packageName = StringUtils.Normalize(package.packageName);
                    if (string.IsNullOrEmpty(packageName))
                    {
                        continue;
                    }

                    var packageLocation = StringUtils.Normalize(package.location);
                    if (!result.TryGetValue(packageName, out var packageLocations))
                    {
                        packageLocations = new List<string>();
                        result[packageName] = packageLocations;
                    }

                    if (!packageLocations.Contains(packageLocation))
                    {
                        packageLocations.Add(packageLocation);
                    }
                }
            }

            return result.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal);
        }

        private static IReadOnlyList<ManifestPackageDiffEntry> SortByPackageName(List<ManifestPackageDiffEntry> changes)
        {
            changes.Sort((left, right) =>
            {
                var nameComparison = string.Compare(left.packageTechnicalName, right.packageTechnicalName, StringComparison.Ordinal);
                return nameComparison == 0 ?
                    string.Compare(left.packageListValue, right.packageListValue, StringComparison.Ordinal) :
                    nameComparison;
            });
            return changes;
        }
    }

    public sealed class ManifestPackageDiffResult
    {
        public readonly IReadOnlyList<ManifestPackageDiffEntry> allChanges;
        public bool hasChanges => allChanges.Count > 0;

        internal ManifestPackageDiffResult(
            IReadOnlyList<ManifestPackageDiffEntry> missingInPackageLists,
            IReadOnlyList<ManifestPackageDiffEntry> removedFromManifest,
            IReadOnlyList<ManifestPackageDiffEntry> changed)
        {
            allChanges = missingInPackageLists
                .Concat(removedFromManifest)
                .Concat(changed)
                .OrderBy(change => change.packageTechnicalName, StringComparer.Ordinal)
                .ToArray();
        }
    }

    [Serializable]
    public struct ManifestPackageDiffEntry
    {
        public string packageTechnicalName;
        public string manifestValue;
        public string packageListValue;
        public ManifestPackageChangeKind changeKind;

        private ManifestPackageDiffEntry(
            string packageTechnicalName,
            string manifestValue,
            string packageListValue,
            ManifestPackageChangeKind changeKind)
        {
            this.packageTechnicalName = packageTechnicalName ?? string.Empty;
            this.manifestValue = manifestValue ?? string.Empty;
            this.packageListValue = packageListValue ?? string.Empty;
            this.changeKind = changeKind;
        }

        public static ManifestPackageDiffEntry MissingInPackageLists(string packageTechnicalName, string manifestValue)
        {
            return new ManifestPackageDiffEntry(
                packageTechnicalName,
                manifestValue,
                string.Empty,
                ManifestPackageChangeKind.MissingInPackageLists);
        }

        public static ManifestPackageDiffEntry RemovedFromManifest(string packageTechnicalName, string packageListValue)
        {
            return new ManifestPackageDiffEntry(

                packageTechnicalName,
                string.Empty,
                packageListValue,
                ManifestPackageChangeKind.RemovedFromManifest);
        }

        public static ManifestPackageDiffEntry Changed(
            string packageTechnicalName,
            string manifestValue,
            string packageListValue,
            ManifestPackageChangeKind changeKind)
        {
            return new ManifestPackageDiffEntry(packageTechnicalName, manifestValue, packageListValue, changeKind);
        }
    }

    public enum ManifestPackageChangeKind
    {
        MissingInPackageLists,
        RemovedFromManifest,
        Changed
    }
}
