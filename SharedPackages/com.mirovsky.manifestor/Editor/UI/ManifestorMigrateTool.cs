namespace Manifestor.UI
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

        private readonly ViewDataModel _viewDataModel = new();

        private void CreateGUI()
        {
            _migrationToolAsset.CloneTree(rootVisualElement);

            var listView = rootVisualElement.Q<ListView>("ContentListView");
            listView.itemsSource = _viewDataModel.rows;
            listView.makeNoneElement = () => null;

            var newPackageListButton = rootVisualElement.Q<Button>("NewPackageListButton");
            var applyButton = rootVisualElement.Q<Button>("ApplyButton");
            var refreshButton = rootVisualElement.Q<Button>("RefreshButton");

            applyButton.clicked += HandleApplyButtonClicked;
            refreshButton.clicked += HandleRefreshButtonClicked;
            newPackageListButton.clicked += HandleNewPackageListButtonClicked;
        }

        [MenuItem("Tools/Manifestor/Manifest Migration")]
        public static void ShowWindow()
        {
            var window = GetWindow<ManifestorMigrateTool>(utility: true, "Manifest Migration");
            window.Refresh();
            window.ShowUtility();
        }

        private void HandleNewPackageListButtonClicked()
        {
            PackagesListUtils.CreateNewPackageList();

            Refresh();
        }

        private void HandleApplyButtonClicked()
        {
            PackagesListUtils.ApplyPackageListChanges(_viewDataModel.rows);

            GetWindow<ManifestorMigrateTool>().Close();
        }

        private void HandleRefreshButtonClicked()
        {
            Refresh();
        }

        private void Refresh()
        {
            var selectedStates = _viewDataModel.rows
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
            var packageLists = PackagesListUtils.FindPackageLists();
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

            _viewDataModel.rows.Clear();
            _viewDataModel.rows.AddRange(rows);

            var emptyLabel = rootVisualElement.Q<Label>("EmptyContentList");
            emptyLabel.style.display = _viewDataModel.rows == null || _viewDataModel.rows.Count == 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            rootVisualElement.Q<ListView>("ContentListView")?.RefreshItems();
            Repaint();
        }

        private static (string packageTechnicalName, string manifestValue, string packageListValue, ManifestPackageChangeKind changeKind, string assetPath) CreateSelectionKey(ManifestPackageDiffEntry change, string assetPath)
        {
            return (
                change.packageTechnicalName,
                change.manifestValue,
                change.packageListValue,
                change.changeKind,
                assetPath ?? string.Empty);
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
                    !string.Equals(StringUtils.Normalize(package.packageName), change.packageTechnicalName, StringComparison.Ordinal));
            }

            return packageList.packages.Any(package =>
                package != null &&
                string.Equals(StringUtils.Normalize(package.packageName), change.packageTechnicalName, StringComparison.Ordinal) &&
                string.Equals(StringUtils.Normalize(package.location), change.packageListValue, StringComparison.Ordinal));
        }

        [Serializable]
        private class ViewDataModel
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
