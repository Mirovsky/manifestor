using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Build.Profile;

namespace Manifestor
{
    [CreateAssetMenu(menuName = "Manifestor/Platform Profile", fileName = "ManifestPlatformProfile")]
    public class ManifestProfileSO : ScriptableObject
    {
        [SerializeField] private string _profileName;
        [SerializeField] private BuildProfile _buildProfile;
        [SerializeField] private ManifestorPackagesListSO[] _packageLists;

        public string profileName => _profileName;
        public BuildProfile buildProfile => _buildProfile;
        public IReadOnlyList<ManifestorPackagesListSO> packagesLists => _packageLists ?? Array.Empty<ManifestorPackagesListSO>();
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CustomManifestProfileAttribute : Attribute
    {
    }

    internal static class ManifestProfileTypeResolver
    {
        public static bool TryResolve(out Type profileType, out string error)
        {
            var attributedTypes = TypeCache.GetTypesWithAttribute<CustomManifestProfileAttribute>()
                .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal)
                .ToArray();

            if (attributedTypes.Length == 0)
            {
                profileType = typeof(ManifestProfileSO);
                error = string.Empty;
                return true;
            }

            var invalidTypes = attributedTypes.Where(type =>
                    !type.IsClass ||
                    type.IsAbstract ||
                    type.ContainsGenericParameters ||
                    type == typeof(ManifestProfileSO) ||
                    !typeof(ManifestProfileSO).IsAssignableFrom(type))
                .ToArray();
            if (invalidTypes.Length > 0)
            {
                profileType = null;
                error = $"Types marked with {nameof(CustomManifestProfileAttribute)} must be concrete, non-generic " +
                        $"subclasses of {nameof(ManifestProfileSO)}: {FormatTypes(invalidTypes)}.";
                return false;
            }

            if (attributedTypes.Length > 1)
            {
                profileType = null;
                error = $"Multiple custom manifest profile types were found: {FormatTypes(attributedTypes)}. " +
                        $"Only one type may use {nameof(CustomManifestProfileAttribute)}.";
                return false;
            }

            profileType = attributedTypes[0];
            error = string.Empty;
            return true;
        }

        private static string FormatTypes(Type[] types)
        {
            return string.Join(", ", types.Select(type => type.FullName ?? type.Name));
        }
    }
}
