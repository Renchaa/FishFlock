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
    }
}
#endif
