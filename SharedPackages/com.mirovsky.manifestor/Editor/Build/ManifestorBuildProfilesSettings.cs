namespace Manifestor.Build
{
    using UnityEditor;
    using UnityEngine;

    [FilePath("ProjectSettings/ManifestorBuildProfilesSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ManifestorBuildProfilesSettings : ScriptableSingleton<ManifestorBuildProfilesSettings>
    {
        [SerializeField] private bool _enabled;

        public bool enabled => _enabled;

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider("Project/Manifestor", SettingsScope.Project)
            {
                label = "Manifestor",
                guiHandler = _ =>
                {
                    EditorGUI.BeginChangeCheck();
                    var enabled = EditorGUILayout.Toggle(
                        new GUIContent("Use Build Profiles Build button"),
                        instance._enabled);
                    if (EditorGUI.EndChangeCheck())
                    {
                        instance._enabled = enabled;
                        instance.Save(true);
                    }

                    EditorGUILayout.HelpBox(
                        "This setting takes effect after the next Editor script reload or restart. " +
                        "Unity supports one build player handler, so another package can replace Manifestor's handler.",
                        MessageType.Info);
                }
            };
            return provider;
        }
    }
}
