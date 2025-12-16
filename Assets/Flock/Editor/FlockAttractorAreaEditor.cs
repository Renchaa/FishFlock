#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FlockAttractorArea))]
    [CanEditMultipleObjects]
    public sealed class FlockAttractorAreaEditor : UnityEditor.Editor {
        SerializedProperty _shape;
        SerializedProperty _sphereRadius;
        SerializedProperty _boxSize;

        SerializedProperty _baseStrength;
        SerializedProperty _falloffPower;
        SerializedProperty _attractedTypes;

        SerializedProperty _usage;
        SerializedProperty _cellPriority;

        void OnEnable() {
            _shape = serializedObject.FindProperty("shape");
            _sphereRadius = serializedObject.FindProperty("sphereRadius");
            _boxSize = serializedObject.FindProperty("boxSize");

            _baseStrength = serializedObject.FindProperty("baseStrength");
            _falloffPower = serializedObject.FindProperty("falloffPower");
            _attractedTypes = serializedObject.FindProperty("attractedTypes");

            _usage = serializedObject.FindProperty("usage");
            _cellPriority = serializedObject.FindProperty("cellPriority");
        }

        static void BeginBox(string title) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);
        }

        static void EndBox() {
            EditorGUILayout.EndVertical();
        }

        static float DrawFloat(SerializedProperty prop, GUIContent label, float minClamp) {
            using (new EditorGUI.IndentLevelScope(0)) {
                EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                float v = EditorGUILayout.FloatField(label, prop.floatValue);
                if (EditorGUI.EndChangeCheck()) {
                    v = Mathf.Max(minClamp, v);
                    prop.floatValue = v;
                }
                EditorGUI.showMixedValue = false;
                return prop.floatValue;
            }
        }

        static float DrawSlider(SerializedProperty prop, GUIContent label, float min, float max) {
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            float v = EditorGUILayout.Slider(label, prop.floatValue, min, max);
            if (EditorGUI.EndChangeCheck()) {
                prop.floatValue = v;
            }
            EditorGUI.showMixedValue = false;
            return prop.floatValue;
        }

        static void DrawVector3(SerializedProperty prop, GUIContent label) {
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            Vector3 v = EditorGUILayout.Vector3Field(label, prop.vector3Value);
            if (EditorGUI.EndChangeCheck()) {
                prop.vector3Value = v;
            }
            EditorGUI.showMixedValue = false;
        }

        static void DrawEnum<TEnum>(SerializedProperty prop, GUIContent label) where TEnum : System.Enum {
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var current = (TEnum)System.Enum.ToObject(typeof(TEnum), prop.enumValueIndex);
            var next = (TEnum)EditorGUILayout.EnumPopup(label, current);
            if (EditorGUI.EndChangeCheck()) {
                prop.enumValueIndex = System.Convert.ToInt32(next);
            }
            EditorGUI.showMixedValue = false;
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Shape dropdown
            EditorGUILayout.PropertyField(_shape);

            // Only show the relevant shape field
            var shapeValue = (FlockAttractorShape)_shape.enumValueIndex;
            if (shapeValue == FlockAttractorShape.Sphere) {
                EditorGUILayout.PropertyField(_sphereRadius); // uses runtime [Header("Sphere Settings")]
            } else {
                EditorGUILayout.PropertyField(_boxSize);      // uses runtime [Header("Box Settings")]
            }

            // Let runtime [Header("Attraction")] and [Header("Usage")] render naturally (no duplicates)
            EditorGUILayout.PropertyField(_baseStrength);
            EditorGUILayout.PropertyField(_falloffPower);

            // Keep default Unity array UI (foldout/dropdown style)
            EditorGUILayout.PropertyField(_attractedTypes, true);

            EditorGUILayout.PropertyField(_usage);
            EditorGUILayout.PropertyField(_cellPriority);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
