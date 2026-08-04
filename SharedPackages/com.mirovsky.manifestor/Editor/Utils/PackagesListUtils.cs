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
        internal static bool TryFindAppliedProfilePackageLists(out PackageListTarget[] packageLists)
        {
            if (!TryGetAppliedProfile(out var profile))
            {
                packageLists = Array.Empty<PackageListTarget>();
                return false;
            }

            packageLists = profile.packagesLists
                .Where(packageList => packageList != null)
                .Select(packageList => new PackageListTarget(packageList, AssetDatabase.GetAssetPath(packageList)))
                .Where(target => !string.IsNullOrEmpty(target.assetPath))
                .GroupBy(target => target.assetPath, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(target => target.assetPath, StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        internal static void CreateNewPackageListForAppliedProfile()
        {
            if (!TryGetAppliedProfile(out var profile))
            {
                return;
            }

            var serializedProfile = new SerializedObject(profile);
            var packageListsProperty = serializedProfile.FindProperty("_packageLists");
            if (packageListsProperty == null || !packageListsProperty.isArray)
            {
                Debug.LogWarning($"Manifestor could not attach a package list to profile '{profile.name}'.");
                return;
            }

            var assetPath = EditorUtility.SaveFilePanelInProject(
                "Create Packages List",
                "PackagesList",
                "asset",
                "Select where to create the packages list.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create and Attach Packages List");

            var packageList = ScriptableObject.CreateInstance<ManifestorPackagesListSO>();
            AssetDatabase.CreateAsset(packageList, assetPath);
            Undo.RegisterCreatedObjectUndo(packageList, "Create Packages List");
            Undo.RecordObject(profile, "Attach Packages List to Manifest Profile");

            packageListsProperty.arraySize++;
            packageListsProperty.GetArrayElementAtIndex(packageListsProperty.arraySize - 1).objectReferenceValue = packageList;
            serializedProfile.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
        }

        public static void ApplyPackageListChanges(List<ManifestorMigrateTool.MigrationRow> rows)
        {
            var selectedChanges = rows
                .Where(row => row.targets != null)
                .SelectMany(row => row.targets
                    .Where(target => target != null && target.selected && target.packageList != null)
                    .Select(target => new SelectedChange(row.change, target.packageList)))
                .ToLookup(selectedChange => selectedChange.packageList);
            if (!TryFindAppliedProfilePackageLists(out var packageLists))
            {
                return;
            }
            var manifest = ManifestorIO.LoadExistingManifest();
            var scopedRegistries = manifest?.scopedRegistries ?? Array.Empty<ScopedManifestRegistry>();

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

            ManifestorIO.RefreshDependenciesFingerprint(manifest);
        }

        private static bool ApplyChange(ManifestorPackagesListSO packageList, ManifestPackageDiffEntry change)
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

        private static bool TryGetAppliedProfile(out ManifestProfileSO profile)
        {
            profile = null;
            if (!ManifestorSettings.instance.TryGetLastAppliedProfilePath(out var profilePath))
            {
                Debug.LogWarning("Manifestor migration requires a successfully applied manifest profile.");
                return false;
            }

            profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(profilePath);
            if (profile != null)
            {
                return true;
            }

            Debug.LogWarning($"Manifestor migration could not load the applied profile at '{profilePath}'.");
            return false;
        }

        private static bool UsesScopedRegistry(ManifestorPackagesListSO packageList, ScopedManifestRegistry scopedRegistry)
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
            public readonly ManifestorPackagesListSO packageList;

            public SelectedChange(ManifestPackageDiffEntry change, ManifestorPackagesListSO packageList)
            {
                this.change = change;
                this.packageList = packageList;
            }
        }
    }

}
