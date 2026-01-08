using UnityEditor;
using UnityEngine;
using Flock.Scripts.Build.Influence.Environment.Obstacles.Runtime;

namespace Flock.Scripts.Editor.Inspectors {
    /**
     * <summary>
     * Custom inspector for FlockObstacle that displays only the relevant shape settings.
     * </summary>
     */
    [CustomEditor(typeof(FlockObstacle))]

    [CanEditMultipleObjects]
    public sealed class FlockObstacleEditor : UnityEditor.Editor {
        private const string ShapePropName = "shape";
        private const string SphereRadiusPropName = "sphereRadius";
        private const string BoxSizePropName = "boxSize";

        private const int SphereShapeIndex = 0;

        private SerializedProperty shapeProp;
        private SerializedProperty sphereRadiusProp;
        private SerializedProperty boxSizeProp;

        private void OnEnable() {
            shapeProp = serializedObject.FindProperty(ShapePropName);
            sphereRadiusProp = serializedObject.FindProperty(SphereRadiusPropName);
            boxSizeProp = serializedObject.FindProperty(BoxSizePropName);
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            EditorGUILayout.PropertyField(shapeProp);

            if (shapeProp.hasMultipleDifferentValues) {
                EditorGUILayout.HelpBox(
                    "Multiple objects selected with different Shape values. Set Shape to edit the corresponding settings.",
                    MessageType.Info);

                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawShapeSpecificSettings();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawShapeSpecificSettings() {
            int shapeValue = shapeProp.enumValueIndex;

            if (shapeValue == SphereShapeIndex) {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Sphere Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(sphereRadiusProp, new GUIContent("Radius"));
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Box Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(boxSizeProp, new GUIContent("Size"));
        }
    }
}
