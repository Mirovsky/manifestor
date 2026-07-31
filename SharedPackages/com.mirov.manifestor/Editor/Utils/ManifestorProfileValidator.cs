namespace Mirov.Manifestor.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    public static class ManifestorProfileValidator
    {
        public static ManifestorResult Validate(ManifestProfileSO profile)
        {
            var errors = new List<string>();
            if (profile == null)
            {
                errors.Add("Manifest profile is required.");
                return CreateError(errors);
            }

            if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile)))
            {
                errors.Add($"Manifest profile '{profile.name}' must be saved as a project asset.");
            }

            if (string.IsNullOrWhiteSpace(profile.profileName))
            {
                errors.Add("Profile name is required.");
            }

            if (profile.buildProfile == null)
            {
                errors.Add($"Manifest profile '{profile.name}' has no Unity Build Profile assigned.");
            }
            else if (string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile.buildProfile)))
            {
                errors.Add($"Unity Build Profile assigned to manifest profile '{profile.name}' must be saved as a project asset.");
            }

            ValidatePackageLists(profile.packagesLists, errors);
            return errors.Count == 0 ? ManifestorResult.Ok() : CreateError(errors);
        }

        private static void ValidatePackageLists(IReadOnlyList<PackagesListSO> packageLists, List<string> errors)
        {
            if (packageLists == null || packageLists.Count == 0)
            {
                errors.Add("At least one package list is required.");
                return;
            }

            var seenPackageLists = new HashSet<PackagesListSO>();
            var packageOwners = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var listIndex = 0; listIndex < packageLists.Count; listIndex++)
            {
                var packageList = packageLists[listIndex];
                if (packageList == null)
                {
                    errors.Add($"Package list at index {listIndex} is missing.");
                    continue;
                }

                var owner = $"package list '{packageList.name}'";
                if (!seenPackageLists.Add(packageList))
                {
                    errors.Add($"Package list '{packageList.name}' is referenced more than once.");
                    continue;
                }

                ValidatePackages(packageList, owner, packageOwners, errors);
                ValidateScopedRegistries(packageList, owner, errors);
            }
        }

        private static void ValidatePackages(
            PackagesListSO packageList,
            string owner,
            Dictionary<string, string> packageOwners,
            List<string> errors)
        {
            if (packageList.packages == null)
            {
                errors.Add($"Packages collection in {owner} is missing.");
                return;
            }

            for (var packageIndex = 0; packageIndex < packageList.packages.Count; packageIndex++)
            {
                var package = packageList.packages[packageIndex];
                if (package == null)
                {
                    errors.Add($"Package entry at index {packageIndex} in {owner} is missing.");
                    continue;
                }

                var packageName = Normalize(package.packageName);
                if (string.IsNullOrEmpty(packageName))
                {
                    errors.Add($"Package entry at index {packageIndex} in {owner} has an empty package name.");
                }
                else if (packageOwners.TryGetValue(packageName, out var existingOwner))
                {
                    errors.Add($"Package '{packageName}' in {owner} is already declared in {existingOwner}.");
                }
                else
                {
                    packageOwners.Add(packageName, owner);
                }

                if (string.IsNullOrEmpty(Normalize(package.location)))
                {
                    errors.Add($"Package '{packageName}' in {owner} has an empty location.");
                }
            }
        }

        private static void ValidateScopedRegistries(PackagesListSO packageList, string owner, List<string> errors)
        {
            if (packageList.scopedRegistries == null)
            {
                errors.Add($"Scoped registries collection in {owner} is missing.");
                return;
            }

            for (var registryIndex = 0; registryIndex < packageList.scopedRegistries.Count; registryIndex++)
            {
                var registry = packageList.scopedRegistries[registryIndex];
                if (registry == null)
                {
                    errors.Add($"Scoped registry at index {registryIndex} in {owner} is missing.");
                    continue;
                }

                var registryName = Normalize(registry.scopeName);
                if (string.IsNullOrEmpty(registryName))
                {
                    errors.Add($"Scoped registry at index {registryIndex} in {owner} has an empty name.");
                }

                if (string.IsNullOrEmpty(Normalize(registry.scopeUrl)))
                {
                    errors.Add($"Scoped registry '{registryName}' in {owner} has an empty URL.");
                }

                if (registry.scopes == null || registry.scopes.Length == 0)
                {
                    errors.Add($"Scoped registry '{registryName}' in {owner} must contain at least one scope.");
                    continue;
                }

                for (var scopeIndex = 0; scopeIndex < registry.scopes.Length; scopeIndex++)
                {
                    if (string.IsNullOrEmpty(Normalize(registry.scopes[scopeIndex])))
                    {
                        errors.Add($"Scope at index {scopeIndex} in scoped registry '{registryName}' in {owner} is empty.");
                    }
                }
            }
        }

        private static ManifestorResult CreateError(IEnumerable<string> errors)
        {
            return ManifestorResult.Error(
                "Manifest profile validation failed:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, PrefixErrors(errors)));
        }

        private static IEnumerable<string> PrefixErrors(IEnumerable<string> errors)
        {
            foreach (var error in errors)
            {
                yield return $"- {error}";
            }
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
