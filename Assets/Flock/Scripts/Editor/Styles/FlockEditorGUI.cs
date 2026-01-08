#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flock.Scripts.Editor.Window {
    /**
    * <summary>
    * Shared styles and helper methods for all flock editor UIs.
    * Keeps section headers / cards consistent across tools.
    * </summary>
    */
    public static class FlockEditorGUI {

        // Cached GUIStyles (lazy-initialized).
        private static GUIStyle _sectionHeader;
        private static GUIStyle _sectionBox;
        private static GUIStyle _cardHeader;
        private static GUIStyle _arrayFoldout;
        private static GUIStyle _arrayElementBox;

        /**
        * <summary>
        * Box style used for array elements, matching Unity's HelpBox but with flock UI padding/margins.
        * </summary>
        */
        public static GUIStyle ArrayElementBox {
            get {
                if (_arrayElementBox == null) {
                    _arrayElementBox = new GUIStyle("HelpBox") {
                        padding = EditorUI.Copy(EditorUI.ArrayElementPadding),
                        margin = EditorUI.Copy(EditorUI.ArrayElementMargin)
                    };
                }

                return _arrayElementBox;
            }
        }

        /**
        * <summary>
        * Card body style using HelpBox as a base.
        * </summary>
        */
        public static GUIStyle SectionBox {
            get {
                if (_sectionBox == null) {
                    _sectionBox = new GUIStyle("HelpBox") {
                        padding = EditorUI.Copy(EditorUI.SectionBoxPadding),
                        margin = EditorUI.Copy(EditorUI.SectionBoxMargin)
                    };
                }

                return _sectionBox;
            }
        }

        /**
        * <summary>
        * Header style for card titles.
        * </summary>
        */
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

        private static GUIStyle ArrayFoldout {
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

        /**
        * <summary>
        * Draws a property field while preventing foldout indentation from shifting the visuals to the right.
        * If the property is an array (excluding string), a custom array drawer is used when children are included.
        * </summary>
        * <param name="property">The property to draw.</param>
        * <param name="includeChildren">Whether to include child properties.</param>
        * <param name="labelOverride">Optional label override. If null, the display name is used.</param>
        */
        public static void PropertyFieldClamped(SerializedProperty property, bool includeChildren = true, GUIContent labelOverride = null) {
            if (property == null) {
                return;
            }

            GUIContent label = labelOverride ?? EditorGUIUtility.TrTextContent(property.displayName);

            bool isArray = property.isArray && property.propertyType != SerializedPropertyType.String;
            if (includeChildren && isArray) {
                DrawArrayField(property, label);
                return;
            }

            float propertyHeight = EditorGUI.GetPropertyHeight(property, label, includeChildren);
            Rect controlRect = EditorGUILayout.GetControlRect(true, propertyHeight, GUILayout.ExpandWidth(true));

            bool needsFoldoutGutter = includeChildren && property.propertyType == SerializedPropertyType.Generic;
            float foldoutGutterWidth = needsFoldoutGutter ? EditorUI.FoldoutGutterWidth : 0f;

            Rect groupRect = foldoutGutterWidth > 0f
                ? new Rect(controlRect.x - foldoutGutterWidth, controlRect.y, controlRect.width + foldoutGutterWidth, controlRect.height)
                : controlRect;

            DrawPropertyInClampedGroup(groupRect, foldoutGutterWidth, controlRect.width, controlRect.height, property, label, includeChildren);
        }

        /**
        * <summary>
        * Convenience wrapper to temporarily set a label width for a block of GUI code.
        * </summary>
        * <param name="labelWidth">The label width to apply during the draw callback.</param>
        * <param name="draw">The draw callback.</param>
        */
        public static void WithLabelWidth(float labelWidth, System.Action draw) {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = labelWidth;

            try {
                draw?.Invoke();
            } finally {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        /**
        * <summary>
        * Begins a non-collapsible "card" section header + body.
        * Use this when you want things grouped but not hidden behind foldouts.
        * </summary>
        * <param name="title">The card title.</param>
        */
        public static void BeginCard(string title) {
            EditorGUILayout.BeginVertical(SectionBox, GUILayout.ExpandWidth(true));

            Rect titleRowRect = GUILayoutUtility.GetRect(0f, EditorUI.CardTitleRowHeight, GUILayout.ExpandWidth(true));
            titleRowRect.xMin += EditorUI.CardTitleLeftInset;

            EditorGUI.LabelField(titleRowRect, title, CardHeader);
            EditorGUILayout.Space(EditorUI.CardAfterTitleSpace);
        }

        /**
        * <summary>
        * Ends a card section started with <see cref="BeginCard"/>.
        * </summary>
        */
        public static void EndCard() {
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(EditorUI.CardAfterCardSpace);
        }

        private static void DrawPropertyInClampedGroup(
            Rect groupRect,
            float foldoutGutterWidth,
            float controlWidth,
            float controlHeight,
            SerializedProperty property,
            GUIContent label,
            bool includeChildren) {
            GUI.BeginGroup(groupRect);
            {
                Rect localRect = new Rect(foldoutGutterWidth, 0f, controlWidth, controlHeight);
                EditorGUI.PropertyField(localRect, property, label, includeChildren);
            }
            GUI.EndGroup();
        }

        private static void DrawArrayField(SerializedProperty arrayProperty, GUIContent label) {
            bool isHeaderless = (label == GUIContent.none) || string.IsNullOrEmpty(label.text);

            if (!isHeaderless) {
                bool isExpanded = DrawArrayHeader(arrayProperty, label);
                if (!isExpanded) {
                    return;
                }
            }

            DrawArrayBody(arrayProperty, isHeaderless);
            DrawArrayFooter(arrayProperty);

            EditorGUILayout.Space(EditorUI.ArrayFooterSpace);
        }

        private static bool DrawArrayHeader(SerializedProperty arrayProperty, GUIContent label) {
            Rect headerRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

            float sizeFieldWidth = EditorUI.ArraySizeFieldWidth;
            Rect sizeRect = new Rect(headerRect.xMax - sizeFieldWidth, headerRect.y, sizeFieldWidth, headerRect.height);
            Rect foldoutRect = new Rect(headerRect.x, headerRect.y, headerRect.width - sizeFieldWidth - EditorUI.ArrayHeaderGap, headerRect.height);

            arrayProperty.isExpanded = EditorGUI.Foldout(foldoutRect, arrayProperty.isExpanded, label, true, ArrayFoldout);

            EditorGUI.BeginChangeCheck();
            int newSize = EditorGUI.IntField(sizeRect, arrayProperty.arraySize);
            if (EditorGUI.EndChangeCheck()) {
                arrayProperty.arraySize = Mathf.Max(0, newSize);
            }

            return arrayProperty.isExpanded;
        }

        private static void DrawArrayBody(SerializedProperty arrayProperty, bool isHeaderless) {
            int bodyIndent = isHeaderless ? 0 : 1;

            using (new EditorGUI.IndentLevelScope(bodyIndent)) {
                if (arrayProperty.arraySize == 0) {
                    EditorGUILayout.LabelField("List is Empty", EditorStyles.miniLabel);
                    return;
                }

                for (int elementIndex = 0; elementIndex < arrayProperty.arraySize; elementIndex++) {
                    SerializedProperty elementProperty = arrayProperty.GetArrayElementAtIndex(elementIndex);

                    using (new EditorGUILayout.VerticalScope(ArrayElementBox)) {
                        // Elements draw aligned like normal fields (and nested arrays still work).
                        PropertyFieldClamped(elementProperty, includeChildren: true);
                    }
                }
            }
        }

        private static void DrawArrayFooter(SerializedProperty arrayProperty) {
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("+", EditorStyles.miniButtonLeft, GUILayout.Width(EditorUI.ArrayPlusMinusButtonWidth))) {
                    arrayProperty.arraySize += 1;
                }

                using (new EditorGUI.DisabledScope(arrayProperty.arraySize == 0)) {
                    if (GUILayout.Button("-", EditorStyles.miniButtonRight, GUILayout.Width(EditorUI.ArrayPlusMinusButtonWidth))) {
                        arrayProperty.arraySize = Mathf.Max(0, arrayProperty.arraySize - 1);
                    }
                }
            }
        }
    }
}
#endif
