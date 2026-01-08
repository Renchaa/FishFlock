#if UNITY_EDITOR
using Flock.Scripts.Build.Agents.Fish.Profiles;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace Flock.Scripts.Editor.Window
{
    /**
     * <summary>
     * Interactions tab UI and interaction-matrix asset wiring for the flock editor window.
     * </summary>
     */
    public sealed partial class FlockEditorWindow
    {
        private static void DrawInteractionsPanelHeader()
        {
            EditorGUILayout.LabelField("Interactions / Relationships", EditorStyles.boldLabel);
        }

        private static void DrawMissingSetupHelpBox()
        {
            EditorGUILayout.HelpBox("Assign a FlockSetup asset before editing interactions.", MessageType.Warning);
        }

        private static void DrawMissingInteractionMatrixHelpBox()
        {
            EditorGUILayout.HelpBox(
                "Assign or create a FishInteractionMatrix asset.\n" +
                "Its custom inspector handles fish types, interaction grid, relationships and weights.",
                MessageType.Info);
        }

        private static bool TryGetFishTypesProperty(
            FishInteractionMatrix matrix,
            out SerializedObject serializedObject,
            out SerializedProperty fishTypesProperty)
        {
            serializedObject = new SerializedObject(matrix);
            fishTypesProperty = serializedObject.FindProperty("fishTypes");
            return fishTypesProperty != null;
        }

        private static bool AreFishTypesInSync(
            SerializedProperty fishTypesProperty,
            IReadOnlyList<FishTypePreset> fishTypes,
            int desiredFishTypeCount)
        {
            if (fishTypesProperty.arraySize != desiredFishTypeCount) { return false; }
            if (desiredFishTypeCount == 0) { return true; }

            for (int index = 0; index < desiredFishTypeCount; index += 1)
            {
                Object currentReference = fishTypesProperty.GetArrayElementAtIndex(index).objectReferenceValue;
                Object desiredReference = index < fishTypes.Count ? fishTypes[index] : null;
                if (currentReference != desiredReference) { return false; }
            }

            return true;
        }

        private static void ApplyFishTypesToMatrix(
            SerializedObject serializedObject,
            SerializedProperty fishTypesProperty,
            IReadOnlyList<FishTypePreset> fishTypes,
            int desiredFishTypeCount,
            FishInteractionMatrix matrix)
        {
            SetFishTypeReferences(fishTypesProperty, fishTypes, desiredFishTypeCount);
            serializedObject.ApplyModifiedProperties();
            matrix.SyncSizeWithFishTypes();
            EditorUtility.SetDirty(matrix);
        }

        private static void SetFishTypeReferences(
            SerializedProperty fishTypesProperty,
            IReadOnlyList<FishTypePreset> fishTypes,
            int desiredFishTypeCount)
        {
            int fishTypeCount = fishTypes?.Count ?? 0;
            fishTypesProperty.arraySize = desiredFishTypeCount;

            for (int index = 0; index < desiredFishTypeCount; index += 1)
            {
                FishTypePreset preset = index < fishTypeCount ? fishTypes[index] : null;
                fishTypesProperty.GetArrayElementAtIndex(index).objectReferenceValue = preset;
            }
        }

        private static bool TryGetInteractionMatrixAssetPath(out string path)
        {
            path = EditorUtility.SaveFilePanelInProject(
                "Create Fish Interaction Matrix",
                "FishInteractionMatrix",
                "asset",
                "Choose a location for the FishInteractionMatrix asset");

            return !string.IsNullOrEmpty(path);
        }

        private static FishInteractionMatrix CreateInteractionMatrixAssetAtPath(string path)
        {
            FishInteractionMatrix asset = ScriptableObject.CreateInstance<FishInteractionMatrix>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }

        private void DrawInteractionsPanel()
        {
            DrawInteractionsPanelHeader();
            if (_setup == null) { DrawMissingSetupHelpBox(); return; }

            if (TryDrawInteractionMatrixField(out FishInteractionMatrix selectedMatrix)) { ApplyInteractionMatrixSelection(selectedMatrix); }
            DrawCreateMatrixAssetButton();

            if (_setup.InteractionMatrix == null) { DrawMissingInteractionMatrixHelpBox(); return; }

            SyncMatrixFishTypesFromSetup();
            EnsureInteractionMatrixEditorIsCurrent();
            DrawInteractionMatrixInspector();
        }

        private bool TryDrawInteractionMatrixField(out FishInteractionMatrix selectedMatrix)
        {
            FishInteractionMatrix currentMatrix = _setup != null ? _setup.InteractionMatrix : null;

            EditorGUI.BeginChangeCheck();
            selectedMatrix = (FishInteractionMatrix)EditorGUILayout.ObjectField(
                "Interaction Matrix",
                currentMatrix,
                typeof(FishInteractionMatrix),
                false);

            return _setup != null && EditorGUI.EndChangeCheck();
        }

        private void ApplyInteractionMatrixSelection(FishInteractionMatrix matrix)
        {
            _setup.InteractionMatrix = matrix;
            EditorUtility.SetDirty(_setup);
            RebuildInteractionMatrixEditor();
        }

        private void DrawCreateMatrixAssetButton()
        {
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(_setup == null || _setup.InteractionMatrix != null))
            {
                if (GUILayout.Button("Create Matrix Asset", GUILayout.Width(EditorUI.CreateMatrixAssetButtonWidth))) { CreateInteractionMatrixAsset(); }
            }
        }

        private void EnsureInteractionMatrixEditorIsCurrent()
        {
            if (_interactionMatrixEditor == null || _interactionMatrixEditor.target != _setup.InteractionMatrix) { RebuildInteractionMatrixEditor(); }
        }

        private void DrawInteractionMatrixInspector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Interaction Matrix Editor", EditorStyles.boldLabel);
                EditorGUILayout.Space(EditorUI.SpaceSmall);

                _interactionsScroll = EditorGUILayout.BeginScrollView(_interactionsScroll);
                if (_interactionMatrixEditor != null) { _interactionMatrixEditor.OnInspectorGUI(); }
                EditorGUILayout.EndScrollView();
            }
        }

        private void SyncMatrixFishTypesFromSetup()
        {
            if (!TryGetInteractionMatrixAndFishTypes(out FishInteractionMatrix matrix, out IReadOnlyList<FishTypePreset> fishTypes)) { return; }
            if (!TryGetFishTypesProperty(matrix, out SerializedObject serializedObject, out SerializedProperty fishTypesProperty)) { return; }

            int desiredFishTypeCount = fishTypes?.Count ?? 0;
            if (AreFishTypesInSync(fishTypesProperty, fishTypes, desiredFishTypeCount)) { return; }

            ApplyFishTypesToMatrix(serializedObject, fishTypesProperty, fishTypes, desiredFishTypeCount, matrix);
        }

        private bool TryGetInteractionMatrixAndFishTypes(out FishInteractionMatrix matrix, out IReadOnlyList<FishTypePreset> fishTypes)
        {
            matrix = _setup != null ? _setup.InteractionMatrix : null;
            fishTypes = _setup != null ? _setup.FishTypes : null;
            return matrix != null;
        }

        private void CreateInteractionMatrixAsset()
        {
            if (!TryGetInteractionMatrixAssetPath(out string path)) { return; }

            FishInteractionMatrix asset = CreateInteractionMatrixAssetAtPath(path);
            AssignInteractionMatrixAssetToSetup(asset);

            RebuildInteractionMatrixEditor();
            EditorGUIUtility.PingObject(asset);
        }

        private void AssignInteractionMatrixAssetToSetup(FishInteractionMatrix asset)
        {
            if (_setup == null) { return; }
            _setup.InteractionMatrix = asset;
            EditorUtility.SetDirty(_setup);
        }

        private void RebuildInteractionMatrixEditor()
        {
            if (_setup == null) { DestroyEditor(ref _interactionMatrixEditor); return; }
            EnsureEditor(ref _interactionMatrixEditor, _setup.InteractionMatrix);
        }

        private void DestroyInteractionMatrixEditor()
        {
            DestroyEditor(ref _interactionMatrixEditor);
        }
    }
}
#endif
