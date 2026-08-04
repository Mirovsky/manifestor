namespace Manifestor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using Newtonsoft.Json;
    using SerializedData;
    using UnityEngine;

    public static class ManifestorIO
    {
        private const string ManifestPath = "Packages/manifest.json";

        public static ProjectManifest LoadExistingManifest()
        {
            var manifestString = File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : "{}";
            return JsonConvert.DeserializeObject<ProjectManifest>(manifestString);
        }

        public static ProjectManifest ConvertToManifest(ManifestProfileSO profile)
        {
            var scopedRegistries = profile.packagesLists
                .SelectMany(l => l.scopedRegistries)
                .Where(registry => registry != null)
                .Select(registry => new
                {
                    name = StringUtils.Normalize(registry.scopeName),
                    url = StringUtils.Normalize(registry.scopeUrl),
                    scopes = NormalizeValues(registry.scopes)
                })
                .GroupBy(registry => CreateRegistryKey(registry.name, registry.url, registry.scopes), StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(registry => new ScopedManifestRegistry(registry.name, registry.url, registry.scopes))
                .ToArray();

            var dependencies = profile.packagesLists
                .SelectMany(list => list.packages)
                .Where(package => package != null)
                .ToDictionary(package => StringUtils.Normalize(package.packageName), package => StringUtils.Normalize(package.location));

            var manifestorData = new ManifestorData(
                StringUtils.Normalize(profile.profileName),
                createdByProfile: true,
                dependenciesFingerprint: CalculateDependenciesFingerprint(dependencies));

            var testables = profile.packagesLists
                .SelectMany(list => list.testables)
                .Distinct()
                .ToArray();

            return new ProjectManifest(
                manifestorData,
                scopedRegistries,
                dependencies,
                enableLockFile: true,
                resolutionStrategy: "lowest",
                testables: testables,
                pinnedPackages: Array.Empty<string>()
            );
        }

        public static void SaveManifest(ProjectManifest manifest)
        {
            SaveManifestTextAtomic(SerializeManifest(manifest));
        }

        internal static bool ManifestExists()
        {
            return File.Exists(ManifestPath);
        }

        internal static string LoadManifestText()
        {
            return File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : string.Empty;
        }

        internal static bool HasUnchangedGeneratedDependencies(ProjectManifest manifest)
        {
            if (manifest == null ||
                !manifest.manifestorData.createdByProfile ||
                string.IsNullOrEmpty(manifest.manifestorData.dependenciesFingerprint))
            {
                return false;
            }

            var currentFingerprint = CalculateDependenciesFingerprint(manifest.dependencies);
            return string.Equals(
                manifest.manifestorData.dependenciesFingerprint,
                currentFingerprint,
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasMatchingGeneratedManifest(ManifestProfileSO profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (!ManifestExists())
            {
                return false;
            }

            ProjectManifest currentManifest;
            try
            {
                currentManifest = LoadExistingManifest();
            }
            catch (JsonException)
            {
                return false;
            }

            var expectedManifest = ConvertToManifest(profile);
            return ManifestsEqual(currentManifest, expectedManifest);
        }

        internal static void RefreshDependenciesFingerprint(ProjectManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (manifest.dependencies == null)
            {
                throw new InvalidOperationException("The project manifest has no dependencies collection.");
            }

            manifest.SetDependenciesFingerprint(CalculateDependenciesFingerprint(manifest.dependencies));
            SaveManifest(manifest);
        }

        internal static void SaveManifestTextAtomic(string json)
        {
            var temporaryPath = ManifestPath + ".manifestor.tmp";
            try
            {
                File.WriteAllText(temporaryPath, json ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (File.Exists(ManifestPath))
                {
                    File.Replace(temporaryPath, ManifestPath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temporaryPath, ManifestPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        internal static void DeleteManifest()
        {
            if (File.Exists(ManifestPath))
            {
                File.Delete(ManifestPath);
            }
        }

        private static string SerializeManifest(ProjectManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return JsonConvert.SerializeObject(manifest, Formatting.Indented) + Environment.NewLine;
        }

        private static bool ManifestsEqual(ProjectManifest current, ProjectManifest expected)
        {
            return current != null &&
                   expected != null &&
                   current.manifestorData.createdByProfile == expected.manifestorData.createdByProfile &&
                   string.Equals(current.manifestorData.name, expected.manifestorData.name, StringComparison.Ordinal) &&
                   string.Equals(
                       current.manifestorData.dependenciesFingerprint,
                       expected.manifestorData.dependenciesFingerprint,
                       StringComparison.OrdinalIgnoreCase) &&
                   HasUnchangedGeneratedDependencies(current) &&
                   current.enableLockFile == expected.enableLockFile &&
                   string.Equals(current.resolutionStrategy, expected.resolutionStrategy, StringComparison.Ordinal) &&
                   CanonicalDependencies(current.dependencies).SequenceEqual(CanonicalDependencies(expected.dependencies)) &&
                   CanonicalRegistries(current.scopedRegistries).SequenceEqual(CanonicalRegistries(expected.scopedRegistries)) &&
                   CanonicalValues(current.testables).SequenceEqual(CanonicalValues(expected.testables)) &&
                   CanonicalValues(current.pinnedPackages).SequenceEqual(CanonicalValues(expected.pinnedPackages));
        }

        private static string[] CanonicalDependencies(IReadOnlyDictionary<string, string> dependencies)
        {
            if (dependencies == null)
            {
                return new[] { "<missing>" };
            }

            return dependencies
                .Select(dependency => CreateLengthPrefixedKey(
                    StringUtils.Normalize(dependency.Key),
                    StringUtils.Normalize(dependency.Value)))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] CanonicalRegistries(IReadOnlyList<ScopedManifestRegistry> registries)
        {
            return (registries ?? Array.Empty<ScopedManifestRegistry>())
                .Select(registry => CreateRegistryKey(
                    StringUtils.Normalize(registry.name),
                    StringUtils.Normalize(registry.url),
                    NormalizeValues(registry.scopes)))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] CanonicalValues(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Select(value => value == null ? "0:" : $"1:{value.Length}:{value}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string CreateLengthPrefixedKey(string first, string second)
        {
            first ??= string.Empty;
            second ??= string.Empty;
            return $"{first.Length}:{first}{second.Length}:{second}";
        }

        private static string CalculateDependenciesFingerprint(IReadOnlyDictionary<string, string> dependencies)
        {
            var canonicalDependencies = new StringBuilder();
            var normalizedDependencies = (dependencies ?? new Dictionary<string, string>())
                .Select(dependency => new
                {
                    packageName = StringUtils.Normalize(dependency.Key),
                    packageLocation = StringUtils.Normalize(dependency.Value)
                })
                .Where(dependency => !string.IsNullOrEmpty(dependency.packageName))
                .OrderBy(dependency => dependency.packageName, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.packageLocation, StringComparer.Ordinal);

            foreach (var dependency in normalizedDependencies)
            {
                canonicalDependencies
                    .Append(dependency.packageName.Length)
                    .Append(':')
                    .Append(dependency.packageName)
                    .Append(dependency.packageLocation.Length)
                    .Append(':')
                    .Append(dependency.packageLocation);
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalDependencies.ToString()));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string[] NormalizeValues(System.Collections.Generic.IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Select(StringUtils.Normalize)
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string CreateRegistryKey(string name, string url, string[] scopes)
        {
            return name + "\n" + url + "\n" + string.Join("\n", scopes);
        }
    }
}
