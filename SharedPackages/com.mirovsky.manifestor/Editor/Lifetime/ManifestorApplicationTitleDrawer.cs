namespace Manifestor
{
    using UnityEditor;

    [InitializeOnLoad]
    internal static class ManifestorApplicationTitleDrawer
    {
        static ManifestorApplicationTitleDrawer()
        {
            EditorApplication.updateMainWindowTitle -= DrawApplicationTitle;
            EditorApplication.updateMainWindowTitle += DrawApplicationTitle;
            EditorApplication.projectChanged -= RefreshApplicationTitle;
            EditorApplication.projectChanged += RefreshApplicationTitle;
            ObjectChangeEvents.changesPublished -= HandleObjectChanges;
            ObjectChangeEvents.changesPublished += HandleObjectChanges;
            EditorApplication.delayCall += RefreshApplicationTitle;
        }

        private static void DrawApplicationTitle(ApplicationTitleDescriptor titleDescriptor)
        {
            var profileName = ManifestorSettings.instance.appliedProfile?.profileName;
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            titleDescriptor.title = titleDescriptor.title.Replace(
                titleDescriptor.projectName,
                $"{titleDescriptor.projectName} [{profileName}]");
        }

        private static void HandleObjectChanges(ref ObjectChangeEventStream stream)
        {
            var appliedProfile = ManifestorSettings.instance.appliedProfile;
            if (appliedProfile == null)
            {
                return;
            }

            var appliedProfileEntityId = appliedProfile.GetEntityId();
            for (var eventIndex = 0; eventIndex < stream.length; eventIndex++)
            {
                if (stream.GetEventType(eventIndex) != ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    continue;
                }

                stream.GetChangeAssetObjectPropertiesEvent(eventIndex, out var changeEvent);
                if (changeEvent.entityId != appliedProfileEntityId)
                {
                    continue;
                }

                RefreshApplicationTitle();
                return;
            }
        }

        private static void RefreshApplicationTitle()
        {
            EditorApplication.UpdateMainWindowTitle();
        }
    }
}
