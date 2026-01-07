#if UNITY_EDITOR
using System.Collections.Generic;
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /**
     * <summary>
     * Editor window for configuring flock setup assets and related editor tooling.
     * </summary>
     */
    public sealed partial class FlockEditorWindow {
        private static void DrawSetupSelectorHeader() {
            EditorGUILayout.LabelField("Flock Setup", EditorStyles.boldLabel);
        }

        private static bool TryGetSetupAssetPath(out string path) {
            path = EditorUtility.SaveFilePanelInProject(
                "Create Flock Setup",
                "FlockSetup",
                "asset",
                "Choose a location for the FlockSetup asset");

            return !string.IsNullOrEmpty(path);
        }

        private static FlockSetup CreateSetupAssetAtPath(string path) {
            FlockSetup asset = ScriptableObject.CreateInstance<FlockSetup>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }

        private void DrawSetupSelector() {
            DrawSetupSelectorHeader();
            TryDrawSetupAssetField();

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Create Setup", GUILayout.Width(EditorUI.CreateSetupButtonWidth))) {
                    CreateSetupAsset();
                }
            }

            EditorGUILayout.Space();
        }

        private void TryDrawSetupAssetField() {
            EditorGUI.BeginChangeCheck();

            _setup = (FlockSetup)EditorGUILayout.ObjectField(
                "Setup Asset",
                _setup,
                typeof(FlockSetup),
                false);

            if (EditorGUI.EndChangeCheck()) {
                ApplySetupSelectionChanged();
            }
        }

        private void ApplySetupSelectionChanged() {
            ResetEditorSelection();
            DestroyAllTabEditors();
            ResetSceneSyncState();
        }

        private void ResetEditorSelection() {
            _selectedSpeciesIndex = -1;
            _selectedNoiseIndex = -1;
            _selectedTab = FlockEditorTabKind.Species;
        }

        private void DestroyAllTabEditors() {
            DestroySpeciesEditor();
            DestroyInteractionMatrixEditor();
            DestroyGroupNoiseEditor();
            DestroyPatternAssetEditor();
        }

        private void CreateSetupAsset() {
            if (!TryGetSetupAssetPath(out string path)) {
                return;
            }

            FlockSetup asset = CreateSetupAssetAtPath(path);
            AssignCreatedSetup(asset);

            DestroyAllTabEditors();
            EditorGUIUtility.PingObject(asset);
        }

        private void AssignCreatedSetup(FlockSetup asset) {
            _setup = asset;
            _setup.PatternAssets = new List<FlockLayer3PatternProfile>();
            EditorUtility.SetDirty(_setup);

            _selectedSpeciesIndex = -1;
            _selectedNoiseIndex = -1;
            _selectedTab = 0;
        }
    }
}
#endif
