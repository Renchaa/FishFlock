#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /**
     * <summary>
     * Custom inspector for <see cref="FlockAttractorArea"/> that conditionally displays shape-specific
     * properties while preserving the runtime header layout for the remaining fields.
     * </summary>
     */
    [CustomEditor(typeof(FlockAttractorArea))]

    [CanEditMultipleObjects]
    public sealed class FlockAttractorAreaEditor : UnityEditor.Editor {
        private SerializedProperty shapeProperty;
        private SerializedProperty sphereRadiusProperty;
        private SerializedProperty boxSizeProperty;

        private SerializedProperty baseStrengthProperty;
        private SerializedProperty falloffPowerProperty;
        private SerializedProperty attractedTypesProperty;

        private SerializedProperty usageProperty;
        private SerializedProperty cellPriorityProperty;

        // Cache serialized properties once per enable to avoid repeated string lookups in GUI.
        private void OnEnable() {
            shapeProperty = serializedObject.FindProperty("shape");
            sphereRadiusProperty = serializedObject.FindProperty("sphereRadius");
            boxSizeProperty = serializedObject.FindProperty("boxSize");

            baseStrengthProperty = serializedObject.FindProperty("baseStrength");
            falloffPowerProperty = serializedObject.FindProperty("falloffPower");
            attractedTypesProperty = serializedObject.FindProperty("attractedTypes");

            usageProperty = serializedObject.FindProperty("usage");
            cellPriorityProperty = serializedObject.FindProperty("cellPriority");
        }

        private void DrawShapeSection() {
            // Shape dropdown
            EditorGUILayout.PropertyField(shapeProperty);

            // Only show the relevant shape field
            FlockAttractorShape shapeValue = (FlockAttractorShape)shapeProperty.enumValueIndex;
            if (shapeValue == FlockAttractorShape.Sphere) {
                EditorGUILayout.PropertyField(sphereRadiusProperty); // uses runtime [Header("Sphere Settings")]
                return;
            }

            EditorGUILayout.PropertyField(boxSizeProperty); // uses runtime [Header("Box Settings")]
        }

        private void DrawAttractionSection() {
            // Let runtime [Header("Attraction")] render naturally (no duplicates)
            EditorGUILayout.PropertyField(baseStrengthProperty);
            EditorGUILayout.PropertyField(falloffPowerProperty);

            // Keep default Unity array UI (foldout/dropdown style)
            EditorGUILayout.PropertyField(attractedTypesProperty, true);
        }

        private void DrawUsageSection() {
            // Let runtime [Header("Usage")] render naturally (no duplicates)
            EditorGUILayout.PropertyField(usageProperty);
            EditorGUILayout.PropertyField(cellPriorityProperty);
        }

        /** <inheritdoc /> */
        public override void OnInspectorGUI() {
            serializedObject.Update();

            DrawShapeSection();
            DrawAttractionSection();
            DrawUsageSection();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
