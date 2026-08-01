namespace Manifestor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using SerializedData;
    using UI;
    using UnityEditor;
    using UnityEngine;

    public static class PackagesListUtils
    {
        public static PackageListTarget[] FindPackageLists()
        {
            return AssetDatabase.FindAssets("t:PackagesListSO")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new PackageListTarget(AssetDatabase.LoadAssetAtPath<PackagesListSO>(path), path))
                .Where(target => target.packageList != null)
                .ToArray();
        }

        public static void CreateNewPackageList()
        {
            var assetPath = EditorUtility.SaveFilePanelInProject(
                "Create Packages List",
                "PackagesList",
                "asset",
                "Select where to create the packages list.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            var packageList = ScriptableObject.CreateInstance<PackagesListSO>();
            AssetDatabase.CreateAsset(packageList, assetPath);
            Undo.RegisterCreatedObjectUndo(packageList, "Create Packages List");
            AssetDatabase.SaveAssets();
        }

        public static void ApplyPackageListChanges(List<ManifestorMigrateTool.MigrationRow> rows)
        {
            var selectedChanges = rows
                .Where(row => row.targets != null)
                .SelectMany(row => row.targets
                    .Where(target => target != null && target.selected && target.packageList != null)
                    .Select(target => new SelectedChange(row.change, target.packageList)))
                .ToLookup(selectedChange => selectedChange.packageList);
            var packageLists = FindPackageLists();
            var scopedRegistries = ManifestorIO.LoadExistingManifest()?.scopedRegistries
                                   ?? Array.Empty<ScopedManifestRegistry>();

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Package Manifest Migration");
            var hasChanges = false;

            foreach (var packageListTarget in packageLists)
            {
                var packageList = packageListTarget.packageList;
                Undo.RecordObject(packageList, "Apply Package Manifest Migration");

                var packageListChanged = selectedChanges[packageList]
                    .Aggregate(false, (current, selectedChange) => current | ApplyChange(packageList, selectedChange.change));
                packageListChanged |= scopedRegistries
                    .Where(scopedRegistry => UsesScopedRegistry(packageList, scopedRegistry))
                    .Aggregate(false, (current, scopedRegistry) => current | packageList.AddScopedRegistry(scopedRegistry.name, scopedRegistry.url, scopedRegistry.scopes));

                if (!packageListChanged)
                {
                    continue;
                }

                EditorUtility.SetDirty(packageList);
                hasChanges = true;
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (hasChanges)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static bool ApplyChange(PackagesListSO packageList, ManifestPackageDiffEntry change)
        {
            return change.changeKind switch
            {
                ManifestPackageChangeKind.MissingInPackageLists => packageList.AddPackage(change.packageTechnicalName,
                    change.manifestValue),
                ManifestPackageChangeKind.RemovedFromManifest => packageList.RemovePackage(change.packageTechnicalName,
                    change.packageListValue),
                ManifestPackageChangeKind.Changed => packageList.UpdatePackage(change.packageTechnicalName,
                    change.packageListValue, change.manifestValue),
                _ => false
            };
        }

        private static bool UsesScopedRegistry(PackagesListSO packageList, ScopedManifestRegistry scopedRegistry)
        {
            if (packageList.packages == null || scopedRegistry.scopes == null)
            {
                return false;
            }

            return packageList.packages
                .Where(package => package != null)
                .Select(package => StringUtils.Normalize(package.packageName))
                .Any(packageName => scopedRegistry.scopes
                    .Select(StringUtils.Normalize)
                    .Where(scope => !string.IsNullOrEmpty(scope))
                    .Any(scope => packageName.StartsWith(scope, StringComparison.Ordinal)));
        }

        private readonly struct SelectedChange
        {
            public readonly ManifestPackageDiffEntry change;
            public readonly PackagesListSO packageList;

            public SelectedChange(ManifestPackageDiffEntry change, PackagesListSO packageList)
            {
                this.change = change;
                this.packageList = packageList;
            }
        }
    }

}
