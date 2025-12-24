#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        private void DrawInteractionsPanel() {
            EditorGUILayout.LabelField("Interactions / Relationships", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var matrix = (FishInteractionMatrix)EditorGUILayout.ObjectField(
                "Interaction Matrix",
                _setup.InteractionMatrix,
                typeof(FishInteractionMatrix),
                false);
            if (EditorGUI.EndChangeCheck()) {
                _setup.InteractionMatrix = matrix;
                EditorUtility.SetDirty(_setup);
                RebuildInteractionMatrixEditor();
            }

            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(_setup == null || _setup.InteractionMatrix != null)) {
                    if (GUILayout.Button("Create Matrix Asset", GUILayout.Width(FlockEditorUI.CreateMatrixAssetButtonWidth))) {
                        CreateInteractionMatrixAsset();
                    }
                }
            }

            if (_setup.InteractionMatrix == null) {
                EditorGUILayout.HelpBox(
                    "Assign or create a FishInteractionMatrix asset.\n" +
                    "Its custom inspector handles fish types, interaction grid, relationships and weights.",
                    MessageType.Info);
                return;
            }

            SyncMatrixFishTypesFromSetup();

            if (_interactionMatrixEditor == null ||
                _interactionMatrixEditor.target != _setup.InteractionMatrix) {
                RebuildInteractionMatrixEditor();
            }

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
            if (_setup == null || _setup.InteractionMatrix == null) return;

            FishInteractionMatrix matrix = _setup.InteractionMatrix;
            var list = _setup.FishTypes;

            int desiredSize = (list != null) ? list.Count : 0;

            SerializedObject so = new SerializedObject(matrix);
            SerializedProperty fishTypesProp = so.FindProperty("fishTypes");
            if (fishTypesProp == null) return;

            int currentSize = fishTypesProp.arraySize;
            bool changed = (currentSize != desiredSize);

            if (!changed) {
                for (int i = 0; i < currentSize; i++) {
                    Object currentRef = fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    Object desiredRef = (list != null && i < list.Count) ? list[i] : null;
                    if (currentRef != desiredRef) {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed) return;

            fishTypesProp.arraySize = desiredSize;

            for (int i = 0; i < desiredSize; i++) {
                var preset = (list != null && i < list.Count) ? list[i] : null;
                fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue = preset;
            }

            so.ApplyModifiedProperties();

            matrix.SyncSizeWithFishTypes();
            EditorUtility.SetDirty(matrix);
        }

        private void CreateInteractionMatrixAsset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Fish Interaction Matrix",
                "FishInteractionMatrix",
                "asset",
                "Choose a location for the FishInteractionMatrix asset");

            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<FishInteractionMatrix>();
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
