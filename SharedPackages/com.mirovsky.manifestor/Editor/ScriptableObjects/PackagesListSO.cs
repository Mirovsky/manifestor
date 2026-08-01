namespace Manifestor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;

    [Serializable]
    public class PackageEntry
    {
        [SerializeField] private string _packageName;
        [SerializeField] private string _location;

        public string packageName => _packageName;
        public string location => _location;

        internal PackageEntry(string packageName, string location)
        {
            _packageName = packageName ?? string.Empty;
            _location = location ?? string.Empty;
        }

        internal bool SetLocation(string location)
        {
            location ??= string.Empty;
            if (_location == location)
            {
                return false;
            }

            _location = location;
            return true;
        }
    }

    [Serializable]
    public class PackagesScopedRegistry
    {
        [SerializeField] private string _scopeName;
        [SerializeField] private string _scopeUrl;
        [SerializeField] private string[] _scopes;

        public string scopeName => _scopeName;
        public string scopeUrl => _scopeUrl;
        public IReadOnlyList<string> scopes => _scopes ?? Array.Empty<string>();

        internal PackagesScopedRegistry(string scopeName, string scopeUrl, IEnumerable<string> scopes)
        {
            _scopeName = scopeName ?? string.Empty;
            _scopeUrl = scopeUrl ?? string.Empty;
            _scopes = NormalizeScopes(scopes);
        }

        internal bool MergeScopes(IEnumerable<string> scopes)
        {
            var currentScopes = _scopes ?? Array.Empty<string>();
            var mergedScopes = NormalizeScopes(currentScopes.Concat(scopes ?? Array.Empty<string>()));
            if (currentScopes.SequenceEqual(mergedScopes))
            {
                return false;
            }

            _scopes = mergedScopes;
            return true;
        }

        private static string[] NormalizeScopes(IEnumerable<string> scopes)
        {
            return (scopes ?? Array.Empty<string>())
                .Select(scope => (scope ?? string.Empty).Trim())
                .Where(scope => !string.IsNullOrEmpty(scope))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }


    [CreateAssetMenu(menuName = "Manifestor/Packages List", fileName = "PackagesList")]
    public sealed class PackagesListSO : ScriptableObject
    {
        [SerializeField] internal List<PackageEntry> _packages = new();
        [SerializeField] internal List<string> _defines = new();
        [SerializeField] internal List<PackagesScopedRegistry> _scopedRegistries = new();

        public IReadOnlyList<PackageEntry> packages => _packages;
        public IReadOnlyList<string> defines => _defines;
        public IReadOnlyList<PackagesScopedRegistry> scopedRegistries => _scopedRegistries;

        public bool AddPackage(string packageName, string location)
        {
            _packages ??= new List<PackageEntry>();
            if (_packages.Exists(package =>
                    package != null &&
                    string.Equals(Normalize(package.packageName), Normalize(packageName), StringComparison.Ordinal)))
            {
                return false;
            }

            _packages.Add(new PackageEntry(packageName, location));
            return true;
        }

        public bool UpdatePackage(string packageName, string currentLocation, string newLocation)
        {
            if (_packages == null)
            {
                return false;
            }

            var changed = false;
            foreach (var package in _packages)
            {
                if (!Matches(package, packageName, currentLocation))
                {
                    continue;
                }

                changed |= package.SetLocation(newLocation);
            }

            return changed;
        }

        public bool RemovePackage(string packageName, string location)
        {
            return _packages != null && _packages.RemoveAll(package => Matches(package, packageName, location)) > 0;
        }

        public bool AddScopedRegistry(string name, string url, IEnumerable<string> scopes)
        {
            var registry = new PackagesScopedRegistry(name, url, scopes);
            if (registry.scopes.Count == 0)
            {
                return false;
            }

            _scopedRegistries ??= new List<PackagesScopedRegistry>();
            var existingRegistry = _scopedRegistries.Find(existing =>
                existing != null &&
                string.Equals(Normalize(existing.scopeName), Normalize(name), StringComparison.Ordinal) &&
                string.Equals(Normalize(existing.scopeUrl), Normalize(url), StringComparison.Ordinal));

            if (existingRegistry != null)
            {
                return existingRegistry.MergeScopes(registry.scopes);
            }

            _scopedRegistries.Add(registry);
            return true;
        }

        private static bool Matches(PackageEntry package, string packageName, string location)
        {
            return package != null &&
                   string.Equals(Normalize(package.packageName), Normalize(packageName), StringComparison.Ordinal) &&
                   string.Equals(Normalize(package.location), Normalize(location), StringComparison.Ordinal);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
