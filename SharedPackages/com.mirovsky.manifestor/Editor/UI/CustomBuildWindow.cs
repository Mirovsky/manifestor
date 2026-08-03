using UnityEngine;

namespace Manifestor.UI
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Build;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    public class CustomBuildWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset _customBuildAsset;

        private readonly CustomBuildData _customBuildData = new();

        private ListView _manifestsList;
        private VisualElement _content;
        private BuildStepsList _buildStepsList;

        private Editor _manifestProfileEditor;

        private void CreateGUI()
        {
            _customBuildAsset.CloneTree(rootVisualElement);

            var newManifestButton = rootVisualElement.Q<Button>("NewManifestButton");
            newManifestButton.clicked += HandleNewManifestClicked;

             var refreshButton = rootVisualElement.Q<Button>("RefreshButton");
             refreshButton.clicked += HandleRefreshClicked;

             var packageManagerButton = rootVisualElement.Q<Button>("PackageManagerButton");
             packageManagerButton.clicked += HandlePackageManagerClicked;

            var playerSettingsButton = rootVisualElement.Q<Button>("PlayerSettingsButton");
            playerSettingsButton.clicked += HandlePlayerSettingsClicked;

            var buildProfilesButton = rootVisualElement.Q<Button>("BuildProfilesButton");
            buildProfilesButton.clicked += HandleBuildProfilesClicked;

            var applyManifestButton = rootVisualElement.Q<Button>("ApplyManifestButton");
            applyManifestButton.clicked += HandleApplyManifestButtonClicked;

            var buildButton = rootVisualElement.Q<DropdownButton>("BuildButton");
            buildButton.clicked += HandleDefaultBuildButtonClicked;
            buildButton.choiceSelected += HandleBuildChoiceSelected;

            _manifestsList = rootVisualElement.Q<ListView>("ManifestsListView");
            _manifestsList.selectionChanged += HandleManifestsListSelectionChanged;
            _manifestsList.makeNoneElement = () => null;

            _buildStepsList = rootVisualElement.Q<BuildStepsList>("BuildStepsList");

            _content = rootVisualElement.Q<VisualElement>("Content");
            rootVisualElement.dataSource = _customBuildData;

            ManifestorBuildPipeline.completed -= HandleCustomBuildPipelineCompleted;
            ManifestorBuildPipeline.completed += HandleCustomBuildPipelineCompleted;

            Refresh();
        }

        private void OnDisable()
        {
            ManifestorBuildPipeline.completed -= HandleCustomBuildPipelineCompleted;
            DestroyManifestProfileEditor();
        }

        [MenuItem("Tools/Manifestor/Custom Build")]
        public static void ShowWindow()
        {
            var window = GetWindow<CustomBuildWindow>("Manifestor Build");
            window.Refresh();
            window.Show();
        }

        private void HandleDefaultBuildButtonClicked()
        {
            var profile = _customBuildData.selectedManifestProfile;
            if (profile?.buildProfile == null)
            {
                LogPipelineStartError(ManifestorBuildPipeline.Build(profile, string.Empty));
                return;
            }

            var folderPath = EditorUtility.SaveFolderPanel("Build output folder", "", "");
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            var result = ManifestorBuildPipeline.Build(profile, folderPath);
            LogPipelineStartError(result);
        }

        private void HandleCleanBuildButtonClicked()
        {
            var profile = _customBuildData.selectedManifestProfile;
            if (profile?.buildProfile == null)
            {
                LogPipelineStartError(ManifestorBuildPipeline.Build(profile, string.Empty));
                return;
            }

            var folderPath = EditorUtility.SaveFolderPanel("Build output folder", "", "");
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            var result = ManifestorBuildPipeline.Build(profile, folderPath, BuildOptions.CleanBuildCache);
            LogPipelineStartError(result);
        }

        private void HandleBuildChoiceSelected(string choice)
        {
            if (choice == "Clean Build")
            {
                HandleCleanBuildButtonClicked();
            }
        }

        private void HandleManifestsListSelectionChanged(IEnumerable<object> manifests)
        {
            var selectedManifest = manifests?.FirstOrDefault() as ManifestProfileData;
            var manifestProfile = selectedManifest?.manifestProfile;

            ShowManifestProfileInspector(manifestProfile);

            _customBuildData.selectedManifestProfile = manifestProfile;
        }

        private void Refresh()
        {
            var hasValidOrder = ManifestorBuildPipeline.TryGetOrderedSteps(out var steps, out var error);
            _buildStepsList.SetSteps(hasValidOrder, steps, error);

            var manifests = FindManifests();
            var selectedIndex = manifests.FindIndex(manifest => manifest.isActive);
            if (selectedIndex < 0 && manifests.Count > 0)
            {
                selectedIndex = 0;
            }

            var activeProfile =  selectedIndex >= 0
                ? manifests[selectedIndex].manifestProfile
                : null;

            _customBuildData.activeManifestProfile = activeProfile;
            _customBuildData.selectedManifestProfile = activeProfile;

            _customBuildData.manifests.Clear();
            _customBuildData.manifests.AddRange(manifests);

            _manifestsList.RefreshItems();

            var emptyLabel = rootVisualElement.Q<Label>("EmptyContentList");
            emptyLabel.style.display = _customBuildData.manifests.Count == 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (selectedIndex >= 0)
            {
                _manifestsList.SetSelection(selectedIndex);
                ShowManifestProfileInspector(manifests[selectedIndex].manifestProfile);
            }
            else
            {
                _manifestsList.ClearSelection();
                ShowManifestProfileInspector(null);
            }
        }

        private void ShowManifestProfileInspector(ManifestProfileSO manifestProfile)
        {
            if (_content == null ||
                manifestProfile != null &&
                _manifestProfileEditor != null &&
                _manifestProfileEditor.target == manifestProfile)
            {
                return;
            }

            _content.Clear();
            DestroyManifestProfileEditor();

            if (manifestProfile == null)
            {
                return;
            }

            _manifestProfileEditor = Editor.CreateEditor(manifestProfile);
            _content.Add(
                new InspectorElement(_manifestProfileEditor)
                {
                    style =
                    {
                        flexGrow = 1
                    }
                }
            );
        }

        private void DestroyManifestProfileEditor()
        {
            if (_manifestProfileEditor == null)
            {
                return;
            }

            DestroyImmediate(_manifestProfileEditor);
            _manifestProfileEditor = null;
        }

        private void HandleNewManifestClicked()
        {
            if (!ManifestProfileTypeResolver.TryResolve(out var profileType, out var error))
            {
                Debug.LogError(error);
                return;
            }

            var assetPath = EditorUtility.SaveFilePanelInProject("Save Manifest", "ManifestProfile", "asset", "Select where to create new Manifest Profile");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            var manifestProfile = CreateInstance(profileType) as ManifestProfileSO;
            if (manifestProfile == null)
            {
                Debug.LogError($"Failed to create manifest profile of type '{profileType.FullName}'.");
                return;
            }

            AssetDatabase.CreateAsset(manifestProfile, assetPath);
            Undo.RegisterCreatedObjectUndo(manifestProfile, "Create Manifest Profile");
            AssetDatabase.SaveAssets();
            Refresh();
        }

        private void HandleRefreshClicked()
        {
            Refresh();
        }

        private void HandlePackageManagerClicked()
        {
            EditorApplication.ExecuteMenuItem("Window/Package Management/Package Manager");
        }

        private void HandlePlayerSettingsClicked()
        {
            SettingsService.OpenProjectSettings("Project/Player");
        }

        private void HandleBuildProfilesClicked()
        {
            EditorApplication.ExecuteMenuItem("File/Build Profiles");
        }

        private void HandleApplyManifestButtonClicked()
        {
            LogPipelineStartError(ManifestorBuildPipeline.Apply(_customBuildData.selectedManifestProfile));
        }

        private void HandleCustomBuildPipelineCompleted(
            ManifestorBuildOperation operation,
            ManifestorBuildPipelineStatus pipelineStatus)
        {
            Refresh();
        }

        private static void LogPipelineStartError(ManifestorResult result)
        {
            if (!result.success)
            {
                Debug.LogError(result.message);
            }
        }

        private static List<ManifestProfileData> FindManifests()
        {
            ManifestorSettings.instance.TryGetLastAppliedProfilePath(out var profilePath);

            return AssetDatabase.FindAssets("t:ManifestProfileSO")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<ManifestProfileSO>)
                .Select(manifest => new ManifestProfileData
                {
                    manifestProfile = manifest,
                    isActive = AssetDatabase.GetAssetPath(manifest) == profilePath
                })
                .ToList();
        }

        [Serializable]
        private class CustomBuildData
        {
            public List<ManifestProfileData> manifests = new();
            public ManifestProfileSO activeManifestProfile;
            public ManifestProfileSO selectedManifestProfile;
        }

        [Serializable]
        private class ManifestProfileData
        {
            public ManifestProfileSO manifestProfile;
            public bool isActive;
        }
    }
}
