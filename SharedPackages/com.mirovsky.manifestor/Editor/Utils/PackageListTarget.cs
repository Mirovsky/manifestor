namespace Manifestor
{
    using System;

    [Serializable]
    public struct PackageListTarget
    {
        public ManifestorPackagesListSO packageList;
        public string assetPath;

        public PackageListTarget(ManifestorPackagesListSO packageList, string assetPath)
        {
            this.packageList = packageList;
            this.assetPath = assetPath ?? string.Empty;
        }
    }

    public readonly struct ManifestPackageMigrationSelection
    {
        public readonly ManifestPackageDiffEntry change;
        public readonly ManifestorPackagesListSO packageList;

        public ManifestPackageMigrationSelection(
            ManifestPackageDiffEntry change,
            ManifestorPackagesListSO packageList)
        {
            this.change = change;
            this.packageList = packageList;
        }
    }
}
