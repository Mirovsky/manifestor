using UnityEngine;

namespace Mirov.Manifestor.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;
    using UnityEngine.UIElements;

    public class CustomBuildWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset _customBuildAsset;

        private readonly CustomBuildData _customBuildData = new();

        [MenuItem("Tools/Manifestor/Custom Build")]
        public static void ShowWindow()
        {
            var window = GetWindow<CustomBuildWindow>("Custom Build");
            window.Refresh();
            window.Show();
        }

        private void CreateGUI()
        {
            _customBuildAsset.CloneTree(rootVisualElement);

            var newManifestButton = rootVisualElement.Q<Button>("NewManifestButton");
            newManifestButton.clicked += HandleNewManifestClicked;

            var playerSettingsButton = rootVisualElement.Q<Button>("PlayerSettingsButton");
            playerSettingsButton.clicked += HandlePlayerSettingsClicked;

            var buildProfilesButton = rootVisualElement.Q<Button>("BuildProfilesButton");
            buildProfilesButton.clicked += HandleBuildProfilesClicked;

            var manifestsList = rootVisualElement.Q<ListView>("ManifestsListView");
            manifestsList.selectionChanged += HandleManifestsListSelectionChanged;

            rootVisualElement.dataSource = _customBuildData;
        }

        private void HandleManifestsListSelectionChanged(IEnumerable<object> manifests)
        {
            if (manifests == null || manifests.Count() == 0)
            {
                return;
            }

            var selectedManifest = manifests.First() as ManifestProfileData;
            if (selectedManifest == null)
            {
                return;
            }

            _customBuildData.activeManifestProfile = selectedManifest.manifestProfile;
        }

        private void Refresh()
        {
            var manifests = FindManifests();

            _customBuildData.activeManifestProfile = manifests.First(m => m.isActive)?.manifestProfile;

            _customBuildData.manifests.Clear();
            _customBuildData.manifests.AddRange(manifests);
        }

        private void HandleNewManifestClicked()
        {
            var assetPath = EditorUtility.SaveFilePanelInProject("Save Manifest", "ManifestProfile", "asset", "Select where to create new Manifest Profile");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            var manifestProfile = CreateInstance<ManifestProfileSO>();
            AssetDatabase.CreateAsset(manifestProfile, assetPath);
            Undo.RegisterCreatedObjectUndo(manifestProfile, "Create Manifest Profile");
            AssetDatabase.SaveAssets();
            Refresh();
        }

        private void HandlePlayerSettingsClicked()
        {
            SettingsService.OpenProjectSettings("Project/Player");
        }

        private void HandleBuildProfilesClicked()
        {
            EditorApplication.ExecuteMenuItem("File/Build Profiles");
        }

        private static List<ManifestProfileData> FindManifests()
        {
            ManifestorEditorPrefs.TryGetLastAppliedProfilePath(out var profilePath);

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
        }

        [Serializable]
        private class ManifestProfileData
        {
            public ManifestProfileSO manifestProfile;
            public bool isActive;
        }
    }
}
