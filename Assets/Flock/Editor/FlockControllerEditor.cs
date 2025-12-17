#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FlockController))]
    public sealed class FlockControllerEditor : UnityEditor.Editor {

        SerializedProperty boundsTypeProp;
        SerializedProperty boundsCenterProp;
        SerializedProperty boundsExtentsProp;
        SerializedProperty boundsSphereRadiusProp;
        SerializedProperty fishTypesProp;

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


        void OnEnable() {
            fishTypesProp = serializedObject.FindProperty("fishTypes");

            // ADD:
            boundsTypeProp = serializedObject.FindProperty("boundsType");
            boundsCenterProp = serializedObject.FindProperty("boundsCenter");
            boundsExtentsProp = serializedObject.FindProperty("boundsExtents");
            boundsSphereRadiusProp = serializedObject.FindProperty("boundsSphereRadius");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Fish types (now editable)
            DrawFishTypesCard();

            // ADD: our custom bounds card
            DrawBoundsCard();

            // Draw everything else in normal inspector order (no special read-only)
            var it = serializedObject.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren)) {
                enterChildren = false;

                // Skip script + fishTypes (script already has built-in header)
                if (it.name == "m_Script" || it.name == "fishTypes") {
                    continue;
                }

                // ADD: skip raw bounds fields (we draw them in DrawBoundsCard)
                if (it.name == "boundsType"
                    || it.name == "boundsCenter"
                    || it.name == "boundsExtents"
                    || it.name == "boundsSphereRadius") {
                    continue;
                }

                FlockEditorGUI.PropertyFieldClamped(it, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        void DrawBoundsCard() {
            if (boundsTypeProp == null || boundsCenterProp == null) {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bounds", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            // Bounds type (Box / Sphere)
            FlockEditorGUI.PropertyFieldClamped(boundsTypeProp, true);

            // Always show center
            FlockEditorGUI.PropertyFieldClamped(boundsCenterProp, true);

            // Decide which size field to show
            var type = (FlockBoundsType)boundsTypeProp.enumValueIndex;

            if (type == FlockBoundsType.Box) {
                if (boundsExtentsProp != null) {
                    FlockEditorGUI.PropertyFieldClamped(boundsExtentsProp, true);
                }
            } else if (type == FlockBoundsType.Sphere) {
                if (boundsSphereRadiusProp != null) {
                    FlockEditorGUI.PropertyFieldClamped(boundsSphereRadiusProp, true);
                }
            }

            EditorGUI.indentLevel--;
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
