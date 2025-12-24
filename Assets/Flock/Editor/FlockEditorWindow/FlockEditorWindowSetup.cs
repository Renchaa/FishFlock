#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        private void DrawSetupSelector() {
            EditorGUILayout.LabelField("Flock Setup", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _setup = (FlockSetup)EditorGUILayout.ObjectField(
                "Setup Asset",
                _setup,
                typeof(FlockSetup),
                false);
            if (EditorGUI.EndChangeCheck()) {
                _selectedSpeciesIndex = -1;
                _selectedNoiseIndex = -1;
                _selectedTab = 0;

                DestroySpeciesEditor();
                DestroyInteractionMatrixEditor();
                DestroyGroupNoiseEditor();
                DestroyPatternAssetEditor();

                ResetSceneSyncState();
            }

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Create Setup", GUILayout.Width(FlockEditorUI.CreateSetupButtonWidth))) {
                    CreateSetupAsset();
                }
            }
            EditorGUILayout.Space();
        }

        private void CreateSetupAsset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Flock Setup",
                "FlockSetup",
                "asset",
                "Choose a location for the FlockSetup asset");

            if (string.IsNullOrEmpty(path))
                return;

            var asset = ScriptableObject.CreateInstance<FlockSetup>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup = asset;
            _setup.PatternAssets = new List<FlockLayer3PatternProfile>();
            _selectedNoiseIndex = -1;
            EditorUtility.SetDirty(_setup);
            _selectedSpeciesIndex = -1;
            _selectedTab = 0;

            DestroySpeciesEditor();
            DestroyInteractionMatrixEditor();
            DestroyGroupNoiseEditor();
            DestroyPatternAssetEditor();

            EditorGUIUtility.PingObject(asset);
        }
    }
}
#endif
