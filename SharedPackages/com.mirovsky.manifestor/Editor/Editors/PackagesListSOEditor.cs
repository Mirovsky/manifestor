namespace Manifestor.Editor
{
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(PackagesListSO))]

    public class PackagesListSOEditor : Editor
    {
        private static readonly GUIContent LocationLabel = new("Location / Version");
        private static readonly GUIContent DefinesLabel = new("Defines");
        private static readonly GUIContent ScopesLabel = new("Scopes");

        private SerializedProperty _packagesProperty;
        private SerializedProperty _definesProperty;
        private SerializedProperty _scopedRegistriesProperty;

        private void OnEnable()
        {
            _packagesProperty = serializedObject.FindProperty(nameof(PackagesListSO._packages));
            _definesProperty = serializedObject.FindProperty(nameof(PackagesListSO._defines));
            _scopedRegistriesProperty = serializedObject.FindProperty(nameof(PackagesListSO._scopedRegistries));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_packagesProperty, LocationLabel, includeChildren: true);
            EditorGUILayout.PropertyField(_definesProperty, DefinesLabel, includeChildren: true);
            EditorGUILayout.PropertyField(_scopedRegistriesProperty, ScopesLabel, includeChildren: true);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
