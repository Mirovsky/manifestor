namespace Manifestor.UI
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Build;
    using UnityEditor;
    using UnityEditor.Toolbars;
    using UnityEngine;

    [InitializeOnLoad]
    internal static class ManifestorProfileToolbar
    {
        private const string ToolbarPath = "Manifestor/Profile";

        private static ManifestProfileSO _selectedProfile;
        private static List<ManifestProfileSO> _cachedProfiles;
        private static HashSet<string> _cachedProfilePaths;

        static ManifestorProfileToolbar()
        {
            ManifestorUnityEditorPipeline.completed -= HandlePipelineCompleted;
            ManifestorUnityEditorPipeline.completed += HandlePipelineCompleted;
            ObjectChangeEvents.changesPublished -= HandleObjectChanges;
            ObjectChangeEvents.changesPublished += HandleObjectChanges;
        }

        [MainToolbarElement(ToolbarPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
        public static IEnumerable<MainToolbarElement> CreateToolbar()
        {
            var profiles = FindProfiles();
            EnsureValidSelection(profiles);

            yield return new MainToolbarDropdown(
                new MainToolbarContent(GetProfileName(_selectedProfile), "Select a manifest profile."),
                dropdownRect => ShowProfilesMenu(dropdownRect, profiles))
            {
                enabled = profiles.Count > 0
            };

            yield return new MainToolbarButton(
                new MainToolbarContent("Apply", "Apply the selected manifest profile."),
                ApplySelectedProfile)
            {
                enabled = _selectedProfile != null
            };
        }

        private static List<ManifestProfileSO> FindProfiles()
        {
            if (_cachedProfiles != null)
            {
                return _cachedProfiles;
            }

            _cachedProfiles = ManifestProfileAssets.FindAll();
            _cachedProfilePaths = _cachedProfiles
                .Select(AssetDatabase.GetAssetPath)
                .ToHashSet(StringComparer.Ordinal);
            return _cachedProfiles;
        }

        internal static void InvalidateIfProfilesChanged(
            IEnumerable<string> importedAssets,
            IEnumerable<string> deletedAssets,
            IEnumerable<string> movedAssets,
            IEnumerable<string> movedFromAssetPaths)
        {
            var knownPaths = _cachedProfilePaths;
            var changed = importedAssets.Concat(movedAssets)
                .Any(path => AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(path) != null) ||
                knownPaths != null && deletedAssets.Concat(movedFromAssetPaths).Any(knownPaths.Contains);
            if (!changed)
            {
                return;
            }

            _cachedProfiles = null;
            _cachedProfilePaths = null;
            Refresh();
        }

        private static void EnsureValidSelection(IReadOnlyList<ManifestProfileSO> profiles)
        {
            if (_selectedProfile != null && profiles.Contains(_selectedProfile))
            {
                return;
            }

            var appliedProfile = ManifestorSettings.instance.appliedProfile;
            _selectedProfile = appliedProfile != null && profiles.Contains(appliedProfile)
                ? appliedProfile
                : profiles.FirstOrDefault();
        }

        private static string GetProfileName(ManifestProfileSO profile)
        {
            if (profile == null)
            {
                return "No Profiles";
            }

            return string.IsNullOrWhiteSpace(profile.profileName)
                ? profile.name
                : profile.profileName;
        }

        private static void ShowProfilesMenu(Rect dropdownRect, IReadOnlyList<ManifestProfileSO> profiles)
        {
            var menu = new GenericMenu();
            if (profiles.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Manifest Profiles"));
                menu.DropDown(dropdownRect);
                return;
            }

            var duplicateNames = profiles
                .GroupBy(GetProfileName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var profile in profiles)
            {
                var profileName = GetProfileName(profile);
                var menuLabel = duplicateNames.Contains(profileName)
                    ? $"{profileName} ({AssetDatabase.GetAssetPath(profile).Replace("/", " > ")})"
                    : profileName;
                menu.AddItem(
                    new GUIContent(menuLabel),
                    profile == _selectedProfile,
                    () => SelectProfile(profile));
            }

            menu.DropDown(dropdownRect);
        }

        private static void SelectProfile(ManifestProfileSO profile)
        {
            _selectedProfile = profile;
            Refresh();
        }

        private static void ApplySelectedProfile()
        {
            if (_selectedProfile == null)
            {
                return;
            }

            var result = ManifestorUnityEditorPipeline.Apply(_selectedProfile);
            if (!result.success)
            {
                Debug.LogError(result.message);
            }

            Refresh();
        }

        private static void HandlePipelineCompleted(
            ManifestorBuildOperation operation,
            ManifestorBuildPipelineStatus status)
        {
            if (operation == ManifestorBuildOperation.Apply &&
                status == ManifestorBuildPipelineStatus.Succeeded)
            {
                _selectedProfile = ManifestorSettings.instance.appliedProfile;
            }

            Refresh();
        }

        private static void HandleObjectChanges(ref ObjectChangeEventStream stream)
        {
            for (var eventIndex = 0; eventIndex < stream.length; eventIndex++)
            {
                if (stream.GetEventType(eventIndex) != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    continue;
                }

                stream.GetChangeAssetObjectPropertiesEvent(eventIndex, out var changeEvent);
                if (EditorUtility.EntityIdToObject(changeEvent.entityId) is not ManifestProfileSO)
                {
                    continue;
                }

                Refresh();
                return;
            }
        }

        private static void Refresh()
        {
            MainToolbar.Refresh(ToolbarPath);
        }
    }

    internal sealed class ManifestorProfileToolbarAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            ManifestorProfileToolbar.InvalidateIfProfilesChanged(
                importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
        }
    }
}
