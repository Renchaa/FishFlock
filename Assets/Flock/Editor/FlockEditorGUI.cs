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
            Rect r = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            r.xMin += 2f;
            EditorGUI.LabelField(r, title, CardHeader);
            EditorGUILayout.Space(2f);
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
