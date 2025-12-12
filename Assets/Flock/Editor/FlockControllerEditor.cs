#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FlockController))]
    public sealed class FlockControllerEditor : UnityEditor.Editor {
        SerializedProperty fishTypesProp;

        void OnEnable() {
            fishTypesProp = serializedObject.FindProperty("fishTypes");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Read-only fish types (driven ONLY by FlockSetup via FlockEditorWindow)
            if (fishTypesProp != null) {
                using (new EditorGUI.DisabledScope(true)) {
                    EditorGUILayout.PropertyField(
                        fishTypesProp,
                        new GUIContent("Fish Types (from FlockSetup)"),
                        true);
                }
            } else {
                EditorGUILayout.HelpBox(
                    "fishTypes field not found. Check FlockController serialization.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Controller Settings", EditorStyles.boldLabel);

            // Draw the rest of the serialized fields, but skip fishTypes + m_Script
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren)) {
                enterChildren = false;

                if (property.name == "m_Script" || property.name == "fishTypes") {
                    continue;
                }

                EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
