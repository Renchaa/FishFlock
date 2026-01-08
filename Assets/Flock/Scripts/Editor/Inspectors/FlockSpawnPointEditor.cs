#if UNITY_EDITOR
using Flock.Scripts.Build.Core.Simulation.Runtime.Spawn;

using UnityEditor;
using UnityEngine;

namespace Flock.Scripts.Editor.Inspectors {
    /**
     * <summary>
     * Custom inspector for <see cref="FlockSpawnPoint"/> that conditionally displays
     * shape-specific spawn parameters.
     * </summary>
     */
    [CustomEditor(typeof(FlockSpawnPoint))]
    [CanEditMultipleObjects]
    public sealed class FlockSpawnPointEditor : UnityEditor.Editor {
        SerializedProperty shapeProperty;
        SerializedProperty radiusProperty;
        SerializedProperty halfExtentsProperty;

        void OnEnable() {
            shapeProperty = serializedObject.FindProperty("shape");
            radiusProperty = serializedObject.FindProperty("radius");
            halfExtentsProperty = serializedObject.FindProperty("halfExtents");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            EditorGUILayout.PropertyField(shapeProperty);

            FlockSpawnShape shapeValue = (FlockSpawnShape)shapeProperty.enumValueIndex;

            EditorGUILayout.Space(4f);

            switch (shapeValue) {
                case FlockSpawnShape.Sphere:
                    DrawSphereSettings();
                    break;

                case FlockSpawnShape.Box:
                    DrawBoxSettings();
                    break;

                case FlockSpawnShape.Point:
                default:
                    DrawPointHelpBox();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        void DrawSphereSettings() {
            EditorGUILayout.LabelField("Sphere Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(radiusProperty, new GUIContent("Radius"));
        }

        void DrawBoxSettings() {
            EditorGUILayout.LabelField("Box Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(halfExtentsProperty, new GUIContent("Half Extents"));
        }

        static void DrawPointHelpBox() {
            EditorGUILayout.HelpBox("Point spawns exactly at the Transform position.", MessageType.Info);
        }
    }
}
#endif
