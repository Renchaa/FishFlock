#if UNITY_EDITOR
using Flock.Scripts.Build.Influence.Noise.Profiles;

using UnityEditor;
using UnityEngine;

namespace Flock.Scripts.Editor.Inspectors
{
    /**
     * <summary>
     * Custom inspector for <see cref="GroupNoisePatternProfile"/>. Renders common noise settings first, then displays
     * the additional fields required by the currently selected <see cref="FlockGroupNoisePatternType"/>.
     * </summary>
     */
    [CustomEditor(typeof(GroupNoisePatternProfile))]
    public sealed class GroupNoisePatternProfileEditor : UnityEditor.Editor
    {
        private const string BaseFrequencyPropertyName = "baseFrequency";
        private const string TimeScalePropertyName = "timeScale";
        private const string PhaseOffsetPropertyName = "phaseOffset";
        private const string WorldScalePropertyName = "worldScale";
        private const string SeedPropertyName = "seed";
        private const string PatternTypePropertyName = "patternType";

        private const string SwirlStrengthPropertyName = "swirlStrength";
        private const string VerticalBiasPropertyName = "verticalBias";

        private const string VortexCenterNormPropertyName = "vortexCenterNorm";
        private const string VortexRadiusPropertyName = "vortexRadius";
        private const string VortexTightnessPropertyName = "vortexTightness";

        private const string SphereRadiusPropertyName = "sphereRadius";
        private const string SphereThicknessPropertyName = "sphereThickness";
        private const string SphereSwirlStrengthPropertyName = "sphereSwirlStrength";
        private const string SphereCenterNormPropertyName = "sphereCenterNorm";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCommonProperties();
            FlockGroupNoisePatternType patternType = DrawPatternSelection();
            DrawPatternSpecificProperties(patternType);

            serializedObject.ApplyModifiedProperties();
        }

        private SerializedProperty GetProperty(string propertyName)
        {
            return serializedObject.FindProperty(propertyName);
        }

        private void DrawCommonProperties()
        {
            EditorGUILayout.PropertyField(GetProperty(BaseFrequencyPropertyName), new GUIContent("Base Frequency"));
            EditorGUILayout.PropertyField(GetProperty(TimeScalePropertyName));
            EditorGUILayout.PropertyField(GetProperty(PhaseOffsetPropertyName));
            EditorGUILayout.PropertyField(GetProperty(WorldScalePropertyName));
            EditorGUILayout.PropertyField(GetProperty(SeedPropertyName));

            EditorGUILayout.Space(6f);
        }

        private FlockGroupNoisePatternType DrawPatternSelection()
        {
            SerializedProperty patternTypeProperty = GetProperty(PatternTypePropertyName);
            EditorGUILayout.PropertyField(patternTypeProperty, new GUIContent("Pattern Type"));

            EditorGUILayout.Space(6f);

            return (FlockGroupNoisePatternType)patternTypeProperty.enumValueIndex;
        }

        private void DrawPatternSpecificProperties(FlockGroupNoisePatternType patternType)
        {
            switch (patternType)
            {
                case FlockGroupNoisePatternType.SimpleSine:
                case FlockGroupNoisePatternType.VerticalBands:
                    EditorGUILayout.PropertyField(GetProperty(SwirlStrengthPropertyName));
                    EditorGUILayout.PropertyField(GetProperty(VerticalBiasPropertyName));
                    break;

                case FlockGroupNoisePatternType.Vortex:
                    EditorGUILayout.PropertyField(GetProperty(VortexCenterNormPropertyName));
                    EditorGUILayout.PropertyField(GetProperty(VortexRadiusPropertyName));
                    EditorGUILayout.PropertyField(GetProperty(VortexTightnessPropertyName));
                    break;

                case FlockGroupNoisePatternType.SphereShell:
                    EditorGUILayout.PropertyField(GetProperty(SphereRadiusPropertyName));
                    EditorGUILayout.PropertyField(GetProperty(SphereThicknessPropertyName));
                    EditorGUILayout.PropertyField(GetProperty(SphereSwirlStrengthPropertyName));
                    EditorGUILayout.PropertyField(GetProperty(SphereCenterNormPropertyName));
                    break;
            }
        }
    }
}
#endif
