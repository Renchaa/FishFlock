// ==========================================
// FishInteractionMatrixEditor.cs (Editor)
// ==========================================
#if UNITY_EDITOR
namespace Flock.Editor {
    using Flock.Runtime;
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(FishInteractionMatrix))]
    public sealed class FishInteractionMatrixEditor : UnityEditor.Editor {
        const float HeaderWidth = 90f;
        const float CellSize = 22f;
        const float ColumnHeaderHeight = 40f;

        FishInteractionMatrix matrix;
        GUIStyle headerStyle;
        GUIStyle cellToggleStyle;

        static readonly GUIContent[] RelationshipOptions = {
            new GUIContent("Friendly"),
            new GUIContent("Avoid"),
            new GUIContent("Neutral"),   // UI label for enum value "None"
        };

        // NEW: which fish row is currently selected in the relations panel
        int selectedFishIndex = -1;

        void OnEnable() {
            // Don’t assume Unity’s timing; just init defensively.
            EnsureInit();
        }

        GUIStyle CellStyle {
            get {
                EnsureInit();
                return cellToggleStyle;
            }
        }

        // ===== REPLACE OnInspectorGUI WITH THIS VERSION =====
        public override void OnInspectorGUI() {
            EnsureInit();
            if (matrix == null) {
                return;
            }

            serializedObject.Update();

            // ----------------------------
            // Fish Types + Defaults (flat)
            // ----------------------------
            EditorGUILayout.LabelField("Fish Types & Defaults", EditorStyles.boldLabel);

            SerializedProperty fishTypesProp = serializedObject.FindProperty("fishTypes");
            if (fishTypesProp != null) {
                using (new EditorGUI.DisabledScope(true)) {
                    FlockEditorGUI.PropertyFieldClamped(
                        fishTypesProp,
                        includeChildren: true,
                        labelOverride: EditorGUIUtility.TrTextContent("Fish Types"));
                }
            }

            SerializedProperty defaultLeaderProp = serializedObject.FindProperty("defaultLeadershipWeight");
            EditorGUILayout.PropertyField(defaultLeaderProp, new GUIContent("Default Leadership Weight"));

            SerializedProperty defaultAvoidProp = serializedObject.FindProperty("defaultAvoidanceWeight");
            EditorGUILayout.PropertyField(defaultAvoidProp, new GUIContent("Default Avoidance Weight"));

            SerializedProperty defaultNeutralProp = serializedObject.FindProperty("defaultNeutralWeight");
            EditorGUILayout.PropertyField(defaultNeutralProp, new GUIContent("Default Neutral Weight"));

            serializedObject.ApplyModifiedProperties();
            matrix.SyncSizeWithFishTypes();

            EditorGUILayout.Space(8f);

            // ----------------------------
            // Interaction Matrix (flat)
            // ----------------------------
            EditorGUILayout.LabelField("Interaction Matrix", EditorStyles.boldLabel);
            DrawInteractionMatrix();

            EditorGUILayout.Space(8f);

            // ----------------------------
            // Relationships (flat)
            // ----------------------------
            EditorGUILayout.LabelField("Relationships", EditorStyles.boldLabel);
            DrawRelationsInspector();

            EditorGUILayout.Space(8f);

            // ----------------------------
            // Weights (flat)
            // ----------------------------
            EditorGUILayout.LabelField("Leadership Weights", EditorStyles.boldLabel);
            DrawLeadershipWeights();

            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Avoidance Weights", EditorStyles.boldLabel);
            DrawAvoidanceWeights();

            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Neutral Weights", EditorStyles.boldLabel);
            DrawNeutralWeights();
        }

        // ===== existing matrix drawing (unchanged) =====
        void DrawInteractionMatrix() {
            int count = matrix.Count;
            FishTypePreset[] types = matrix.FishTypes;

            if (count == 0 || types == null) {
                EditorGUILayout.HelpBox(
                    "Add Fish Types to edit the interaction matrix.",
                    MessageType.Info);
                return;
            }

            // column headers
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(HeaderWidth);

            for (int col = 0; col < count; col++) {
                string label = GetTypeName(types[col]);

                Rect r = GUILayoutUtility.GetRect(
                    CellSize,
                    ColumnHeaderHeight,
                    GUILayout.Width(CellSize),
                    GUILayout.Height(ColumnHeaderHeight));

                DrawVerticalHeaderLabel(r, label);
            }

            EditorGUILayout.EndHorizontal();

            // rows
            for (int row = 0; row < count; row++) {
                EditorGUILayout.BeginHorizontal();

                string rowLabel = GetTypeName(types[row]);
                GUILayout.Label(rowLabel, headerStyle, GUILayout.Width(HeaderWidth));

                for (int col = 0; col < count; col++) {
                    if (col < row) {
                        GUILayout.Space(CellSize);
                        continue;
                    }

                    bool value = matrix.GetInteraction(row, col);

                    bool newValue = GUILayout.Toggle(
                        value,
                        GUIContent.none,
                        CellStyle,
                        GUILayout.Width(CellSize),
                        GUILayout.Height(CellSize));

                    if (newValue != value) {
                        Undo.RecordObject(matrix, "Toggle Fish Interaction");
                        matrix.SetSymmetricInteraction(row, col, newValue);
                        EditorUtility.SetDirty(matrix);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        // ===== NEW: relations panel for a selected fish row =====
        void DrawRelationsInspector() {
            int count = matrix.Count;
            FishTypePreset[] types = matrix.FishTypes;

            if (count == 0 || types == null) {
                return;
            }
            // build popup list for selecting which fish row we are editing
            string[] names = new string[count];
            for (int i = 0; i < count; i++) {
                names[i] = GetTypeName(types[i]);
            }

            if (selectedFishIndex < 0 || selectedFishIndex >= count) {
                selectedFishIndex = 0;
            }

            selectedFishIndex = EditorGUILayout.Popup(
                "Selected Fish",
                selectedFishIndex,
                names);

            EditorGUI.indentLevel++;

            bool hasAnyRelation = false;

            for (int other = 0; other < count; other++) {
                // show self-pair as well, but only if interaction is enabled in matrix
                if (!matrix.GetInteraction(selectedFishIndex, other)) {
                    continue;
                }

                hasAnyRelation = true;

                FishRelationType relation = matrix.GetRelation(selectedFishIndex, other);
                string label = $"{GetTypeName(types[selectedFishIndex])} <-> {GetTypeName(types[other])}";

                int currentIndex = RelationToIndex(relation);

                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup(
                    new GUIContent(label),      // FIX: use GUIContent overload
                    currentIndex,
                    RelationshipOptions);       // GUIContent[]
                if (EditorGUI.EndChangeCheck()) {
                    FishRelationType newRelation = IndexToRelation(newIndex);

                    Undo.RecordObject(matrix, "Change Fish Relationship");
                    matrix.SetSymmetricRelation(selectedFishIndex, other, newRelation);
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

        // === FishInteractionMatrixEditor.cs ===
        // ADD these two helper methods anywhere inside the class:

        static int RelationToIndex(FishRelationType relation) {
            switch (relation) {
                case FishRelationType.Friendly:
                    return 0;
                case FishRelationType.Avoid:
                    return 1;
                default: // None / Neutral
                    return 2;
            }
        }

        static FishRelationType IndexToRelation(int index) {
            switch (index) {
                case 0:
                    return FishRelationType.Friendly;
                case 1:
                    return FishRelationType.Avoid;
                default:
                    return FishRelationType.Neutral; // shown in UI as "Neutral"
            }
        }

        // ===== existing leadership weights drawing (unchanged) =====
        void DrawLeadershipWeights() {
            int count = matrix.Count;
            FishTypePreset[] types = matrix.FishTypes;

            if (count == 0 || types == null) {
                return;
            }

            EditorGUI.indentLevel++;

            for (int i = 0; i < count; i++) {
                string label = GetTypeName(types[i]);
                float weight = matrix.GetLeadershipWeight(i);

                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.FloatField(label, weight);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(matrix, "Change Leadership Weight");
                    matrix.SetLeadershipWeight(i, newWeight);
                    EditorUtility.SetDirty(matrix);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ===== NEW: avoidance weights drawing =====
        void DrawAvoidanceWeights() {
            int count = matrix.Count;
            FishTypePreset[] types = matrix.FishTypes;

            if (count == 0 || types == null) {
                return;
            }

            EditorGUI.indentLevel++;

            for (int i = 0; i < count; i++) {
                string label = GetTypeName(types[i]);
                float weight = matrix.GetAvoidanceWeight(i);

                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.FloatField(label, weight);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(matrix, "Change Avoidance Weight");
                    matrix.SetAvoidanceWeight(i, newWeight);
                    EditorUtility.SetDirty(matrix);
                }
            }

            EditorGUI.indentLevel--;
        }

        void DrawNeutralWeights() {
            int count = matrix.Count;
            FishTypePreset[] types = matrix.FishTypes;

            if (count == 0 || types == null) {
                return;
            }

            EditorGUI.indentLevel++;

            for (int i = 0; i < count; i++) {
                string label = GetTypeName(types[i]);
                float weight = matrix.GetNeutralWeight(i);

                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.FloatField(label, weight);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(matrix, "Change Neutral Weight");
                    matrix.SetNeutralWeight(i, newWeight);
                    EditorUtility.SetDirty(matrix);
                }
            }

            EditorGUI.indentLevel--;
        }

        // ===== vertical header label helper (unchanged) =====
        void DrawVerticalHeaderLabel(Rect rect, string label) {
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
                // Always restore, even if something throws later.
                GUI.matrix = oldMatrix;
            }
        }

        static string GetTypeName(FishTypePreset preset) {
            if (preset == null) {
                return "<null>";
            }

            if (!string.IsNullOrEmpty(preset.DisplayName)) {
                return preset.DisplayName;
            }

            return preset.name;
        }
        void EnsureInit() {
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

    }
}
#endif
