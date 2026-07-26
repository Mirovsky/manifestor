namespace Mirov.Manifestor.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public class ManifestorMigrateTool : EditorWindow
    {
        [SerializeField] private VisualTreeAsset _migrationToolAsset;

        private readonly MigrationRows _migrationRows = new();

        [MenuItem("Tools/Manifestor/Migrate Package Manifest")]
        public static void ShowWindow()
        {
            var window = GetWindow<ManifestorMigrateTool>(utility: true, "Package Manifest Migration");
            window.Refresh();
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            _migrationToolAsset.CloneTree(rootVisualElement);

            var listView = rootVisualElement.Q<ListView>("ContentListView");
            listView.itemsSource = _migrationRows.rows;

            var newPackageListButton = rootVisualElement.Q<Button>("NewPackageListButton");
            var applyButton = rootVisualElement.Q<Button>("ApplyButton");
            var refreshButton = rootVisualElement.Q<Button>("RefreshButton");
            applyButton.clicked += HandleApplyButtonClicked;
            refreshButton.clicked += HandleRefreshButtonClicked;
            newPackageListButton.clicked += HandleNewPackageListButtonClicked;
        }

        private void HandleNewPackageListButtonClicked()
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

            var packageList = CreateInstance<PackagesListSO>();
            AssetDatabase.CreateAsset(packageList, assetPath);
            Undo.RegisterCreatedObjectUndo(packageList, "Create Packages List");
            AssetDatabase.SaveAssets();
            Refresh();
        }

        private void HandleApplyButtonClicked()
        {
            var selectedChanges = _migrationRows.rows
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

            GetWindow<ManifestorMigrateTool>().Close();
        }

        private void HandleRefreshButtonClicked()
        {
            Refresh();
        }

        private void Refresh()
        {
            var selectedStates = _migrationRows.rows
                .Where(row => row.targets != null)
                .SelectMany(row => row.targets
                    .Where(target => target != null)
                    .Select(target => new
                    {
                        key = CreateSelectionKey(row.change, target.assetPath),
                        target.selected
                    }))
                .ToDictionary(selection => selection.key, selection => selection.selected);
            var diff = ManifestPackageDiffUtility.CreateManifestDiff();
            var packageLists = FindPackageLists();
            var rows = BuildRows(diff.allChanges, packageLists);

            foreach (var row in rows)
            {
                foreach (var target in row.targets)
                {
                    if (selectedStates.TryGetValue(CreateSelectionKey(row.change, target.assetPath), out var selected))
                    {
                        target.selected = selected;
                    }
                }
            }

            _migrationRows.rows.Clear();
            _migrationRows.rows.AddRange(rows);

            rootVisualElement.Q<ListView>("ContentListView")?.RefreshItems();
            Repaint();
        }

        private static (string packageTechnicalName, string manifestValue, string packageListValue, ManifestPackageChangeKind changeKind, string assetPath)
            CreateSelectionKey(ManifestPackageDiffEntry change, string assetPath)
        {
            return (
                change.packageTechnicalName,
                change.manifestValue,
                change.packageListValue,
                change.changeKind,
                assetPath ?? string.Empty);
        }

        private static PackageListTarget[] FindPackageLists()
        {
            return AssetDatabase.FindAssets("t:PackagesListSO")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new PackageListTarget(AssetDatabase.LoadAssetAtPath<PackagesListSO>(path), path))
                .Where(target => target.packageList != null)
                .ToArray();
        }

        private static IReadOnlyList<MigrationRow> BuildRows(IEnumerable<ManifestPackageDiffEntry> changes, IReadOnlyList<PackageListTarget> packageLists)
        {
            var result = new List<MigrationRow>();
            if (changes == null)
            {
                return result;
            }

            result.AddRange(changes
                .Select(change => new MigrationRow(
                    change,
                    packageLists
                        .Where(l => l.packageList != null)
                        .Where(l => IsTargetPackageList(change, l.packageList))
                        .Select(l => new PackageListSelection(l.packageList, l.assetPath, change.changeKind != ManifestPackageChangeKind.MissingInPackageLists))
                        .ToList()
                    ))
            );

            return result;
        }

        private static bool IsTargetPackageList(ManifestPackageDiffEntry change, PackagesListSO packageList)
        {
            if (packageList.packages == null)
            {
                return change.changeKind == ManifestPackageChangeKind.MissingInPackageLists;
            }

            if (change.changeKind == ManifestPackageChangeKind.MissingInPackageLists)
            {
                return packageList.packages.All(package =>
                    package == null ||
                    !string.Equals(Normalize(package.packageName), change.packageTechnicalName, StringComparison.Ordinal));
            }

            return packageList.packages.Any(package =>
                package != null &&
                string.Equals(Normalize(package.packageName), change.packageTechnicalName, StringComparison.Ordinal) &&
                string.Equals(Normalize(package.location), change.packageListValue, StringComparison.Ordinal));
        }

        private static bool ApplyChange(PackagesListSO packageList, ManifestPackageDiffEntry change)
        {
            switch (change.changeKind)
            {
                case ManifestPackageChangeKind.MissingInPackageLists:
                    return packageList.AddPackage(change.packageTechnicalName, change.manifestValue);
                case ManifestPackageChangeKind.RemovedFromManifest:
                    return packageList.RemovePackage(change.packageTechnicalName, change.packageListValue);
                case ManifestPackageChangeKind.Changed:
                    return packageList.UpdatePackage(change.packageTechnicalName, change.packageListValue, change.manifestValue);
                default:
                    return false;
            }
        }

        private static bool UsesScopedRegistry(PackagesListSO packageList, ScopedManifestRegistry scopedRegistry)
        {
            if (packageList.packages == null || scopedRegistry.scopes == null)
            {
                return false;
            }

            return packageList.packages
                .Where(package => package != null)
                .Select(package => Normalize(package.packageName))
                .Any(packageName => scopedRegistry.scopes
                    .Select(Normalize)
                    .Where(scope => !string.IsNullOrEmpty(scope))
                    .Any(scope => packageName.StartsWith(scope, StringComparison.Ordinal)));
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
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

        [Serializable]
        public class MigrationRows
        {
            public List<MigrationRow> rows = new();
        }

        [Serializable]
        public struct MigrationRow
        {
            public ManifestPackageDiffEntry change;
            public List<PackageListSelection> targets;

            public MigrationRow(ManifestPackageDiffEntry change, List<PackageListSelection> targets)
            {
                this.change = change;
                this.targets = targets ?? new List<PackageListSelection>();
            }
        }

        [Serializable]
        public struct PackageListTarget
        {
            public PackagesListSO packageList;
            public string assetPath;

            public PackageListTarget(PackagesListSO packageList, string assetPath)
            {
                this.packageList = packageList;
                this.assetPath = assetPath ?? string.Empty;
            }
        }

        [Serializable]
        public class PackageListSelection
        {
            public string packageListName;
            public PackagesListSO packageList;
            public string assetPath;
            public bool selected;

            public PackageListSelection(PackagesListSO packageList, string assetPath, bool selected)
            {
                packageListName = packageList.name;

                this.packageList = packageList;
                this.assetPath = assetPath ?? string.Empty;
                this.selected = selected;
            }
        }
    }
}
