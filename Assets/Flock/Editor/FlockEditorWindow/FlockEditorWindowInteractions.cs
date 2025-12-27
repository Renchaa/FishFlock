#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /**
    * <summary>
    * Interactions tab UI and interaction-matrix asset wiring for the flock editor window.
    * </summary>
    */
    public sealed partial class FlockEditorWindow {
        private void DrawInteractionsPanel() {
            DrawInteractionsPanelHeader();

            if (TryDrawInteractionMatrixField(out FishInteractionMatrix selectedMatrix)) {
                ApplyInteractionMatrixSelection(selectedMatrix);
            }

            DrawCreateMatrixAssetButton();

            if (_setup.InteractionMatrix == null) {
                DrawMissingInteractionMatrixHelpBox();
                return;
            }

            SyncMatrixFishTypesFromSetup();
            EnsureInteractionMatrixEditorIsCurrent();
            DrawInteractionMatrixInspector();
        }

        private static void DrawInteractionsPanelHeader() {
            EditorGUILayout.LabelField("Interactions / Relationships", EditorStyles.boldLabel);
        }

        private bool TryDrawInteractionMatrixField(out FishInteractionMatrix selectedMatrix) {
            EditorGUI.BeginChangeCheck();

            selectedMatrix = (FishInteractionMatrix)EditorGUILayout.ObjectField(
                "Interaction Matrix",
                _setup.InteractionMatrix,
                typeof(FishInteractionMatrix),
                false);

            return EditorGUI.EndChangeCheck();
        }

        private void ApplyInteractionMatrixSelection(FishInteractionMatrix matrix) {
            _setup.InteractionMatrix = matrix;
            EditorUtility.SetDirty(_setup);
            RebuildInteractionMatrixEditor();
        }

        private void DrawCreateMatrixAssetButton() {
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(_setup == null || _setup.InteractionMatrix != null)) {
                if (GUILayout.Button("Create Matrix Asset", GUILayout.Width(FlockEditorUI.CreateMatrixAssetButtonWidth))) {
                    CreateInteractionMatrixAsset();
                }
            }
        }

        private static void DrawMissingInteractionMatrixHelpBox() {
            EditorGUILayout.HelpBox(
                "Assign or create a FishInteractionMatrix asset.\n" +
                "Its custom inspector handles fish types, interaction grid, relationships and weights.",
                MessageType.Info);
        }

        private void EnsureInteractionMatrixEditorIsCurrent() {
            if (_interactionMatrixEditor == null || _interactionMatrixEditor.target != _setup.InteractionMatrix) {
                RebuildInteractionMatrixEditor();
            }
        }

        private void DrawInteractionMatrixInspector() {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField("Interaction Matrix Editor", EditorStyles.boldLabel);
                EditorGUILayout.Space(FlockEditorUI.SpaceSmall);

                _interactionsScroll = EditorGUILayout.BeginScrollView(_interactionsScroll);

                if (_interactionMatrixEditor != null) {
                    _interactionMatrixEditor.OnInspectorGUI();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void SyncMatrixFishTypesFromSetup() {
            if (_setup == null || _setup.InteractionMatrix == null) {
                return;
            }

            FishInteractionMatrix matrix = _setup.InteractionMatrix;
            var fishTypeList = _setup.FishTypes;

            int desiredSize = fishTypeList != null ? fishTypeList.Count : 0;

            SerializedObject serializedObject = new SerializedObject(matrix);
            SerializedProperty fishTypesProperty = serializedObject.FindProperty("fishTypes");

            if (fishTypesProperty == null) {
                return;
            }

            if (AreFishTypesInSync(fishTypesProperty, fishTypeList, desiredSize)) {
                return;
            }

            ApplyFishTypesToMatrix(serializedObject, fishTypesProperty, fishTypeList, desiredSize, matrix);
        }

        private static bool AreFishTypesInSync(SerializedProperty fishTypesProperty, object fishTypeList, int desiredSize) {
            int currentSize = fishTypesProperty.arraySize;
            if (currentSize != desiredSize) {
                return false;
            }

            if (currentSize == 0) {
                return true;
            }

            // Keep the exact comparison semantics: element-by-element reference equality with setup ordering.
            for (int index = 0; index < currentSize; index += 1) {
                Object currentReference = fishTypesProperty.GetArrayElementAtIndex(index).objectReferenceValue;

                Object desiredReference = null;
                if (fishTypeList is System.Collections.Generic.IList<FishTypePreset> genericList && index < genericList.Count) {
                    desiredReference = genericList[index];
                }

                if (currentReference != desiredReference) {
                    return false;
                }
            }

            return true;
        }

        private void ApplyFishTypesToMatrix(
            SerializedObject serializedObject,
            SerializedProperty fishTypesProperty,
            object fishTypeList,
            int desiredSize,
            FishInteractionMatrix matrix) {
            fishTypesProperty.arraySize = desiredSize;

            for (int index = 0; index < desiredSize; index += 1) {
                FishTypePreset preset = null;

                if (fishTypeList is System.Collections.Generic.IList<FishTypePreset> genericList && index < genericList.Count) {
                    preset = genericList[index];
                }

                fishTypesProperty.GetArrayElementAtIndex(index).objectReferenceValue = preset;
            }

            serializedObject.ApplyModifiedProperties();

            matrix.SyncSizeWithFishTypes();
            EditorUtility.SetDirty(matrix);
        }

        private void CreateInteractionMatrixAsset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Fish Interaction Matrix",
                "FishInteractionMatrix",
                "asset",
                "Choose a location for the FishInteractionMatrix asset");

            if (string.IsNullOrEmpty(path)) {
                return;
            }

            FishInteractionMatrix asset = ScriptableObject.CreateInstance<FishInteractionMatrix>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.InteractionMatrix = asset;
            EditorUtility.SetDirty(_setup);

            RebuildInteractionMatrixEditor();
            EditorGUIUtility.PingObject(asset);
        }

        private void RebuildInteractionMatrixEditor() {
            if (_setup == null) {
                DestroyEditor(ref _interactionMatrixEditor);
                return;
            }

            EnsureEditor(ref _interactionMatrixEditor, _setup.InteractionMatrix);
        }

        private void DestroyInteractionMatrixEditor() {
            DestroyEditor(ref _interactionMatrixEditor);
        }
    }
}
#endif
