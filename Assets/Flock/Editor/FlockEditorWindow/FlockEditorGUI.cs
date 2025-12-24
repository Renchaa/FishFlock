#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /// <summary>
    /// Shared styles and helper methods for all flock editor UIs.
    /// Keeps section headers / cards consistent across tools.
    /// </summary>
    internal static class FlockEditorGUI {
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
                        padding = FlockEditorUI.Copy(FlockEditorUI.ArrayElementPadding),
                        margin = FlockEditorUI.Copy(FlockEditorUI.ArrayElementMargin)
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
            float gutter = needsFoldoutGutter ? FlockEditorUI.FoldoutGutterWidth : 0f;

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

                float sizeWidth = FlockEditorUI.ArraySizeFieldWidth;
                Rect sizeRect = new Rect(header.xMax - sizeWidth, header.y, sizeWidth, header.height);
                Rect foldoutRect = new Rect(header.x, header.y, header.width - sizeWidth - FlockEditorUI.ArrayHeaderGap, header.height);

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

                if (GUILayout.Button("+", EditorStyles.miniButtonLeft, GUILayout.Width(FlockEditorUI.ArrayPlusMinusButtonWidth))) {
                arrayProp.arraySize++;
                }

                using (new EditorGUI.DisabledScope(arrayProp.arraySize == 0)) {
                    if (GUILayout.Button("-", EditorStyles.miniButtonRight, GUILayout.Width(FlockEditorUI.ArrayPlusMinusButtonWidth))) {
                        arrayProp.arraySize = Mathf.Max(0, arrayProp.arraySize - 1);
                    }
                }
            }

            EditorGUILayout.Space(FlockEditorUI.ArrayFooterSpace);
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
                        padding = FlockEditorUI.Copy(FlockEditorUI.SectionBoxPadding),
                        margin = FlockEditorUI.Copy(FlockEditorUI.SectionBoxMargin)
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
            Rect rect = GUILayoutUtility.GetRect(0f, FlockEditorUI.SectionHeaderRowHeight, GUILayout.ExpandWidth(true));

            // Foldout that occupies the whole row
            expanded = EditorGUI.Foldout(rect, expanded, title, true, SectionHeader);

            if (!expanded) {
                return false;
            }

            EditorGUILayout.BeginVertical(SectionBox);
            EditorGUILayout.Space(FlockEditorUI.BeginSectionBodyTopSpace);
            return true;
        }

        public static void EndSection() {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(FlockEditorUI.EndSectionBottomSpace);
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
            Rect r = GUILayoutUtility.GetRect(0f, FlockEditorUI.CardTitleRowHeight, GUILayout.ExpandWidth(true));
            r.xMin += FlockEditorUI.CardTitleLeftInset;
            EditorGUI.LabelField(r, title, CardHeader);
            EditorGUILayout.Space(FlockEditorUI.CardAfterTitleSpace);
        }

        public static void EndCard() {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(FlockEditorUI.CardAfterCardSpace);
        }
    }
}
#endif
