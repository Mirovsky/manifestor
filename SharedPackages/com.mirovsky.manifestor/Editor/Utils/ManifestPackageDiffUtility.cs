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
            var manifest = ManifestorIO.LoadExistingManifest();
            if (!PackagesListUtils.TryFindAppliedProfilePackageLists(out var packageListTargets))
            {
                return new ManifestPackageDiffResult(Array.Empty<ManifestPackageDiffEntry>());
            }

            var packageLists = packageListTargets.Select(p => p.packageList);

            return Compare(manifest?.dependencies, packageLists);
        }

        internal static ManifestPackageDiffResult Compare(
            IReadOnlyDictionary<string, string> manifestDependencies,
            IEnumerable<ManifestorPackagesListSO> packageLists)
        {
            var manifest = NormalizeManifestDependencies(manifestDependencies);
            var packageListDependencies = NormalizePackageListDependencies(packageLists);

            var missing = manifest
                .Where(d => !packageListDependencies.ContainsKey(d.Key))
                .Select(d => ManifestPackageDiffEntry.MissingInPackageLists(d.Key, d.Value));
            var changed = manifest
                .Where(d => packageListDependencies.ContainsKey(d.Key))
                .SelectMany(d => packageListDependencies[d.Key]
                    .Where(packageListValue => packageListValue != d.Value)
                    .Select(packageListValue => ManifestPackageDiffEntry.Changed(d.Key, d.Value, packageListValue, ManifestPackageChangeKind.Changed)));
            var removed = packageListDependencies
                .Where(d => !manifest.ContainsKey(d.Key))
                .SelectMany(d => d.Value.Select(packageListValue => ManifestPackageDiffEntry.RemovedFromManifest(d.Key, packageListValue)));

            return new ManifestPackageDiffResult(missing.Concat(removed).Concat(changed));
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

        private static Dictionary<string, HashSet<string>> NormalizePackageListDependencies(IEnumerable<ManifestorPackagesListSO> packageLists)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var packageList in packageLists ?? Array.Empty<ManifestorPackagesListSO>())
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
                        packageLocations = new HashSet<string>(StringComparer.Ordinal);
                        result[packageName] = packageLocations;
                    }

                    packageLocations.Add(packageLocation);
                }
            }

            return result;
        }
    }

    public sealed class ManifestPackageDiffResult
    {
        public readonly IReadOnlyList<ManifestPackageDiffEntry> allChanges;
        public bool hasChanges => allChanges.Count > 0;

        internal ManifestPackageDiffResult(IEnumerable<ManifestPackageDiffEntry> changes)
        {
            allChanges = changes
                .OrderBy(change => change.packageTechnicalName, StringComparer.Ordinal)
                .ThenBy(change => change.changeKind)
                .ThenBy(change => change.packageListValue, StringComparer.Ordinal)
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
