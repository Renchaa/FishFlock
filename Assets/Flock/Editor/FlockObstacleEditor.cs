using UnityEditor;
using UnityEngine;

namespace Flock.Runtime.Editor {
    [CustomEditor(typeof(Flock.Runtime.FlockObstacle))]
    [CanEditMultipleObjects]
    public sealed class FlockObstacleEditor : UnityEditor.Editor {
        const string ShapePropName = "shape";
        const string SphereRadiusPropName = "sphereRadius";
        const string BoxSizePropName = "boxSize";

        SerializedProperty shapeProp;
        SerializedProperty sphereRadiusProp;
        SerializedProperty boxSizeProp;

        void OnEnable() {
            shapeProp = serializedObject.FindProperty(ShapePropName);
            sphereRadiusProp = serializedObject.FindProperty(SphereRadiusPropName);
            boxSizeProp = serializedObject.FindProperty(BoxSizePropName);
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            EditorGUILayout.PropertyField(shapeProp);

            // If multi-editing with mixed values, don't guess which block to show.
            if (shapeProp.hasMultipleDifferentValues) {
                EditorGUILayout.HelpBox("Multiple objects selected with different Shape values. Set Shape to edit the corresponding settings.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // Draw only the relevant settings.
            var shapeValue = shapeProp.enumValueIndex;

            // 0 = Sphere, 1 = Box (matches your enum order). If that changes, this should be updated accordingly.
            if (shapeValue == 0) {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Sphere Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(sphereRadiusProp, new GUIContent("Radius"));
            } else {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Box Settings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(boxSizeProp, new GUIContent("Size"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
