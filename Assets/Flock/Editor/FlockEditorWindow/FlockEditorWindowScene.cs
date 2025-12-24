#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        private void DrawSceneSimulationPanel() {
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Scene / Simulation", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            sceneController = (FlockController)EditorGUILayout.ObjectField(
                "Scene Controller",
                sceneController,
                typeof(FlockController),
                true);
            if (EditorGUI.EndChangeCheck()) {
                DestroySceneControllerEditor();
                ResetSceneSyncState();

                if (!EditorApplication.isPlayingOrWillChangePlaymode && sceneController != null) {
                    TryAutoSyncSetupToController(sceneController);
                }
            }

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Find In Scene", GUILayout.Width(FlockEditorUI.FindInSceneButtonWidth))) {
                    var found = FindObjectOfType<FlockController>();
                    if (found != null) {
                        sceneController = found;

                        DestroySceneControllerEditor();
                        ResetSceneSyncState();

                        if (!EditorApplication.isPlayingOrWillChangePlaymode) {
                            TryAutoSyncSetupToController(sceneController);
                        }
                    } else {
                        EditorUtility.DisplayDialog(
                            "Flock Controller",
                            "No FlockController was found in the open scene.",
                            "OK");
                    }
                }
            }

            EditorGUILayout.Space();

            if (sceneController == null) {
                EditorGUILayout.HelpBox(
                    "Assign a FlockController in the current scene.\n" +
                    "You can drag it from the Hierarchy or use 'Find In Scene'.",
                    MessageType.Info);
                return;
            }

            if (sceneControllerEditor == null || sceneControllerEditor.target != sceneController) {
                RebuildSceneControllerEditor();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("FlockController Inspector", EditorStyles.miniBoldLabel);

            sceneScroll = EditorGUILayout.BeginScrollView(sceneScroll);

            EditorGUI.BeginChangeCheck();
            DrawSceneControllerInspectorCards(sceneController);
            if (EditorGUI.EndChangeCheck()) {
                // Two-way sync + centralized cross-tab refresh
                if (TryAutoSyncSetupToController(sceneController)) {
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnEditorUpdate() {
            bool repaint = false;

            // 1) GLOBAL sync tick (NOT tab-gated)
            if (_setup != null && sceneController != null && !EditorApplication.isPlayingOrWillChangePlaymode) {
                double now = EditorApplication.timeSinceStartup;
                if (now >= _nextSceneAutoSyncTime) {
                    _nextSceneAutoSyncTime = now + FlockEditorUI.SceneAutoSyncIntervalSeconds;

                    // This now also performs cross-tab UI refresh (see section 2)
                    repaint |= TryAutoSyncSetupToController(sceneController);
                }
            }

            // 2) Active tab update stays as-is
            var active = GetActiveTabOrNull();
            if (active != null) {
                repaint |= active.OnEditorUpdate(this);
            }

            if (repaint) {
                Repaint();
            }
        }


        private void TrySyncSpawnerFromController(FlockController controller) {
            if (controller == null) return;

            var spawner = controller.MainSpawner;
            if (spawner == null) return;

            var types = controller.FishTypes;
            if (types == null || types.Length == 0) return;

            FlockMainSpawnerEditor.SyncTypesFromController(spawner, types);
        }

        private void RebuildSceneControllerEditor() {
            EnsureEditor(ref sceneControllerEditor, sceneController);
        }

        private void DestroySceneControllerEditor() {
            DestroyEditor(ref sceneControllerEditor);
        }

        void DrawSceneControllerInspectorCards(FlockController controller) {
            if (controller == null) return;

            var so = new SerializedObject(controller);
            so.Update();

            SerializedProperty pBoundsType = so.FindProperty("boundsType");
            SerializedProperty pBoundsCenter = so.FindProperty("boundsCenter");
            SerializedProperty pBoundsExtents = so.FindProperty("boundsExtents");
            SerializedProperty pBoundsRadius = so.FindProperty("boundsSphereRadius");
            bool boundsCardDrawn = false;

            Type rootType = controller.GetType();
            string currentSection = null;
            bool sectionOpen = false;

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {
                SerializedProperty it = so.GetIterator();
                bool enterChildren = true;

                while (it.NextVisible(enterChildren)) {
                    enterChildren = false;

                    if (it.depth != 0) continue;
                    if (it.propertyPath == "m_Script") continue;

                    if (!boundsCardDrawn &&
                        (it.propertyPath == "boundsType"
                         || it.propertyPath == "boundsCenter"
                         || it.propertyPath == "boundsExtents"
                         || it.propertyPath == "boundsSphereRadius")) {

                        if (sectionOpen) {
                            FlockEditorGUI.EndCard();
                            sectionOpen = false;
                            currentSection = null;
                        }

                        DrawSceneBoundsCard(
                            pBoundsType,
                            pBoundsCenter,
                            pBoundsExtents,
                            pBoundsRadius);

                        boundsCardDrawn = true;
                        continue;
                    }

                    if (boundsCardDrawn &&
                        (it.propertyPath == "boundsType"
                         || it.propertyPath == "boundsCenter"
                         || it.propertyPath == "boundsExtents"
                         || it.propertyPath == "boundsSphereRadius")) {
                        continue;
                    }

                    if (TryGetHeaderForPropertyPath(rootType, it.propertyPath, out string header)) {
                        if (!sectionOpen || !string.Equals(currentSection, header, StringComparison.Ordinal)) {
                            if (sectionOpen) FlockEditorGUI.EndCard();
                            currentSection = header;
                            FlockEditorGUI.BeginCard(currentSection);
                            sectionOpen = true;
                        }
                    } else if (!sectionOpen) {
                        currentSection = "Settings";
                        FlockEditorGUI.BeginCard(currentSection);
                        sectionOpen = true;
                    }

                    var prop = it.Copy();

                    GUIContent labelOverride =
                        (!string.IsNullOrEmpty(currentSection) &&
                         string.Equals(prop.displayName, currentSection, StringComparison.Ordinal))
                            ? GUIContent.none
                            : null;

                    DrawPropertyNoDecorators(prop, labelOverride);
                }

                if (sectionOpen) {
                    FlockEditorGUI.EndCard();
                }
            });

            if (so.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(controller);
            }
        }

        void DrawSceneBoundsCard(
            SerializedProperty boundsType,
            SerializedProperty boundsCenter,
            SerializedProperty boundsExtents,
            SerializedProperty boundsSphereRadius) {

            if (boundsCenter == null && boundsExtents == null && boundsSphereRadius == null) {
                return;
            }

            FlockEditorGUI.BeginCard("Bounds");
            {
                if (boundsType != null) {
                    FlockEditorGUI.PropertyFieldClamped(boundsType, true);
                }

                if (boundsCenter != null) {
                    FlockEditorGUI.PropertyFieldClamped(boundsCenter, true);
                }

                if (boundsType != null) {
                    var type = (FlockBoundsType)boundsType.enumValueIndex;

                    using (new EditorGUI.IndentLevelScope()) {
                        switch (type) {
                            case FlockBoundsType.Box:
                                if (boundsExtents != null) {
                                    FlockEditorGUI.PropertyFieldClamped(boundsExtents, true);
                                }
                                break;

                            case FlockBoundsType.Sphere:
                                if (boundsSphereRadius != null) {
                                    FlockEditorGUI.PropertyFieldClamped(boundsSphereRadius, true);
                                }
                                break;
                        }
                    }
                } else {
                    if (boundsExtents != null) {
                        FlockEditorGUI.PropertyFieldClamped(boundsExtents, true);
                    }
                    if (boundsSphereRadius != null) {
                        FlockEditorGUI.PropertyFieldClamped(boundsSphereRadius, true);
                    }
                }
            }
            FlockEditorGUI.EndCard();
        }
    }
}
#endif
