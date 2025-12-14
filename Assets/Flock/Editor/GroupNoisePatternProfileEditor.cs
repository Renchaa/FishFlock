#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(GroupNoisePatternProfile))]
    public sealed class GroupNoisePatternProfileEditor : UnityEditor.Editor {

        bool commonExpanded = true;
        bool patternExpanded = true;
        bool simpleExpanded = false;
        bool vortexExpanded = false;
        bool sphereExpanded = false;

        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // ---------------- Common base settings ----------------
            if (FlockEditorGUI.BeginSection("Common", ref commonExpanded)) {
                EditorGUILayout.PropertyField(P("baseFrequency"), new GUIContent("Base Frequency"));
                EditorGUILayout.PropertyField(P("timeScale"));
                EditorGUILayout.PropertyField(P("phaseOffset"));
                EditorGUILayout.PropertyField(P("worldScale"));
                EditorGUILayout.PropertyField(P("seed"));
                FlockEditorGUI.EndSection();
            }

            EditorGUILayout.Space(2f);

            // ---------------- Pattern selection ----------------
            SerializedProperty patternProp = P("patternType");
            if (FlockEditorGUI.BeginSection("Pattern Type", ref patternExpanded)) {
                EditorGUILayout.PropertyField(patternProp, new GUIContent("Pattern Type"));
                FlockEditorGUI.EndSection();
            }

            // Read current enum value AFTER drawing the field
            var patternType = (FlockGroupNoisePatternType)patternProp.enumValueIndex;

            EditorGUILayout.Space(2f);

            // ---------------- Pattern-specific settings ----------------
            switch (patternType) {
                case FlockGroupNoisePatternType.SimpleSine:
                case FlockGroupNoisePatternType.VerticalBands:
                    if (FlockEditorGUI.BeginSection("Simple / Bands Extras", ref simpleExpanded)) {
                        EditorGUILayout.PropertyField(P("swirlStrength"));
                        EditorGUILayout.PropertyField(P("verticalBias"));
                        FlockEditorGUI.EndSection();
                    }
                    break;

                case FlockGroupNoisePatternType.Vortex:
                    if (FlockEditorGUI.BeginSection("Vortex Settings", ref vortexExpanded)) {
                        EditorGUILayout.PropertyField(P("vortexCenterNorm"));
                        EditorGUILayout.PropertyField(P("vortexRadius"));
                        EditorGUILayout.PropertyField(P("vortexTightness"));
                        FlockEditorGUI.EndSection();
                    }
                    break;

                case FlockGroupNoisePatternType.SphereShell:
                    if (FlockEditorGUI.BeginSection("Sphere Shell Settings", ref sphereExpanded)) {
                        EditorGUILayout.PropertyField(P("sphereRadius"));
                        EditorGUILayout.PropertyField(P("sphereThickness"));
                        EditorGUILayout.PropertyField(P("sphereSwirlStrength"));
                        EditorGUILayout.PropertyField(P("sphereCenterNorm"));
                        FlockEditorGUI.EndSection();
                    }
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
