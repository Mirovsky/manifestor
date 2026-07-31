using System;
using System.Collections.Generic;
using Newtonsoft.Json;

[Serializable]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ProjectManifest
{
    [JsonProperty("manifestorData")]
    private ManifestorData _manifestorData;

    [JsonProperty("scopedRegistries")]
    private ScopedManifestRegistry[] _scopedRegistries;
    [JsonProperty("dependencies")]
    private Dictionary<string, string> _dependencies;
    [JsonProperty("enableLockFile")]
    private bool _enableLockFile;
    [JsonProperty("resolutionStrategy")]
    private string _resolutionStrategy;
    [JsonProperty("testables")]
    private string[] _testables;
    [JsonProperty("pinnedPackages")]
    private string[] _pinnedPackages;

    public ManifestorData manifestorData => _manifestorData;
    public ScopedManifestRegistry[] scopedRegistries => _scopedRegistries;
    public IReadOnlyDictionary<string, string> dependencies => _dependencies;
    public bool enableLockFile => _enableLockFile;
    public string resolutionStrategy => _resolutionStrategy;
    public IReadOnlyCollection<string> testables => _testables;
    public IReadOnlyCollection<string> pinnedPackages => _pinnedPackages;

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
        _scopedRegistries = scopedRegistries;
        _dependencies = dependencies;
        _enableLockFile = enableLockFile;
        _resolutionStrategy = resolutionStrategy;
        _testables = testables;
        _pinnedPackages = pinnedPackages;
    }
}

[Serializable]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public struct ScopedManifestRegistry
{
    [JsonProperty("name")]
    private string _name;
    [JsonProperty("url")]
    private string _url;
    [JsonProperty("scopes")]
    private string[] _scopes;

    public string name => _name;
    public string url => _url;
    public IReadOnlyCollection<string> scopes => _scopes;

    public ScopedManifestRegistry(string name, string url, string[] scopes)
    {
        _name = name;
        _url = url;
        _scopes = scopes;
    }
}

[Serializable]
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public struct ManifestorData
{
    [JsonProperty("name")]
    private string _name;

    public string name => _name;

    public ManifestorData(string name)
    {
        _name = name;
    }
}
