#if UNITY_EDITOR
using Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController;
using Flock.Scripts.Build.Influence.Environment.Bounds.Data;

using UnityEditor;

namespace Flock.Scripts.Editor.Inspectors
{
    /**
     * <summary>
     * Custom inspector for <see cref="FlockController"/> that draws editable Fish Types and a consolidated
     * Bounds section, while preserving the default inspector order for the remaining fields.
     * </summary>
     */
    [CustomEditor(typeof(FlockController))]
    public sealed class FlockControllerEditor : UnityEditor.Editor
    {
        private SerializedProperty fishTypesProperty;

        private SerializedProperty boundsTypeProperty;
        private SerializedProperty boundsCenterProperty;
        private SerializedProperty boundsExtentsProperty;
        private SerializedProperty boundsSphereRadiusProperty;

        private void OnEnable()
        {
            fishTypesProperty = serializedObject.FindProperty("fishTypes");

            boundsTypeProperty = serializedObject.FindProperty("boundsType");
            boundsCenterProperty = serializedObject.FindProperty("boundsCenter");
            boundsExtentsProperty = serializedObject.FindProperty("boundsExtents");
            boundsSphereRadiusProperty = serializedObject.FindProperty("boundsSphereRadius");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawFishTypesCard();
            DrawBoundsCard();

            SerializedProperty propertyIterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (propertyIterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (propertyIterator.name == "m_Script" || propertyIterator.name == "fishTypes")
                {
                    continue;
                }

                if (propertyIterator.name == "boundsType"
                    || propertyIterator.name == "boundsCenter"
                    || propertyIterator.name == "boundsExtents"
                    || propertyIterator.name == "boundsSphereRadius")
                {
                    continue;
                }

                // Default Unity inspector drawing (no custom list UI).
                EditorGUILayout.PropertyField(propertyIterator, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFishTypesCard()
        {
            if (fishTypesProperty != null)
            {
                // Default Unity array/list UI.
                EditorGUILayout.PropertyField(fishTypesProperty, true);
                return;
            }

            EditorGUILayout.HelpBox(
                "fishTypes field not found on FlockController. Check serialization field name.",
                MessageType.Warning);
        }

        private void DrawBoundsCard()
        {
            if (boundsTypeProperty == null || boundsCenterProperty == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bounds", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(boundsTypeProperty, true);
                EditorGUILayout.PropertyField(boundsCenterProperty, true);

                FlockBoundsType boundsType = (FlockBoundsType)boundsTypeProperty.enumValueIndex;

                if (boundsType == FlockBoundsType.Box)
                {
                    if (boundsExtentsProperty != null)
                    {
                        EditorGUILayout.PropertyField(boundsExtentsProperty, true);
                    }
                    return;
                }

                if (boundsType == FlockBoundsType.Sphere)
                {
                    if (boundsSphereRadiusProperty != null)
                    {
                        EditorGUILayout.PropertyField(boundsSphereRadiusProperty, true);
                    }
                }
            }
        }
    }
}
#endif
