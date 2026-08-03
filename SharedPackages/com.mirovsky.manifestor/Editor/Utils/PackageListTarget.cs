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
}
