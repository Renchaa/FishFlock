#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FlockController))]
    public sealed class FlockControllerEditor : UnityEditor.Editor {

        enum Section {
            FishTypes,
            Spawning,
            GroupNoise,
            Bounds,
            Grid,
            Movement,
            Obstacles,
            Attractors,
            Layer3Patterns,
            Logging,
            Debug,
            ControllerSettings
        }

        SerializedProperty fishTypesProp;

        void OnEnable() {
            fishTypesProp = serializedObject.FindProperty("fishTypes");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Fish types (now editable)
            DrawFishTypesCard();

            // Draw everything else in normal inspector order (no special read-only)
            var it = serializedObject.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren)) {
                enterChildren = false;

                // Skip script + fishTypes (script already has built-in header)
                if (it.name == "m_Script" || it.name == "fishTypes") {
                    continue;
                }

                // All properties, including interactionMatrix and layer3Patterns,
                // are now fully editable.
                FlockEditorGUI.PropertyFieldClamped(it, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        void DrawFishTypesCard() {
            if (fishTypesProp != null) {
                // NO DisabledScope -> editable array
                FlockEditorGUI.PropertyFieldClamped(fishTypesProp, true);
            } else {
                EditorGUILayout.HelpBox(
                    "fishTypes field not found on FlockController. Check serialization field name.",
                    MessageType.Warning);
            }
        }
    }
}
#endif
