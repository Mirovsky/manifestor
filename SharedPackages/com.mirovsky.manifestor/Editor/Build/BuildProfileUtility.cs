namespace Manifestor.Build
{
    using System;
    using UnityEditor;
    using UnityEditor.Build.Profile;

    internal static class BuildProfileUtility
    {
        private const string BuildTargetPropertyName = "m_BuildTarget";

        public static BuildTarget GetBuildTarget(BuildProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var serializedObject = new SerializedObject(profile);
            var buildTargetProperty = serializedObject.FindProperty(BuildTargetPropertyName);
            if (buildTargetProperty == null)
            {
                throw new InvalidOperationException(
                    $"BuildProfile serialized property '{BuildTargetPropertyName}' was not found.");
            }

            return (BuildTarget)buildTargetProperty.intValue;
        }
    }
}
