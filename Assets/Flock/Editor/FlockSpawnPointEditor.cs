#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FlockSpawnPoint))]
    [CanEditMultipleObjects]
    public sealed class FlockSpawnPointEditor : UnityEditor.Editor {
        SerializedProperty _shape;
        SerializedProperty _radius;
        SerializedProperty _halfExtents;

        void OnEnable() {
            _shape = serializedObject.FindProperty("shape");
            _radius = serializedObject.FindProperty("radius");
            _halfExtents = serializedObject.FindProperty("halfExtents");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_shape);

            var shapeValue = (FlockSpawnShape)_shape.enumValueIndex;

            EditorGUILayout.Space(4f);

            switch (shapeValue) {
                case FlockSpawnShape.Sphere:
                    EditorGUILayout.LabelField("Sphere Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_radius, new GUIContent("Radius"));
                    break;

                case FlockSpawnShape.Box:
                    EditorGUILayout.LabelField("Box Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_halfExtents, new GUIContent("Half Extents"));
                    break;

                case FlockSpawnShape.Point:
                default:
                    EditorGUILayout.HelpBox("Point spawns exactly at the Transform position.", MessageType.Info);
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
