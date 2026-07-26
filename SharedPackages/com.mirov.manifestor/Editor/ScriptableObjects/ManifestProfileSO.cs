using UnityEngine;
using UnityEditor.Build.Profile;

namespace Mirov.Manifestor.Editor
{
    [CreateAssetMenu(menuName = "Manifestor/Platform Profile", fileName = "ManifestPlatformProfile")]
    public sealed class ManifestProfileSO : ScriptableObject
    {
        [SerializeField] private string _profileName;
        [SerializeField] private BuildProfile _buildProfile;
        [SerializeField] private PackagesListSO[] _packageLists;
        [SerializeField] private int _version;

        public string profileName => _profileName;
        public BuildProfile buildProfile => _buildProfile;
        public PackagesListSO[] packagesLists => _packageLists;
        public int version => _version;
    }
}
