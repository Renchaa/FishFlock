#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /**
    * <summary>
    * Editor window UI for configuring and inspecting flock systems.
    * This partial renders the Scene / Simulation panel, including scene controller binding,
    * inspector rendering, and editor-time synchronization.
    * </summary>
    */
    public sealed partial class FlockEditorWindow {
        private static bool IsTopLevelInspectableProperty(SerializedProperty property) {
            if (property.depth != 0) {
                return false;
            }

            return property.propertyPath != "m_Script";
        }

        private static bool IsBoundsPropertyPath(string propertyPath) {
            return propertyPath == "boundsType"
                   || propertyPath == "boundsCenter"
                   || propertyPath == "boundsExtents"
                   || propertyPath == "boundsSphereRadius";
        }

        private void DrawSceneSimulationPanel() {
            DrawSceneSimulationHeader();

            EditorGUILayout.Space();

            DrawSceneControllerObjectField();

            DrawFindInSceneButton();

            EditorGUILayout.Space();

            if (TryDrawMissingSceneControllerHelpBox()) {
                return;
            }

            EnsureSceneControllerEditor();

            DrawSceneControllerInspectorScrollView();
        }

        private void OnEditorUpdate() {
            bool shouldRepaint = false;

            // Global sync tick (NOT tab-gated).
            shouldRepaint |= TryRunSceneAutoSyncTick();

            // Active tab update stays as-is.
            IFlockEditorTab activeTab = GetActiveTabOrNull();
            if (activeTab != null) {
                shouldRepaint |= activeTab.OnEditorUpdate(this);
            }

            if (shouldRepaint) {
                Repaint();
            }
        }

        private void TrySyncSpawnerFromController(FlockController controller) {
            if (controller == null) {
                return;
            }

            FlockMainSpawner spawner = controller.MainSpawner;
            if (spawner == null) {
                return;
            }

            FishTypePreset[] fishTypes = controller.FishTypes;
            if (fishTypes == null || fishTypes.Length == 0) {
                return;
            }

            FlockMainSpawnerEditor.SyncTypesFromController(spawner, fishTypes);
        }

        private void RebuildSceneControllerEditor() {
            EnsureEditor(ref sceneControllerEditor, sceneController);
        }

        private void DestroySceneControllerEditor() {
            DestroyEditor(ref sceneControllerEditor);
        }

        private void DrawSceneSimulationHeader() {
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Scene / Simulation", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawSceneControllerObjectField() {
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
        }

        private void DrawFindInSceneButton() {
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Find In Scene", GUILayout.Width(EditorUI.FindInSceneButtonWidth))) {
                    FlockController foundController = FindObjectOfType<FlockController>();

                    if (foundController != null) {
                        sceneController = foundController;

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
        }

        private bool TryDrawMissingSceneControllerHelpBox() {
            if (sceneController != null) {
                return false;
            }

            EditorGUILayout.HelpBox(
                "Assign a FlockController in the current scene.\n" +
                "You can drag it from the Hierarchy or use 'Find In Scene'.",
                MessageType.Info);

            return true;
        }

        private void EnsureSceneControllerEditor() {
            if (sceneControllerEditor == null || sceneControllerEditor.target != sceneController) {
                RebuildSceneControllerEditor();
            }
        }

        private void DrawSceneControllerInspectorScrollView() {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("FlockController Inspector", EditorStyles.miniBoldLabel);

            sceneScroll = EditorGUILayout.BeginScrollView(sceneScroll);

            EditorGUI.BeginChangeCheck();
            DrawSceneControllerInspectorCards(sceneController);
            if (EditorGUI.EndChangeCheck()) {
                // Two-way sync + centralized cross-tab refresh.
                if (TryAutoSyncSetupToController(sceneController)) {
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private bool TryRunSceneAutoSyncTick() {
            if (_setup == null || sceneController == null || EditorApplication.isPlayingOrWillChangePlaymode) {
                return false;
            }

            double currentTimeSeconds = EditorApplication.timeSinceStartup;
            if (currentTimeSeconds < _nextSceneAutoSyncTime) {
                return false;
            }

            _nextSceneAutoSyncTime = currentTimeSeconds + EditorUI.SceneAutoSyncIntervalSeconds;

            return TryAutoSyncSetupToController(sceneController);
        }

        private void DrawSceneControllerInspectorCards(FlockController controller) {
            if (controller == null) {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(controller);
            serializedObject.Update();

            SerializedProperty boundsTypeProperty = serializedObject.FindProperty("boundsType");
            SerializedProperty boundsCenterProperty = serializedObject.FindProperty("boundsCenter");
            SerializedProperty boundsExtentsProperty = serializedObject.FindProperty("boundsExtents");
            SerializedProperty boundsSphereRadiusProperty = serializedObject.FindProperty("boundsSphereRadius");

            bool boundsCardDrawn = false;

            Type rootType = controller.GetType();
            string currentSection = null;
            bool sectionOpen = false;

            FlockEditorGUI.WithLabelWidth(EditorUI.DefaultLabelWidth, () => {
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;

                while (iterator.NextVisible(enterChildren)) {
                    enterChildren = false;

                    if (!IsTopLevelInspectableProperty(iterator)) {
                        continue;
                    }

                    bool isBoundsProperty = IsBoundsPropertyPath(iterator.propertyPath);

                    if (!boundsCardDrawn && isBoundsProperty) {
                        if (sectionOpen) {
                            FlockEditorGUI.EndCard();
                            sectionOpen = false;
                            currentSection = null;
                        }

                        DrawSceneBoundsCard(
                            boundsTypeProperty,
                            boundsCenterProperty,
                            boundsExtentsProperty,
                            boundsSphereRadiusProperty);

                        boundsCardDrawn = true;
                        continue;
                    }

                    if (boundsCardDrawn && isBoundsProperty) {
                        continue;
                    }

                    if (TryGetHeaderForPropertyPath(rootType, iterator.propertyPath, out string header)) {
                        if (!sectionOpen || !string.Equals(currentSection, header, StringComparison.Ordinal)) {
                            if (sectionOpen) {
                                FlockEditorGUI.EndCard();
                            }

                            currentSection = header;
                            FlockEditorGUI.BeginCard(currentSection);
                            sectionOpen = true;
                        }
                    } else if (!sectionOpen) {
                        currentSection = "Settings";
                        FlockEditorGUI.BeginCard(currentSection);
                        sectionOpen = true;
                    }

                    SerializedProperty property = iterator.Copy();

                    GUIContent labelOverride =
                        !string.IsNullOrEmpty(currentSection) &&
                        string.Equals(property.displayName, currentSection, StringComparison.Ordinal)
                            ? GUIContent.none
                            : null;

                    DrawPropertyNoDecorators(property, labelOverride);
                }

                if (sectionOpen) {
                    FlockEditorGUI.EndCard();
                }
            });

            if (serializedObject.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(controller);
            }
        }

        private void DrawSceneBoundsCard(
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
                    FlockBoundsType boundsTypeValue = (FlockBoundsType)boundsType.enumValueIndex;

                    using (new EditorGUI.IndentLevelScope()) {
                        switch (boundsTypeValue) {
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
