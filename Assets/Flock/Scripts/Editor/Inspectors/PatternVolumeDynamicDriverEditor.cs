#if UNITY_EDITOR
using Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController;
using Flock.Scripts.Build.Influence.PatternVolume.Runtime;
using Flock.Scripts.Build.Influence.PatternVolume.Data;
using Flock.Scripts.Build.Agents.Fish.Profiles;

using UnityEditor;
using UnityEngine;

namespace Flock.Scripts.Editor.Inspectors
{
    [CustomEditor(typeof(PatternVolumeDynamicDriver))]
    public sealed class PatternVolumeDynamicDriverEditor : UnityEditor.Editor
    {
        private PatternVolumeDynamicDriver driver;

        private SerializedProperty targetProperty;
        private SerializedProperty shapeProperty;

        private SerializedProperty sphereRadiusProperty;
        private SerializedProperty sphereThicknessProperty;

        private SerializedProperty boxHalfExtentsProperty;
        private SerializedProperty boxThicknessProperty;

        private SerializedProperty strengthProperty;
        private SerializedProperty createOnEnableProperty;

        private SerializedProperty controllerProperty;
        private SerializedProperty affectedTypesMaskProperty;

        const float toggleWidth = 18f;
        const float labelToToggleGap = 8f;

        private void OnEnable()
        {
            driver = (PatternVolumeDynamicDriver)target;

            targetProperty = serializedObject.FindProperty("target");
            shapeProperty = serializedObject.FindProperty("shape");

            sphereRadiusProperty = serializedObject.FindProperty("sphereRadius");
            sphereThicknessProperty = serializedObject.FindProperty("sphereThickness");

            boxHalfExtentsProperty = serializedObject.FindProperty("boxHalfExtents");
            boxThicknessProperty = serializedObject.FindProperty("boxThickness");

            strengthProperty = serializedObject.FindProperty("strength");
            createOnEnableProperty = serializedObject.FindProperty("createOnEnable");

            controllerProperty = serializedObject.FindProperty("controller");
            affectedTypesMaskProperty = serializedObject.FindProperty("affectedTypesMask");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptField();
            DrawControllerField();

            EditorGUILayout.Space(6f);
            DrawTargetSection();
            EditorGUILayout.Space(6f);
            DrawShapeSection();
            EditorGUILayout.Space(6f);
            DrawPatternSection();
            EditorGUILayout.Space(6f);
            DrawAffectedTypesSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptField()
        {
            SerializedProperty script = serializedObject.FindProperty("m_Script");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(script);
            }
        }

        private void DrawControllerField()
        {
            EditorGUILayout.LabelField("Controller", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(controllerProperty);

            if (controllerProperty.objectReferenceValue != null)
            {
                return;
            }

            FlockController found = FindControllerFallback(driver);
            if (found == null)
            {
                EditorGUILayout.HelpBox(
                    "No FlockController assigned/found on this GameObject or its parents.",
                    MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(
                    $"Found controller in hierarchy: '{found.name}'.",
                    MessageType.Info);

                if (GUILayout.Button("Assign", GUILayout.Width(70f)))
                {
                    controllerProperty.objectReferenceValue = found;
                }
            }
        }

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(targetProperty);
        }

        private void DrawShapeSection()
        {
            EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(shapeProperty);

            if (shapeProperty == null)
            {
                return;
            }

            PatternVolumeShape shape = (PatternVolumeShape)shapeProperty.enumValueIndex;

            EditorGUI.indentLevel++;

            switch (shape)
            {
                case PatternVolumeShape.SphereShell:
                    EditorGUILayout.LabelField("Sphere Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(sphereRadiusProperty);
                    EditorGUILayout.PropertyField(sphereThicknessProperty);
                    break;

                case PatternVolumeShape.BoxShell:
                    EditorGUILayout.LabelField("Box Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(boxHalfExtentsProperty);
                    EditorGUILayout.PropertyField(boxThicknessProperty);
                    break;
            }

            EditorGUI.indentLevel--;
        }

        private void DrawPatternSection()
        {
            EditorGUILayout.LabelField("Pattern", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(strengthProperty);
            EditorGUILayout.PropertyField(createOnEnableProperty);
        }

        private void DrawAffectedTypesSection()
        {
            EditorGUILayout.LabelField("Affected Types", EditorStyles.boldLabel);

            FlockController controller = controllerProperty.objectReferenceValue as FlockController;
            if (controller == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a FlockController to select affected fish types.",
                    MessageType.Info);
                return;
            }

            FishTypePreset[] fishTypes = controller.FishTypes;
            if (fishTypes == null || fishTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Controller has no FishTypes configured.",
                    MessageType.Info);
                return;
            }

            if (fishTypes.Length > 32)
            {
                EditorGUILayout.HelpBox(
                    "More than 32 FishTypes detected. The affectedTypesMask is a uint and can only represent 32 types.\n" +
                    "You must change the mask type (and runtime APIs) if you need more than 32.",
                    MessageType.Error);
                return;
            }

            uint mask = ReadUInt(affectedTypesMaskProperty);
            mask = ClampMask(mask, fishTypes.Length);

            float viewWidth = EditorGUIUtility.currentViewWidth;
            float blockWidth = Mathf.Clamp(viewWidth * 0.55f, 240f, 420f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(blockWidth)))
                {
                    GUIStyle rightLabel = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleRight,
                        clipping = TextClipping.Clip
                    };

                    for (int i = 0; i < fishTypes.Length; i++)
                    {
                        FishTypePreset preset = fishTypes[i];
                        string label = BuildTypeLabel(preset, i);

                        uint bit = 1u << i;
                        bool isOn = (mask & bit) != 0u;

                        Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

                        Rect toggleRect = new Rect(row.xMax - toggleWidth, row.y, toggleWidth, row.height);
                        Rect labelRect = new Rect(row.x, row.y, row.width - toggleWidth - labelToToggleGap, row.height);

                        EditorGUI.LabelField(labelRect, label, rightLabel);

                        bool newOn = EditorGUI.Toggle(toggleRect, isOn);
                        if (newOn != isOn)
                        {
                            mask = newOn ? (mask | bit) : (mask & ~bit);
                        }
                    }

                    EditorGUILayout.Space(6f);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        const float buttonWidth = 60f;

                        if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(buttonWidth)))
                        {
                            mask = ClampMask(uint.MaxValue, fishTypes.Length);
                        }

                        if (GUILayout.Button("None", EditorStyles.miniButton, GUILayout.Width(buttonWidth)))
                        {
                            mask = 0u;
                        }
                    }
                }
            }

            WriteUInt(affectedTypesMaskProperty, mask);
        }

        private static string BuildTypeLabel(FishTypePreset preset, int index)
        {
            if (preset == null)
            {
                return $"<Null {index}>";
            }

            if (!string.IsNullOrEmpty(preset.DisplayName))
            {
                return preset.DisplayName;
            }

            return preset.name;
        }

        private static uint ClampMask(uint mask, int typeCount)
        {
            if (typeCount <= 0)
            {
                return 0u;
            }

            if (typeCount >= 32)
            {
                return mask;
            }

            uint valid = (1u << typeCount) - 1u;
            return mask & valid;
        }

        private static uint ReadUInt(SerializedProperty property)
        {
            return property != null ? (uint)property.longValue : 0u;
        }

        private static void WriteUInt(SerializedProperty property, uint value)
        {
            if (property != null)
            {
                property.longValue = value;
            }
        }

        private static FlockController FindControllerFallback(PatternVolumeDynamicDriver driver)
        {
            if (driver == null)
            {
                return null;
            }

            return driver.GetComponent<FlockController>()
                ?? driver.GetComponentInParent<FlockController>();
        }
    }
}
#endif
