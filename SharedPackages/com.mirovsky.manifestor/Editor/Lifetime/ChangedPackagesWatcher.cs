namespace Manifestor
{
    using UI;
    using UnityEditor;
    using UnityEditor.PackageManager;
    using UnityEngine;

    [InitializeOnLoad]
    public static class ChangedPackagesWatcher
    {
        private static bool _refreshQueued;
        private static double _checkAfter;
        private const double DebounceSeconds = 0.5d;

        static ChangedPackagesWatcher()
        {
            Events.registeredPackages += EventsOnRegisteredPackages;
        }

        private static void EventsOnRegisteredPackages(PackageRegistrationEventArgs args)
        {
            if (args.added.Count == 0 &&
                args.removed.Count == 0 &&
                args.changedFrom.Count == 0 &&
                args.changedTo.Count == 0)
            {
                return;
            }

            QueueDiffCheck();
        }

        private static void QueueDiffCheck()
        {
            _checkAfter = EditorApplication.timeSinceStartup + DebounceSeconds;
            if (_refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
            EditorApplication.update += CheckForActionableChanges;
        }

        private static void CheckForActionableChanges()
        {
            if (EditorApplication.timeSinceStartup < _checkAfter)
            {
                return;
            }

            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                _checkAfter = EditorApplication.timeSinceStartup + DebounceSeconds;
                return;
            }

            StopQueuedDiffCheck();
            try
            {
                var manifest = ManifestorIO.LoadExistingManifest();
                if (manifest != null && manifest.manifestorData.createdByProfile)
                {
                    return;
                }

                if (ManifestPackageDiffUtility.CreateManifestDiff().hasChanges)
                {
                    ManifestorMigrateTool.ShowWindow();
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Manifestor could not inspect package changes: {exception.Message}");
            }
        }

        private static void StopQueuedDiffCheck()
        {
            EditorApplication.update -= CheckForActionableChanges;
            _refreshQueued = false;
        }
    }
}
