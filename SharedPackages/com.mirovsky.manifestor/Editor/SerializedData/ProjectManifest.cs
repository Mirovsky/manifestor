namespace Manifestor.SerializedData
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    [Serializable]
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class ProjectManifest
    {
        [JsonProperty("manifestorData")] private ManifestorData _manifestorData;
        [JsonProperty("scopedRegistries")] private ScopedManifestRegistry[] _scopedRegistries;
        [JsonProperty("dependencies")] private Dictionary<string, string> _dependencies;
        [JsonProperty("enableLockFile")] private bool _enableLockFile;
        [JsonProperty("resolutionStrategy")] private string _resolutionStrategy;
        [JsonProperty("testables")] private string[] _testables;
        [JsonProperty("pinnedPackages")] private string[] _pinnedPackages;

        public ManifestorData manifestorData => _manifestorData;
        public IReadOnlyList<ScopedManifestRegistry> scopedRegistries => _scopedRegistries ?? Array.Empty<ScopedManifestRegistry>();
        public IReadOnlyDictionary<string, string> dependencies => _dependencies;
        public bool enableLockFile => _enableLockFile;
        public string resolutionStrategy => _resolutionStrategy;
        public IReadOnlyCollection<string> testables => _testables ?? Array.Empty<string>();
        public IReadOnlyCollection<string> pinnedPackages => _pinnedPackages ?? Array.Empty<string>();

        public ProjectManifest(
            ManifestorData manifestorData,
            ScopedManifestRegistry[] scopedRegistries,
            Dictionary<string, string> dependencies,
            bool enableLockFile,
            string resolutionStrategy,
            string[] testables,
            string[] pinnedPackages)
        {
            _manifestorData = manifestorData;
            _scopedRegistries = scopedRegistries ?? Array.Empty<ScopedManifestRegistry>();
            _dependencies = dependencies ?? new Dictionary<string, string>();
            _enableLockFile = enableLockFile;
            _resolutionStrategy = resolutionStrategy;
            _testables = testables ?? Array.Empty<string>();
            _pinnedPackages = pinnedPackages ?? Array.Empty<string>();
        }
    }

    [Serializable]
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public struct ScopedManifestRegistry
    {
        [JsonProperty("name")] private string _name;
        [JsonProperty("url")] private string _url;
        [JsonProperty("scopes")] private string[] _scopes;

        public string name => _name;
        public string url => _url;
        public IReadOnlyList<string> scopes => _scopes ?? Array.Empty<string>();

        public ScopedManifestRegistry(string name, string url, string[] scopes)
        {
            _name = name;
            _url = url;
            _scopes = scopes ?? Array.Empty<string>();
        }
    }

    [Serializable]
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public struct ManifestorData
    {
        [JsonProperty("name")] private string _name;
        [JsonProperty("createdByProfile")] private bool _createdByProfile;
        [JsonProperty("dependenciesFingerprint")] private string _dependenciesFingerprint;

        public string name => _name;
        public bool createdByProfile => _createdByProfile;
        public string dependenciesFingerprint => _dependenciesFingerprint ?? string.Empty;

        public ManifestorData(string name)
            : this(name, createdByProfile: false, dependenciesFingerprint: string.Empty)
        {
        }

        public ManifestorData(string name, bool createdByProfile)
            : this(name, createdByProfile, dependenciesFingerprint: string.Empty)
        {
        }

        public ManifestorData(string name, bool createdByProfile, string dependenciesFingerprint)
        {
            _name = name;
            _createdByProfile = createdByProfile;
            _dependenciesFingerprint = dependenciesFingerprint ?? string.Empty;
        }
    }
}
