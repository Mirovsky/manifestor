namespace Manifestor.Build
{
    using System;
    using UnityEditor;
    using UnityEditor.Build.Profile;

    public static class BuildProfileUtility
    {
        private const string BuildTargetPropertyName = "m_BuildTarget";
        private const string SubtargetPropertyName = "m_Subtarget";

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

        public static int GetSubtarget(BuildProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var serializedObject = new SerializedObject(profile);
            var subtargetProperty = serializedObject.FindProperty(SubtargetPropertyName);
            if (subtargetProperty == null)
            {
                throw new InvalidOperationException(
                    $"BuildProfile serialized property '{SubtargetPropertyName}' was not found.");
            }

            return subtargetProperty.intValue;
        }
    }
}
