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

        /**
         * <summary>
         * Draws the custom inspector UI.
         * </summary>
         */
        public override void OnInspectorGUI() {
            serializedObject.Update();

            DrawShapeSection();
            DrawAttractionSection();
            DrawUsageSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawShapeSection() {
            EditorGUILayout.PropertyField(shapeProperty);

            FlockAttractorShape shapeValue = (FlockAttractorShape)shapeProperty.enumValueIndex;
            if (shapeValue == FlockAttractorShape.Sphere) {
                EditorGUILayout.PropertyField(sphereRadiusProperty);
                return;
            }

            EditorGUILayout.PropertyField(boxSizeProperty);
        }

        private void DrawAttractionSection() {
            EditorGUILayout.PropertyField(baseStrengthProperty);
            EditorGUILayout.PropertyField(falloffPowerProperty);
            EditorGUILayout.PropertyField(attractedTypesProperty, true);
        }

        private void DrawUsageSection() {
            EditorGUILayout.PropertyField(usageProperty);
            EditorGUILayout.PropertyField(cellPriorityProperty);
        }


    }
}
#endif
