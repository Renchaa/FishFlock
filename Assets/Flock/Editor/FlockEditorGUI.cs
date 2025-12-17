#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /// <summary>
    /// Shared styles and helper methods for all flock editor UIs.
    /// Keeps section headers / cards consistent across tools.
    /// </summary>
    internal static class FlockEditorGUI {
        const float DefaultLabelWidth = 170f;

        static GUIStyle _sectionHeader;
        static GUIStyle _sectionBox;
        static GUIStyle _cardHeader;
        // Add near other cached styles in FlockEditorGUI
        static GUIStyle _arrayFoldout;
        static GUIStyle _arrayElementBox;

        public static GUIStyle ArrayElementBox {
            get {
                if (_arrayElementBox == null) {
                    _arrayElementBox = new GUIStyle("HelpBox") {
                        padding = new RectOffset(8, 8, 6, 6),
                        margin = new RectOffset(0, 0, 2, 2)
                    };
                }
                return _arrayElementBox;
            }
        }

        static GUIStyle ArrayFoldout {
            get {
                if (_arrayFoldout == null) {
                    // Keep foldout behavior, but match normal label sizing/weight.
                    _arrayFoldout = new GUIStyle(EditorStyles.foldout) {
                        fontStyle = FontStyle.Normal,
                        fontSize = EditorStyles.label.fontSize
                    };
                }
                return _arrayFoldout;
            }
        }

        public static void PropertyFieldClamped(SerializedProperty property, bool includeChildren = true, GUIContent labelOverride = null) {
            if (property == null) {
                return;
            }

            GUIContent label = labelOverride ?? EditorGUIUtility.TrTextContent(property.displayName);

            // Custom array drawer: fixes the “shifted right” header and lets us control header styling.
            bool isArray = property.isArray && property.propertyType != SerializedPropertyType.String;
            if (includeChildren && isArray) {
                DrawArrayField(property, label);
                return;
            }

            float h = EditorGUI.GetPropertyHeight(property, label, includeChildren);
            Rect r = EditorGUILayout.GetControlRect(true, h, GUILayout.ExpandWidth(true));

            // For foldout-like generic structs/classes, expand clip area left without shifting visuals right.
            bool needsFoldoutGutter = includeChildren && property.propertyType == SerializedPropertyType.Generic;
            float gutter = needsFoldoutGutter ? 16f : 0f;

            Rect groupRect = (gutter > 0f)
                ? new Rect(r.x - gutter, r.y, r.width + gutter, r.height)
                : r;

            GUI.BeginGroup(groupRect);
            {
                Rect local = new Rect(gutter, 0f, r.width, r.height);
                EditorGUI.PropertyField(local, property, label, includeChildren);
            }
            GUI.EndGroup();
        }

        static void DrawArrayField(SerializedProperty arrayProp, GUIContent label) {
            bool headerless = (label == GUIContent.none) || string.IsNullOrEmpty(label.text);

            // Header row (optional): foldout + size field
            if (!headerless) {
                Rect header = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

                const float sizeWidth = 52f;
                Rect sizeRect = new Rect(header.xMax - sizeWidth, header.y, sizeWidth, header.height);
                Rect foldoutRect = new Rect(header.x, header.y, header.width - sizeWidth - 4f, header.height);

                arrayProp.isExpanded = EditorGUI.Foldout(foldoutRect, arrayProp.isExpanded, label, true, ArrayFoldout);

                EditorGUI.BeginChangeCheck();
                int newSize = EditorGUI.IntField(sizeRect, arrayProp.arraySize);
                if (EditorGUI.EndChangeCheck()) {
                    arrayProp.arraySize = Mathf.Max(0, newSize);
                }

                if (!arrayProp.isExpanded) {
                    return;
                }
            }

            // Body
            int bodyIndent = headerless ? 0 : 1;
            using (new EditorGUI.IndentLevelScope(bodyIndent)) {
                if (arrayProp.arraySize == 0) {
                    EditorGUILayout.LabelField("List is Empty", EditorStyles.miniLabel);
                } else {
                    for (int i = 0; i < arrayProp.arraySize; i++) {
                        SerializedProperty element = arrayProp.GetArrayElementAtIndex(i);

                        using (new EditorGUILayout.VerticalScope(ArrayElementBox)) {
                            // Elements draw aligned like normal fields (and nested arrays still work).
                            PropertyFieldClamped(element, includeChildren: true);
                        }
                    }

                }
            }

            // Footer: + / - like the built-in list, right aligned
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("+", EditorStyles.miniButtonLeft, GUILayout.Width(26f))) {
                    arrayProp.arraySize++;
                }

                using (new EditorGUI.DisabledScope(arrayProp.arraySize == 0)) {
                    if (GUILayout.Button("-", EditorStyles.miniButtonRight, GUILayout.Width(26f))) {
                        arrayProp.arraySize = Mathf.Max(0, arrayProp.arraySize - 1);
                    }
                }
            }

            EditorGUILayout.Space(2f);
        }

        public static GUIStyle SectionHeader {
            get {
                if (_sectionHeader == null) {
                    // Foldout-like header, but bold and left-aligned
                    _sectionHeader = new GUIStyle(EditorStyles.foldout) {
                        fontStyle = FontStyle.Bold,
                        richText = false
                    };
                }

                return _sectionHeader;
            }
        }

        public static GUIStyle SectionBox {
            get {
                if (_sectionBox == null) {
                    // "Card" style body using HelpBox as a base
                    _sectionBox = new GUIStyle("HelpBox") {
                        padding = new RectOffset(8, 8, 4, 6),
                        margin = new RectOffset(0, 0, 2, 4)
                    };
                }

                return _sectionBox;
            }
        }

        public static void WithIndentLevel(int indentLevel, System.Action draw) {
            int old = EditorGUI.indentLevel;
            EditorGUI.indentLevel = indentLevel;
            try {
                draw?.Invoke();
            } finally {
                EditorGUI.indentLevel = old;
            }
        }

        /// <summary>
        /// Convenience wrapper to temporarily set a label width for a block of GUI code.
        /// </summary>
        public static void WithLabelWidth(float labelWidth, System.Action draw) {
            float oldWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = labelWidth;
            try {
                draw?.Invoke();
            } finally {
                EditorGUIUtility.labelWidth = oldWidth;
            }
        }

        /// <summary>
        /// Draws a full-width foldout header and, if expanded, begins a boxed section.
        /// Returns true if the body should be drawn.
        /// You MUST call EndSection() if this returns true.
        /// </summary>
        public static bool BeginSection(string title, ref bool expanded) {
            Rect rect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));

            // Foldout that occupies the whole row
            expanded = EditorGUI.Foldout(rect, expanded, title, true, SectionHeader);

            if (!expanded) {
                return false;
            }

            EditorGUILayout.BeginVertical(SectionBox);
            EditorGUILayout.Space(2f);
            return true;
        }

        public static void EndSection() {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        /// <summary>
        /// Helper for a simple N-column row. Caller is responsible for
        /// keeping the number of Begin/EndVertical calls balanced.
        /// </summary>
        public static void BeginColumns() {
            EditorGUILayout.BeginHorizontal();
        }

        public static void EndColumns() {
            EditorGUILayout.EndHorizontal();
        }

        public static GUIStyle CardHeader {
            get {
                if (_cardHeader == null) {
                    _cardHeader = new GUIStyle(EditorStyles.boldLabel) {
                        alignment = TextAnchor.MiddleLeft
                    };
                }
                return _cardHeader;
            }
        }

        /// <summary>
        /// Non-collapsible "card" section header + body.
        /// Use this when you want things grouped but NOT hidden behind foldouts.
        /// </summary>
        public static void BeginCard(string title) {
            EditorGUILayout.BeginVertical(SectionBox, GUILayout.ExpandWidth(true));
            Rect r = GUILayoutUtility.GetRect(0f, 16f, GUILayout.ExpandWidth(true));
            r.xMin += 2f;
            EditorGUI.LabelField(r, title, CardHeader);
            EditorGUILayout.Space(1f);
        }

        public static void EndCard() {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        public static void PropertyFieldClamped(SerializedProperty property, bool includeChildren = true) {
            if (property == null) {
                return;
            }

            float h = EditorGUI.GetPropertyHeight(property, includeChildren);

            // This rect is already "inside" the current card/section layout.
            Rect r = EditorGUILayout.GetControlRect(true, h, GUILayout.ExpandWidth(true));

            // We reserve a left "gutter" ONLY for properties that actually need a foldout arrow (arrays/structs).
            bool needsFoldoutGutter =
                includeChildren &&
                (property.isArray && property.propertyType != SerializedPropertyType.String
                 || property.propertyType == SerializedPropertyType.Generic);

            float gutter = needsFoldoutGutter ? 16f : 0f;

            // Clamp drawing to this rect (Unity 6 array footer buttons stop bleeding outside).
            GUI.BeginGroup(r);
            {
                Rect local = new Rect(gutter, 0f, r.width - gutter, r.height);
                EditorGUI.PropertyField(local, property, includeChildren);
            }
            GUI.EndGroup();
        }
    }
}
#endif
