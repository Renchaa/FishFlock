#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(GroupNoisePatternProfile))]
    public sealed class GroupNoisePatternProfileEditor : UnityEditor.Editor {

        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Common
            EditorGUILayout.PropertyField(P("baseFrequency"), new GUIContent("Base Frequency"));
            EditorGUILayout.PropertyField(P("timeScale"));
            EditorGUILayout.PropertyField(P("phaseOffset"));
            EditorGUILayout.PropertyField(P("worldScale"));
            EditorGUILayout.PropertyField(P("seed"));

            EditorGUILayout.Space(6f);

            // Pattern selection
            SerializedProperty patternProp = P("patternType");
            EditorGUILayout.PropertyField(patternProp, new GUIContent("Pattern Type"));

            var patternType = (FlockGroupNoisePatternType)patternProp.enumValueIndex;

            EditorGUILayout.Space(6f);

            // Pattern-specific
            switch (patternType) {
                case FlockGroupNoisePatternType.SimpleSine:
                case FlockGroupNoisePatternType.VerticalBands:
                    EditorGUILayout.PropertyField(P("swirlStrength"));
                    EditorGUILayout.PropertyField(P("verticalBias"));
                    break;

                case FlockGroupNoisePatternType.Vortex:
                    EditorGUILayout.PropertyField(P("vortexCenterNorm"));
                    EditorGUILayout.PropertyField(P("vortexRadius"));
                    EditorGUILayout.PropertyField(P("vortexTightness"));
                    break;

                case FlockGroupNoisePatternType.SphereShell:
                    EditorGUILayout.PropertyField(P("sphereRadius"));
                    EditorGUILayout.PropertyField(P("sphereThickness"));
                    EditorGUILayout.PropertyField(P("sphereSwirlStrength"));
                    EditorGUILayout.PropertyField(P("sphereCenterNorm"));
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

    }
}
#endif
