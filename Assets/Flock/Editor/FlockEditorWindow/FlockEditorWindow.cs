#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Flock.Editor {
    /// <summary>
    /// Top-level flock editor window.
    /// - Select / create a FlockSetup asset.
    /// - Manage Species (FishTypePreset assets) + Behaviour profiles.
    /// - Interactions matrix.
    /// - Noise / Patterns.
    /// - Scene / Simulation wiring + sync.
    /// </summary>
    public sealed partial class FlockEditorWindow : EditorWindow {
        // ---------------- Serialized window state ----------------
        [SerializeField] private FlockSetup _setup;
        [SerializeField] private int _selectedTab = 0; // 0 = Species, 1 = Interactions, 2 = Noise/Patterns, 3 = Scene/Simulation
        [SerializeField] private FlockController sceneController;
        [SerializeField] private int _selectedSpeciesIndex = -1;
        [SerializeField] private int _speciesInspectorMode = 0;
        [SerializeField] private int _selectedNoiseIndex = -1; // -1 = Group Noise Pattern, >=0 = PatternAssets[i]
        [SerializeField] private int _noiseInspectorMode = 0;  // 0 = Group Noise, 1 = Pattern Assets

        // ---------------- Scroll ----------------
        private Vector2 _speciesListScroll;
        private Vector2 _detailScroll;
        private Vector2 _noiseListScroll;
        private Vector2 _noiseDetailScroll;
        private Vector2 _interactionsScroll;

        // ---------------- Cached inspectors ----------------
        private UnityEditor.Editor _presetEditor;      // FishTypePreset inspector
        private UnityEditor.Editor _behaviourEditor;   // FishBehaviourProfile inspector
        private UnityEditor.Editor _interactionMatrixEditor;
        private UnityEditor.Editor _patternAssetEditor;
        private UnityEditor.Editor groupNoiseEditor;
        private UnityEditor.Editor sceneControllerEditor;

        // ---------------- Scene tab state ----------------
        private Vector2 sceneScroll;
        private bool _isSceneAutoSyncing = false;
        private double _nextSceneAutoSyncTime = 0.0;

        // ---------------- Pattern dropdown ----------------
        private AdvancedDropdownState _createPatternDropdownState;
        private CreatePatternDropdown _createPatternDropdown;

        // ---------------- Scene sync conflict resolution ----------------
        enum SyncSource {
            None,
            Setup,
            Controller
        }

        // Last-synced instanceID snapshots for fish / patterns
        int[] _lastSyncedSetupFishIds;
        int[] _lastSyncedControllerFishIds;
        int[] _lastSyncedSetupPatternIds;
        int[] _lastSyncedControllerPatternIds;

        // Single-ID tracking for InteractionMatrix and GroupNoise
        int _lastSetupMatrixId;
        int _lastControllerMatrixId;
        int _lastSetupNoiseId;
        int _lastControllerNoiseId;

        // Remember last winner to resolve conflicts when both changed
        SyncSource _lastFishSyncSource = SyncSource.None;
        SyncSource _lastPatternSyncSource = SyncSource.None;
        SyncSource _lastMatrixSyncSource = SyncSource.None;
        SyncSource _lastNoiseSyncSource = SyncSource.None;

        [MenuItem("Window/Flock/Flock Editor")]
        public static void Open() {
            GetWindow<FlockEditorWindow>("Flock Editor");
        }

        private void OnEnable() {
            EditorApplication.update += OnEditorUpdate;
            ResetSceneSyncState();
        }

        private void OnDisable() {
            EditorApplication.update -= OnEditorUpdate;

            DestroySpeciesEditor();
            DestroyInteractionMatrixEditor();
            DestroyGroupNoiseEditor();
            DestroyPatternAssetEditor();
            DestroySceneControllerEditor();
        }

        private void OnGUI() {
            DrawSetupSelector();

            if (_setup == null) {
                DrawNoSetupHelp();
                return;
            }

            EditorGUILayout.Space();

            _selectedTab = GUILayout.Toolbar(
                _selectedTab,
                new[] { "Species", "Interactions", "Noise / Patterns", "Scene / Simulation" });

            EditorGUILayout.Space();

            switch (_selectedTab) {
                case 0: // Species
                    using (new EditorGUILayout.HorizontalScope()) {
                        DrawSpeciesListPanel();
                        DrawSpeciesDetailPanel();
                    }
                    break;

                case 1: // Interactions
                    DrawInteractionsPanel();
                    break;

                case 2: // Noise / Patterns
                    DrawNoiseModeToolbar();
                    using (new EditorGUILayout.HorizontalScope()) {
                        DrawNoiseListPanel();
                        DrawNoiseDetailPanel();
                    }
                    HandleGroupNoiseObjectPicker();
                    break;

                case 3: // Scene / Simulation
                    DrawSceneSimulationPanel();
                    break;
            }
        }

        private static void DrawNoSetupHelp() {
            EditorGUILayout.HelpBox(
                "Assign or create a FlockSetup asset.\n\n" +
                "This asset is the central config that holds your species, " +
                "interaction matrix, and noise/pattern assets.",
                MessageType.Info);
        }
    }
}
#endif
