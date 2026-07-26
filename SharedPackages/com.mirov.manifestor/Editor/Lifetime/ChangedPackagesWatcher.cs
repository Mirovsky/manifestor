namespace Mirov.Manifestor.Editor
{
    using UnityEditor;
    using UnityEditor.PackageManager;

    [InitializeOnLoad]
    public static class ChangedPackagesWatcher
    {
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

            ManifestorMigrateTool.ShowWindow();
        }
    }
}
