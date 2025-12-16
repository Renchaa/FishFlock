#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(GroupNoisePatternProfile))]
    public sealed class GroupNoisePatternProfileEditor : UnityEditor.Editor {

        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        static void BeginBoxSection(string title) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);
        }

        static void EndBoxSection() {
            EditorGUILayout.EndVertical();
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // ---------------- Common base settings ----------------
            // ---------------- Common base settings ----------------
            BeginBoxSection("Common");
            {
                EditorGUILayout.PropertyField(P("baseFrequency"), new GUIContent("Base Frequency"));
                EditorGUILayout.PropertyField(P("timeScale"));
                EditorGUILayout.PropertyField(P("phaseOffset"));
                EditorGUILayout.PropertyField(P("worldScale"));
                EditorGUILayout.PropertyField(P("seed"));
            }
            EndBoxSection();

            EditorGUILayout.Space(2f);

            // ---------------- Pattern selection ----------------
            SerializedProperty patternProp = P("patternType");
            BeginBoxSection("Pattern Type");
            {
                EditorGUILayout.PropertyField(patternProp, new GUIContent("Pattern Type"));
            }
            EndBoxSection();

            // Read current enum value AFTER drawing the field
            var patternType = (FlockGroupNoisePatternType)patternProp.enumValueIndex;

            EditorGUILayout.Space(2f);

            // ---------------- Pattern-specific settings ----------------
            switch (patternType) {
                case FlockGroupNoisePatternType.SimpleSine:
                case FlockGroupNoisePatternType.VerticalBands:
                    BeginBoxSection("Simple / Bands Extras"); {
                        EditorGUILayout.PropertyField(P("swirlStrength"));
                        EditorGUILayout.PropertyField(P("verticalBias"));
                    }
                    EndBoxSection();
                    break;

                case FlockGroupNoisePatternType.Vortex:
                    BeginBoxSection("Vortex Settings"); {
                        EditorGUILayout.PropertyField(P("vortexCenterNorm"));
                        EditorGUILayout.PropertyField(P("vortexRadius"));
                        EditorGUILayout.PropertyField(P("vortexTightness"));
                    }
                    EndBoxSection();
                    break;

                case FlockGroupNoisePatternType.SphereShell:
                    BeginBoxSection("Sphere Shell Settings"); {
                        EditorGUILayout.PropertyField(P("sphereRadius"));
                        EditorGUILayout.PropertyField(P("sphereThickness"));
                        EditorGUILayout.PropertyField(P("sphereSwirlStrength"));
                        EditorGUILayout.PropertyField(P("sphereCenterNorm"));
                    }
                    EndBoxSection();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
