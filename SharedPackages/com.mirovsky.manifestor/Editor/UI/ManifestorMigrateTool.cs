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
        private const string TargetToggleClass = "manifestor-migration-target-toggle";
        private const string BulkToggleClass = "manifestor-migration-bulk-selection__toggle";

        [SerializeField] private VisualTreeAsset _migrationToolAsset;

        private readonly ViewDataModel _viewDataModel = new();
        private readonly Dictionary<string, Toggle> _bulkToggles = new(StringComparer.Ordinal);

        private ListView _contentListView;
        private VisualElement _bulkSelectionHeader;
        private VisualElement _bulkSelectionToggles;

        private void CreateGUI()
        {
            _migrationToolAsset.CloneTree(rootVisualElement);

            _contentListView = rootVisualElement.Q<ListView>("ContentListView");
            _contentListView.itemsSource = _viewDataModel.rows;
            _contentListView.makeNoneElement = () => null;

            _bulkSelectionHeader = rootVisualElement.Q<VisualElement>("BulkSelectionHeader");
            _bulkSelectionToggles = rootVisualElement.Q<VisualElement>("BulkSelectionToggles");
            rootVisualElement.RegisterCallback<ChangeEvent<bool>>(HandleTargetSelectionChanged);

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

        private void HandleTargetSelectionChanged(ChangeEvent<bool> evt)
        {
            if (evt.target is not Toggle toggle || !toggle.ClassListContains(TargetToggleClass))
            {
                return;
            }

            rootVisualElement.schedule.Execute(UpdateBulkSelectionStates);
        }

        private void HandleBulkSelectionChanged(string assetPath, bool selected)
        {
            foreach (var target in _viewDataModel.rows
                         .Where(row => row.targets != null)
                         .SelectMany(row => row.targets)
                         .Where(target => target != null &&
                                          string.Equals(target.assetPath, assetPath, StringComparison.Ordinal)))
            {
                target.selected = selected;
            }

            _contentListView.RefreshItems();
            UpdateBulkSelectionStates();
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

            var hasChanges = _viewDataModel.rows.Count > 0;
            var changesHelpBox = rootVisualElement.Q<HelpBox>("ChangesHelpBox");
            changesHelpBox.style.display = hasChanges
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            RebuildBulkSelectionHeader();

            var emptyLabel = rootVisualElement.Q<Label>("EmptyContentList");
            emptyLabel.style.display = hasChanges
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            _contentListView?.RefreshItems();
            Repaint();
        }

        private void RebuildBulkSelectionHeader()
        {
            _bulkSelectionToggles.Clear();
            _bulkToggles.Clear();

            var packageLists = _viewDataModel.rows
                .Where(row => row.targets != null)
                .SelectMany(row => row.targets)
                .Where(target => target != null)
                .GroupBy(target => target.assetPath, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(target => target.packageListName, StringComparer.Ordinal)
                .ThenBy(target => target.assetPath, StringComparer.Ordinal);

            foreach (var packageList in packageLists)
            {
                var assetPath = packageList.assetPath;
                var toggle = new Toggle(packageList.packageListName)
                {
                    tooltip = assetPath
                };
                toggle.AddToClassList(BulkToggleClass);
                toggle.RegisterValueChangedCallback(evt =>
                    HandleBulkSelectionChanged(assetPath, evt.newValue));

                _bulkToggles.Add(assetPath, toggle);
                _bulkSelectionToggles.Add(toggle);
            }

            _bulkSelectionHeader.style.display = _bulkToggles.Count > 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            UpdateBulkSelectionStates();
        }

        private void UpdateBulkSelectionStates()
        {
            foreach (var (assetPath, toggle) in _bulkToggles)
            {
                var selections = _viewDataModel.rows
                    .Where(row => row.targets != null)
                    .SelectMany(row => row.targets)
                    .Where(target => target != null &&
                                     string.Equals(target.assetPath, assetPath, StringComparison.Ordinal))
                    .Select(target => target.selected)
                    .ToList();
                var selectedCount = selections.Count(selected => selected);
                var allSelected = selections.Count > 0 && selectedCount == selections.Count;
                var partiallySelected = selectedCount > 0 && !allSelected;

                toggle.SetValueWithoutNotify(allSelected);
                toggle.showMixedValue = partiallySelected;
            }
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

        internal static IReadOnlyList<MigrationRow> BuildRows(IEnumerable<ManifestPackageDiffEntry> changes, IReadOnlyList<PackageListTarget> packageLists)
        {
            var result = new List<MigrationRow>();
            if (changes == null)
            {
                return result;
            }

            ManifestPackageChangeKind? previousKind = null;
            foreach (var change in changes
                         .OrderBy(change => GetChangeKindOrder(change.changeKind))
                         .ThenBy(change => change.packageTechnicalName, StringComparer.Ordinal)
                         .ThenBy(change => change.packageListValue, StringComparer.Ordinal))
            {
                var targets = packageLists
                    .Where(packageList => packageList.packageList != null)
                    .Where(packageList => IsTargetPackageList(change, packageList.packageList))
                    .Select(packageList => new PackageListSelection(
                        packageList.packageList,
                        packageList.assetPath,
                        change.changeKind != ManifestPackageChangeKind.MissingInPackageLists))
                    .ToList();
                result.Add(new MigrationRow(change, targets, change.changeKind != previousKind));
                previousKind = change.changeKind;
            }

            return result;
        }

        private static int GetChangeKindOrder(ManifestPackageChangeKind changeKind)
        {
            return changeKind switch
            {
                ManifestPackageChangeKind.MissingInPackageLists => 0,
                ManifestPackageChangeKind.Changed => 1,
                ManifestPackageChangeKind.RemovedFromManifest => 2,
                _ => int.MaxValue
            };
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

            public string previousValue;
            public string nextValue;

            public string groupTitle;
            public bool showsGroupHeader;

            public bool isAddition;
            public bool isVersionChange;
            public bool isRemoval;

            public MigrationRow(
                ManifestPackageDiffEntry change,
                List<PackageListSelection> targets,
                bool showsGroupHeader = false)
            {
                this.change = change;
                this.targets = targets ?? new List<PackageListSelection>();
                this.showsGroupHeader = showsGroupHeader;

                isAddition = change.changeKind == ManifestPackageChangeKind.MissingInPackageLists;
                isVersionChange = change.changeKind == ManifestPackageChangeKind.Changed;
                isRemoval = change.changeKind == ManifestPackageChangeKind.RemovedFromManifest;

                previousValue = isAddition ? "\u2014" : change.packageListValue;
                nextValue = isRemoval ? "\u2014" : change.manifestValue;
                groupTitle = change.changeKind switch
                {
                    ManifestPackageChangeKind.MissingInPackageLists => "Additions",
                    ManifestPackageChangeKind.Changed => "Version Changes",
                    ManifestPackageChangeKind.RemovedFromManifest => "Removals",
                    _ => "Other Changes"
                };
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
