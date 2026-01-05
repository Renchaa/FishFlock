#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /**
     * <summary>
     * Custom inspector for <see cref="FishInteractionMatrix"/> that renders the interaction grid,
     * relationship selection, and per-fish weight configuration.
     * </summary>
     */
    [CustomEditor(typeof(FishInteractionMatrix))]
    public sealed class FishInteractionMatrixEditor : UnityEditor.Editor {
        private const float HeaderWidth = 90f;
        private const float CellSize = 22f;
        private const float ColumnHeaderHeight = 40f;

        private static readonly GUIContent[] RelationshipOptions = {
            new GUIContent("Friendly"),
            new GUIContent("Avoid"),
            new GUIContent("Neutral"),
        };

        private FishInteractionMatrix matrix;
        private GUIStyle headerStyle;
        private GUIStyle cellToggleStyle;

        private int selectedFishIndex = -1;

        private GUIStyle CellStyle {
            get {
                EnsureInit();
                return cellToggleStyle;
            }
        }

        private void OnEnable() {
            EnsureInit();
        }

        public override void OnInspectorGUI() {
            EnsureInit();
            if (matrix == null) {
                return;
            }

            serializedObject.Update();

            DrawFishTypesAndDefaults();

            serializedObject.ApplyModifiedProperties();
            matrix.SyncSizeWithFishTypes();

            EditorGUILayout.Space(8f);

            EditorGUILayout.LabelField("Interaction Matrix", EditorStyles.boldLabel);
            DrawInteractionMatrix();

            EditorGUILayout.Space(8f);

            EditorGUILayout.LabelField("Relationships", EditorStyles.boldLabel);
            DrawRelationsInspector();

            EditorGUILayout.Space(8f);

            EditorGUILayout.LabelField("Leadership Weights", EditorStyles.boldLabel);
            DrawLeadershipWeights();

            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Avoidance Weights", EditorStyles.boldLabel);
            DrawAvoidanceWeights();

            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Neutral Weights", EditorStyles.boldLabel);
            DrawNeutralWeights();
        }

        private void DrawFishTypesAndDefaults() {
            EditorGUILayout.LabelField("Fish Types & Defaults", EditorStyles.boldLabel);

            SerializedProperty fishTypesProperty = serializedObject.FindProperty("fishTypes");
            if (fishTypesProperty != null) {
                using (new EditorGUI.DisabledScope(true)) {
                    FlockEditorGUI.PropertyFieldClamped(
                        fishTypesProperty,
                        includeChildren: true,
                        labelOverride: EditorGUIUtility.TrTextContent("Fish Types"));
                }
            }

            SerializedProperty defaultLeaderProperty = serializedObject.FindProperty("defaultLeadershipWeight");
            EditorGUILayout.PropertyField(defaultLeaderProperty, new GUIContent("Default Leadership Weight"));

            SerializedProperty defaultAvoidProperty = serializedObject.FindProperty("defaultAvoidanceWeight");
            EditorGUILayout.PropertyField(defaultAvoidProperty, new GUIContent("Default Avoidance Weight"));

            SerializedProperty defaultNeutralProperty = serializedObject.FindProperty("defaultNeutralWeight");
            EditorGUILayout.PropertyField(defaultNeutralProperty, new GUIContent("Default Neutral Weight"));
        }

        private void DrawInteractionMatrix() {
            int fishTypeCount = matrix.Count;
            FishTypePreset[] fishTypes = matrix.FishTypes;

            if (fishTypeCount == 0 || fishTypes == null) {
                EditorGUILayout.HelpBox("Add Fish Types to edit the interaction matrix.", MessageType.Info);
                return;
            }

            DrawInteractionMatrixColumnHeaders(fishTypeCount, fishTypes);
            DrawInteractionMatrixRows(fishTypeCount, fishTypes);
        }

        private void DrawInteractionMatrixColumnHeaders(int fishTypeCount, FishTypePreset[] fishTypes) {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(HeaderWidth);

            for (int columnIndex = 0; columnIndex < fishTypeCount; columnIndex += 1) {
                string label = GetTypeName(fishTypes[columnIndex]);

                Rect rect = GUILayoutUtility.GetRect(
                    CellSize,
                    ColumnHeaderHeight,
                    GUILayout.Width(CellSize),
                    GUILayout.Height(ColumnHeaderHeight));

                DrawVerticalHeaderLabel(rect, label);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawInteractionMatrixRows(int fishTypeCount, FishTypePreset[] fishTypes) {
            for (int rowIndex = 0; rowIndex < fishTypeCount; rowIndex += 1) {
                EditorGUILayout.BeginHorizontal();

                string rowLabel = GetTypeName(fishTypes[rowIndex]);
                GUILayout.Label(rowLabel, headerStyle, GUILayout.Width(HeaderWidth));

                for (int columnIndex = 0; columnIndex < fishTypeCount; columnIndex += 1) {
                    if (columnIndex < rowIndex) {
                        GUILayout.Space(CellSize);
                        continue;
                    }

                    bool value = matrix.GetInteraction(rowIndex, columnIndex);

                    bool newValue = GUILayout.Toggle(
                        value,
                        GUIContent.none,
                        CellStyle,
                        GUILayout.Width(CellSize),
                        GUILayout.Height(CellSize));

                    if (newValue != value) {
                        Undo.RecordObject(matrix, "Toggle Fish Interaction");
                        matrix.SetSymmetricInteraction(rowIndex, columnIndex, newValue);
                        EditorUtility.SetDirty(matrix);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawRelationsInspector() {
            int fishTypeCount = matrix.Count;
            FishTypePreset[] fishTypes = matrix.FishTypes;

            if (fishTypeCount == 0 || fishTypes == null) {
                return;
            }

            string[] fishTypeNames = BuildFishTypeNames(fishTypes, fishTypeCount);

            if (selectedFishIndex < 0 || selectedFishIndex >= fishTypeCount) {
                selectedFishIndex = 0;
            }

            selectedFishIndex = EditorGUILayout.Popup("Selected Fish", selectedFishIndex, fishTypeNames);

            EditorGUI.indentLevel++;

            bool hasAnyRelation = false;

            for (int otherIndex = 0; otherIndex < fishTypeCount; otherIndex += 1) {
                if (!matrix.GetInteraction(selectedFishIndex, otherIndex)) {
                    continue;
                }

                hasAnyRelation = true;

                FishRelationType relation = matrix.GetRelation(selectedFishIndex, otherIndex);
                string label = $"{GetTypeName(fishTypes[selectedFishIndex])} <-> {GetTypeName(fishTypes[otherIndex])}";

                int currentIndex = RelationToIndex(relation);

                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup(
                    new GUIContent(label),
                    currentIndex,
                    RelationshipOptions);

                if (EditorGUI.EndChangeCheck()) {
                    FishRelationType newRelation = IndexToRelation(newIndex);

                    Undo.RecordObject(matrix, "Change Fish Relationship");
                    matrix.SetSymmetricRelation(selectedFishIndex, otherIndex, newRelation);
                    EditorUtility.SetDirty(matrix);
                }
            }

            if (!hasAnyRelation) {
                EditorGUILayout.HelpBox(
                    "No interactions enabled for this fish. Enable cells in the matrix above first.",
                    MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawLeadershipWeights() {
            int fishTypeCount = matrix.Count;
            FishTypePreset[] fishTypes = matrix.FishTypes;

            if (fishTypeCount == 0 || fishTypes == null) {
                return;
            }

            EditorGUI.indentLevel++;

            for (int fishTypeIndex = 0; fishTypeIndex < fishTypeCount; fishTypeIndex += 1) {
                string label = GetTypeName(fishTypes[fishTypeIndex]);
                float weight = matrix.GetLeadershipWeight(fishTypeIndex);

                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.FloatField(label, weight);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(matrix, "Change Leadership Weight");
                    matrix.SetLeadershipWeight(fishTypeIndex, newWeight);
                    EditorUtility.SetDirty(matrix);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawAvoidanceWeights() {
            int fishTypeCount = matrix.Count;
            FishTypePreset[] fishTypes = matrix.FishTypes;

            if (fishTypeCount == 0 || fishTypes == null) {
                return;
            }

            EditorGUI.indentLevel++;

            for (int fishTypeIndex = 0; fishTypeIndex < fishTypeCount; fishTypeIndex += 1) {
                string label = GetTypeName(fishTypes[fishTypeIndex]);
                float weight = matrix.GetAvoidanceWeight(fishTypeIndex);

                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.FloatField(label, weight);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(matrix, "Change Avoidance Weight");
                    matrix.SetAvoidanceWeight(fishTypeIndex, newWeight);
                    EditorUtility.SetDirty(matrix);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawNeutralWeights() {
            int fishTypeCount = matrix.Count;
            FishTypePreset[] fishTypes = matrix.FishTypes;

            if (fishTypeCount == 0 || fishTypes == null) {
                return;
            }

            EditorGUI.indentLevel++;

            for (int fishTypeIndex = 0; fishTypeIndex < fishTypeCount; fishTypeIndex += 1) {
                string label = GetTypeName(fishTypes[fishTypeIndex]);
                float weight = matrix.GetNeutralWeight(fishTypeIndex);

                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.FloatField(label, weight);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(matrix, "Change Neutral Weight");
                    matrix.SetNeutralWeight(fishTypeIndex, newWeight);
                    EditorUtility.SetDirty(matrix);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawVerticalHeaderLabel(Rect rect, string label) {
            EnsureInit();

            Matrix4x4 oldMatrix = GUI.matrix;
            try {
                GUIStyle style = headerStyle ?? EditorStyles.miniLabel;

                Vector2 labelSize = style.CalcSize(new GUIContent(label ?? string.Empty));
                float desiredWidth = Mathf.Max(labelSize.x, ColumnHeaderHeight);
                float diff = desiredWidth - rect.width;
                if (diff > 0f) {
                    rect.x -= diff * 0.5f;
                    rect.width += diff;
                }

                Vector2 pivot = rect.center;
                GUIUtility.RotateAroundPivot(-90f, pivot);

                GUI.Label(rect, label, style);
            } finally {
                GUI.matrix = oldMatrix;
            }
        }

        private void EnsureInit() {
            if (matrix == null) {
                matrix = (FishInteractionMatrix)target;
            }

            if (headerStyle == null) {
                headerStyle = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false
                };
            }

            if (cellToggleStyle == null) {
                cellToggleStyle = new GUIStyle(EditorStyles.toggle) {
                    fixedWidth = CellSize,
                    fixedHeight = CellSize,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0)
                };
            }
        }

        private static int RelationToIndex(FishRelationType relation) {
            switch (relation) {
                case FishRelationType.Friendly:
                    return 0;
                case FishRelationType.Avoid:
                    return 1;
                default:
                    return 2;
            }
        }

        private static FishRelationType IndexToRelation(int index) {
            switch (index) {
                case 0:
                    return FishRelationType.Friendly;
                case 1:
                    return FishRelationType.Avoid;
                default:
                    return FishRelationType.Neutral;
            }
        }

        private static string[] BuildFishTypeNames(FishTypePreset[] fishTypes, int fishTypeCount) {
            string[] names = new string[fishTypeCount];
            for (int fishTypeIndex = 0; fishTypeIndex < fishTypeCount; fishTypeIndex += 1) {
                names[fishTypeIndex] = GetTypeName(fishTypes[fishTypeIndex]);
            }

            return names;
        }

        private static string GetTypeName(FishTypePreset preset) {
            if (preset == null) {
                return "<null>";
            }

            if (!string.IsNullOrEmpty(preset.DisplayName)) {
                return preset.DisplayName;
            }

            return preset.name;
        }
    }
}
#endif
