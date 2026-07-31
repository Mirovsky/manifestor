namespace Mirov.Manifestor.Editor
{
    using System;

    [Serializable]
    public struct PackageListTarget
    {
        public PackagesListSO packageList;
        public string assetPath;

        public PackageListTarget(PackagesListSO packageList, string assetPath)
        {
            this.packageList = packageList;
            this.assetPath = assetPath ?? string.Empty;
        }
    }
}
