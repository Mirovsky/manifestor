namespace Mirov.Manifestor.Editor
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Newtonsoft.Json;
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
            var manifestorData = new ManifestorData(
                profile.profileName
            );

            var scopedRegistries = profile.packagesLists
                .SelectMany(l => l.scopedRegistries)
                .Distinct()
                .Select(r => new ScopedManifestRegistry(r.scopeName, r.scopeUrl, r.scopes))
                .ToArray();

            var dependencies = profile.packagesLists
                .SelectMany(l => l.packages.ToDictionary(e => e.packageName, e => e.location))
                .ToDictionary(k => k.Key, v => v.Value);

            return new ProjectManifest(
                manifestorData,
                scopedRegistries,
                dependencies,
                enableLockFile: true,
                resolutionStrategy: "lowest",
                testables: Array.Empty<string>(),
                pinnedPackages: Array.Empty<string>()
            );
        }

        public static void SaveManifest(ProjectManifest manifest)
        {
            var json = SerializeManifest(manifest);
            File.WriteAllText(ManifestPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public static string CalculateManifestHash(ProjectManifest manifest)
        {
            return Hash128.Compute(SerializeManifest(manifest)).ToString();
        }

        private static string SerializeManifest(ProjectManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return JsonConvert.SerializeObject(manifest, Formatting.Indented) + Environment.NewLine;
        }
    }
}
