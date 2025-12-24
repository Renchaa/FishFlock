#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Flock.Editor {
    /// <summary>
    /// Phase 1: Top-level flock editor window.
    /// - Select / create a FlockSetup asset.
    /// - Manage the Species list (FishBehaviourProfile assets).
    /// - Edit a selected FishBehaviourProfile using its normal inspector.
    /// </summary>
    public sealed class FlockEditorWindow : EditorWindow {
        [SerializeField] private FlockSetup _setup;
        [SerializeField] private int _selectedTab = 0; // 0 = Species, 1 = Interactions, 2 = Noise/Patterns
        [SerializeField] private FlockController sceneController;
        [SerializeField] private int _selectedSpeciesIndex = -1;
        [SerializeField] private int _speciesInspectorMode = 0;
        [SerializeField] private int _selectedNoiseIndex = -1; // -1 = Group Noise Pattern, >=0 = PatternAssets[i]
        [SerializeField] private int _noiseInspectorMode = 0; // 0 = Group Noise, 1 = Pattern Assets

        private Vector2 _speciesListScroll;
        private Vector2 _detailScroll;
        private Vector2 _noiseListScroll;
        private Vector2 _noiseDetailScroll;
        private Vector2 _interactionsScroll;

        private UnityEditor.Editor _presetEditor;      // FishTypePreset inspector
        private UnityEditor.Editor _behaviourEditor;   // FishBehaviourProfile inspector
        private UnityEditor.Editor _interactionMatrixEditor;
        private UnityEditor.Editor _patternAssetEditor;
        UnityEditor.Editor groupNoiseEditor;
        private bool _isSceneAutoSyncing = false;
        private double _nextSceneAutoSyncTime = 0.0;

        private UnityEditor.Editor sceneControllerEditor;
        private Vector2 sceneScroll;
        private AdvancedDropdownState _createPatternDropdownState;
        private CreatePatternDropdown _createPatternDropdown;

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
                case 0: // Species (Phase 1 behaviour)
                    using (new EditorGUILayout.HorizontalScope()) {
                        DrawSpeciesListPanel();
                        DrawSpeciesDetailPanel();
                    }
                    break;

                case 1: // Interactions
                    DrawInteractionsPanel();
                    break;

                case 2: // Noise / Patterns (Phase 3)
                    DrawNoiseModeToolbar(); // NEW: top-right switch like Species
                    using (new EditorGUILayout.HorizontalScope()) {
                        DrawNoiseListPanel();
                        DrawNoiseDetailPanel();
                    }
                    HandleGroupNoiseObjectPicker(); // NEW: apply "Add Existing" selection
                    break;

                case 3: // Scene / Simulation (Phase 4 wiring)
                    DrawSceneSimulationPanel();
                    break;
            }
        }

        void DrawNoiseModeToolbar() {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Noise / Patterns", EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            _noiseInspectorMode = GUILayout.Toolbar(
                Mathf.Clamp(_noiseInspectorMode, 0, 1),
                new[] { "Group Noise", "Pattern Assets" },
                GUILayout.Width(FlockEditorUI.NoiseModeToolbarWidth));
            if (EditorGUI.EndChangeCheck()) {
                // Rebuild only what's relevant for the active sub-tab
                RebuildNoiseEditors();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(FlockEditorUI.SpaceMedium);
        }


        private void DrawSceneSimulationPanel() {
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Scene / Simulation", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space();

            // Scene controller selection
            EditorGUI.BeginChangeCheck();
            sceneController = (FlockController)EditorGUILayout.ObjectField(
                "Scene Controller",
                sceneController,
                typeof(FlockController),
                true);
            if (EditorGUI.EndChangeCheck()) {
                // Invalidate cached editor; it will rebuild below when needed.
                DestroySceneControllerEditor();

                // ADD:
                ResetSceneSyncState();

                // One-shot sync now; continuous sync remains in OnEditorUpdate (throttled).
                if (!EditorApplication.isPlayingOrWillChangePlaymode && sceneController != null) {
                    TryAutoSyncSetupToController(sceneController);
                }
            }

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Find In Scene", GUILayout.Width(FlockEditorUI.FindInSceneButtonWidth))) {
                    var found = FindObjectOfType<FlockController>();
                    if (found != null) {
                        sceneController = found;

                        // Invalidate cached editor; it will rebuild below when needed.
                        DestroySceneControllerEditor();

                        // ADD:
                        ResetSceneSyncState();

                        // One-shot sync now; continuous sync remains in OnEditorUpdate (throttled).
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
                TryPullControllerRefsIntoSetup(sceneController);
            }

            EditorGUILayout.EndScrollView();

        }

        private void OnEditorUpdate() {
            if (_setup == null || sceneController == null) {
                return;
            }

            // ADD ↓↓↓
            if (_selectedTab != 3) {   // only auto-sync on Scene / Simulation tab
                return;
            }
            // ADD ↑↑↑

            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                return;
            }

            // throttle to avoid hammering serialization every frame
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextSceneAutoSyncTime) {
                return;
            }
            _nextSceneAutoSyncTime = now + FlockEditorUI.SceneAutoSyncIntervalSeconds;

            if (TryAutoSyncSetupToController(sceneController)) {
                Repaint();
            }
        }

        void ResetSceneSyncState() {
            _lastSyncedSetupFishIds = null;
            _lastSyncedControllerFishIds = null;
            _lastSyncedSetupPatternIds = null;
            _lastSyncedControllerPatternIds = null;

            _lastSetupMatrixId = 0;
            _lastControllerMatrixId = 0;
            _lastSetupNoiseId = 0;
            _lastControllerNoiseId = 0;

            _lastFishSyncSource = SyncSource.None;
            _lastPatternSyncSource = SyncSource.None;
            _lastMatrixSyncSource = SyncSource.None;
            _lastNoiseSyncSource = SyncSource.None;
        }


        static bool IntArraysEqual(int[] a, int[] b) {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        static int[] BuildInstanceIdArray<T>(List<T> list) where T : UnityEngine.Object {
            if (list == null || list.Count == 0) {
                return Array.Empty<int>();
            }

            var result = new int[list.Count];
            for (int i = 0; i < list.Count; i++) {
                var obj = list[i];
                result[i] = obj != null ? obj.GetInstanceID() : 0;
            }
            return result;
        }

        static int[] BuildInstanceIdArray<T>(T[] array) where T : UnityEngine.Object {
            if (array == null || array.Length == 0) {
                return Array.Empty<int>();
            }

            var result = new int[array.Length];
            for (int i = 0; i < array.Length; i++) {
                var obj = array[i];
                result[i] = obj != null ? obj.GetInstanceID() : 0;
            }
            return result;
        }

        static SyncSource DetermineWinner(
            bool setupChanged,
            bool controllerChanged,
            SyncSource lastSource) {

            if (!setupChanged && !controllerChanged) {
                return SyncSource.None;
            }

            if (setupChanged && !controllerChanged) {
                return SyncSource.Setup;
            }

            if (!setupChanged && controllerChanged) {
                return SyncSource.Controller;
            }

            // Both changed since last sync → prefer previous winner; default Setup
            if (lastSource == SyncSource.Controller) {
                return SyncSource.Controller;
            }

            return SyncSource.Setup;
        }

        private bool TryAutoSyncSetupToController(FlockController controller) {
            if (_isSceneAutoSyncing || controller == null || _setup == null) {
                return false;
            }

            _isSceneAutoSyncing = true;
            try {
                // Ensure lists exist
                _setup.FishTypes ??= new List<FishTypePreset>();
                _setup.PatternAssets ??= new List<FlockLayer3PatternProfile>();

                // ----- SNAPSHOT CURRENT STATE (Setup) -----
                int[] setupFishIds = BuildInstanceIdArray(_setup.FishTypes);
                int[] setupPatternIds = BuildInstanceIdArray(_setup.PatternAssets);
                int setupMatrixId = _setup.InteractionMatrix != null ? _setup.InteractionMatrix.GetInstanceID() : 0;
                int setupNoiseId = _setup.GroupNoiseSettings != null ? _setup.GroupNoiseSettings.GetInstanceID() : 0;

                // ----- SNAPSHOT CURRENT STATE (Controller) -----
                int[] controllerFishIds = BuildInstanceIdArray(controller.FishTypes);
                int[] controllerPatternIds = BuildInstanceIdArray(controller.Layer3Patterns);

                SerializedObject so = new SerializedObject(controller);
                SerializedProperty interactionProp = so.FindProperty("interactionMatrix");
                SerializedProperty groupNoiseProp = so.FindProperty("groupNoisePattern");

                UnityEngine.Object ctrlMatrixObj = interactionProp != null
                    ? interactionProp.objectReferenceValue
                    : null;
                UnityEngine.Object ctrlNoiseObj = groupNoiseProp != null
                    ? groupNoiseProp.objectReferenceValue
                    : null;

                int controllerMatrixId = ctrlMatrixObj != null ? ctrlMatrixObj.GetInstanceID() : 0;
                int controllerNoiseId = ctrlNoiseObj != null ? ctrlNoiseObj.GetInstanceID() : 0;

                // ----- CHANGE DETECTION -----
                bool setupFishChanged = !IntArraysEqual(setupFishIds, _lastSyncedSetupFishIds);
                bool controllerFishChanged = !IntArraysEqual(controllerFishIds, _lastSyncedControllerFishIds);

                bool setupPatternChanged = !IntArraysEqual(setupPatternIds, _lastSyncedSetupPatternIds);
                bool controllerPatternChanged = !IntArraysEqual(controllerPatternIds, _lastSyncedControllerPatternIds);

                bool setupMatrixChanged = setupMatrixId != _lastSetupMatrixId;
                bool controllerMatrixChanged = controllerMatrixId != _lastControllerMatrixId;

                bool setupNoiseChanged = setupNoiseId != _lastSetupNoiseId;
                bool controllerNoiseChanged = controllerNoiseId != _lastControllerNoiseId;

                // ----- WHO WINS PER TRACK? -----
                SyncSource fishWinner = DetermineWinner(setupFishChanged, controllerFishChanged, _lastFishSyncSource);
                SyncSource patternWinner = DetermineWinner(setupPatternChanged, controllerPatternChanged, _lastPatternSyncSource);
                SyncSource matrixWinner = DetermineWinner(setupMatrixChanged, controllerMatrixChanged, _lastMatrixSyncSource);
                SyncSource noiseWinner = DetermineWinner(setupNoiseChanged, controllerNoiseChanged, _lastNoiseSyncSource);

                bool anyChange = false;
                bool controllerDirty = false;

                // ----- FISH TYPES SYNC (two-way) -----
                if (fishWinner == SyncSource.Setup) {
                    SerializedProperty fishTypesProp = so.FindProperty("fishTypes");
                    if (fishTypesProp != null) {
                        fishTypesProp.arraySize = _setup.FishTypes.Count;
                        for (int i = 0; i < _setup.FishTypes.Count; i++) {
                            fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue = _setup.FishTypes[i];
                        }
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (fishWinner == SyncSource.Controller) {
                    Undo.RecordObject(_setup, "Sync Setup Fish Types From Controller");
                    _setup.FishTypes.Clear();
                    var types = controller.FishTypes;
                    if (types != null && types.Length > 0) {
                        _setup.FishTypes.AddRange(types);
                    }
                    EditorUtility.SetDirty(_setup);
                    anyChange = true;

                    // Refresh Setup snapshot
                    setupFishIds = BuildInstanceIdArray(_setup.FishTypes);
                }

                // ----- LAYER-3 PATTERNS SYNC (two-way) -----
                if (patternWinner == SyncSource.Setup) {
                    SerializedProperty layer3Prop = so.FindProperty("layer3Patterns");
                    if (layer3Prop != null) {
                        layer3Prop.arraySize = _setup.PatternAssets.Count;
                        for (int i = 0; i < _setup.PatternAssets.Count; i++) {
                            layer3Prop.GetArrayElementAtIndex(i).objectReferenceValue = _setup.PatternAssets[i];
                        }
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (patternWinner == SyncSource.Controller) {
                    Undo.RecordObject(_setup, "Sync Setup Patterns From Controller");
                    _setup.PatternAssets.Clear();
                    var patterns = controller.Layer3Patterns;
                    if (patterns != null && patterns.Length > 0) {
                        _setup.PatternAssets.AddRange(patterns);
                    }
                    EditorUtility.SetDirty(_setup);
                    anyChange = true;

                    // Refresh Setup snapshot
                    setupPatternIds = BuildInstanceIdArray(_setup.PatternAssets);
                }

                // ----- INTERACTION MATRIX SYNC (two-way) -----
                if (matrixWinner == SyncSource.Setup) {
                    if (interactionProp != null) {
                        interactionProp.objectReferenceValue = _setup.InteractionMatrix;
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (matrixWinner == SyncSource.Controller) {
                    if (interactionProp != null) {
                        Undo.RecordObject(_setup, "Sync Setup Interaction Matrix From Controller");
                        var matrixFromCtrl = interactionProp.objectReferenceValue as FishInteractionMatrix;
                        _setup.InteractionMatrix = matrixFromCtrl;
                        EditorUtility.SetDirty(_setup);
                        anyChange = true;

                        setupMatrixId = matrixFromCtrl != null ? matrixFromCtrl.GetInstanceID() : 0;
                    }
                }

                // ----- GROUP NOISE PATTERN SYNC (two-way) -----
                if (noiseWinner == SyncSource.Setup) {
                    if (groupNoiseProp != null) {
                        groupNoiseProp.objectReferenceValue = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (noiseWinner == SyncSource.Controller) {
                    if (groupNoiseProp != null) {
                        Undo.RecordObject(_setup, "Sync Setup Group Noise From Controller");
                        var noiseFromCtrl = groupNoiseProp.objectReferenceValue as GroupNoisePatternProfile;
                        _setup.GroupNoiseSettings = noiseFromCtrl;
                        EditorUtility.SetDirty(_setup);
                        anyChange = true;

                        setupNoiseId = noiseFromCtrl != null ? noiseFromCtrl.GetInstanceID() : 0;
                    }
                }

                // ----- APPLY CONTROLLER CHANGES -----
                if (controllerDirty) {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(controller);

                    // keep spawner in sync with controller fish types
                    TrySyncSpawnerFromController(controller);

                    // Refresh controller snapshots
                    controllerFishIds = BuildInstanceIdArray(controller.FishTypes);
                    controllerPatternIds = BuildInstanceIdArray(controller.Layer3Patterns);

                    interactionProp = so.FindProperty("interactionMatrix");
                    groupNoiseProp = so.FindProperty("groupNoisePattern");

                    ctrlMatrixObj = interactionProp != null ? interactionProp.objectReferenceValue : null;
                    ctrlNoiseObj = groupNoiseProp != null ? groupNoiseProp.objectReferenceValue : null;
                    controllerMatrixId = ctrlMatrixObj != null ? ctrlMatrixObj.GetInstanceID() : 0;
                    controllerNoiseId = ctrlNoiseObj != null ? ctrlNoiseObj.GetInstanceID() : 0;
                }

                // ----- UPDATE BASELINES -----

                // Fish
                if (fishWinner != SyncSource.None) {
                    _lastSyncedSetupFishIds = setupFishIds;
                    _lastSyncedControllerFishIds = controllerFishIds;
                    _lastFishSyncSource = fishWinner;
                } else if (_lastSyncedSetupFishIds == null) {
                    _lastSyncedSetupFishIds = setupFishIds;
                    _lastSyncedControllerFishIds = controllerFishIds;
                    _lastFishSyncSource = SyncSource.Setup;
                }

                // Patterns
                if (patternWinner != SyncSource.None) {
                    _lastSyncedSetupPatternIds = setupPatternIds;
                    _lastSyncedControllerPatternIds = controllerPatternIds;
                    _lastPatternSyncSource = patternWinner;
                } else if (_lastSyncedSetupPatternIds == null) {
                    _lastSyncedSetupPatternIds = setupPatternIds;
                    _lastSyncedControllerPatternIds = controllerPatternIds;
                    _lastPatternSyncSource = SyncSource.Setup;
                }

                // Matrix
                if (matrixWinner != SyncSource.None) {
                    _lastSetupMatrixId = setupMatrixId;
                    _lastControllerMatrixId = controllerMatrixId;
                    _lastMatrixSyncSource = matrixWinner;
                } else if (_lastMatrixSyncSource == SyncSource.None) {
                    _lastSetupMatrixId = setupMatrixId;
                    _lastControllerMatrixId = controllerMatrixId;
                    _lastMatrixSyncSource = SyncSource.Setup;
                }

                // Noise
                if (noiseWinner != SyncSource.None) {
                    _lastSetupNoiseId = setupNoiseId;
                    _lastControllerNoiseId = controllerNoiseId;
                    _lastNoiseSyncSource = noiseWinner;
                } else if (_lastNoiseSyncSource == SyncSource.None) {
                    _lastSetupNoiseId = setupNoiseId;
                    _lastControllerNoiseId = controllerNoiseId;
                    _lastNoiseSyncSource = SyncSource.Setup;
                }

                return anyChange;
            } finally {
                _isSceneAutoSyncing = false;
            }
        }

        private bool TryPullControllerRefsIntoSetup(FlockController controller) {
            if (controller == null || _setup == null) {
                return false;
            }

            // Only pull the references you actually want bi-directional.
            SerializedObject so = new SerializedObject(controller);

            bool changed = false;

            // --- Fish Types (controller -> setup) ---
            SerializedProperty ctrlFishTypes = so.FindProperty("fishTypes");
            if (ctrlFishTypes != null) {
                int count = ctrlFishTypes.arraySize;

                // Compare to current setup list
                if (_setup.FishTypes == null) {
                    _setup.FishTypes = new List<FishTypePreset>(count);
                }

                bool mismatch = (_setup.FishTypes.Count != count);
                if (!mismatch) {
                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FishTypePreset)ctrlFishTypes
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        if (_setup.FishTypes[i] != fromCtrl) {
                            mismatch = true;
                            break;
                        }
                    }
                }

                if (mismatch) {
                    Undo.RecordObject(_setup, "Sync Setup Fish Types From Controller");

                    _setup.FishTypes.Clear();
                    _setup.FishTypes.Capacity = Mathf.Max(_setup.FishTypes.Capacity, count);

                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FishTypePreset)ctrlFishTypes
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        _setup.FishTypes.Add(fromCtrl);
                    }

                    changed = true;
                    // Rebuild species editors so the list panel + inspector stay in lockstep
                    DestroySpeciesEditor();
                    RebuildSpeciesEditor();
                }
            }

            // --- Interaction Matrix (controller -> setup) ---
            var ctrlMatrix = so.FindProperty("interactionMatrix")?.objectReferenceValue as FishInteractionMatrix;
            if (ctrlMatrix != null && _setup.InteractionMatrix != ctrlMatrix) {
                Undo.RecordObject(_setup, "Sync Setup Interaction Matrix");
                _setup.InteractionMatrix = ctrlMatrix;
                changed = true;
            }

            // --- Group Noise (controller -> setup) ---
            var ctrlNoise = so.FindProperty("groupNoisePattern")?.objectReferenceValue as GroupNoisePatternProfile;
            if (ctrlNoise != null && _setup.GroupNoiseSettings != ctrlNoise) {
                Undo.RecordObject(_setup, "Sync Setup Group Noise");
                _setup.GroupNoiseSettings = ctrlNoise;
                changed = true;
            }

            // --- Layer-3 Patterns (controller -> setup) ---
            SerializedProperty ctrlLayer3 = so.FindProperty("layer3Patterns");
            if (ctrlLayer3 != null) {
                int count = ctrlLayer3.arraySize;

                if (_setup.PatternAssets == null) {
                    _setup.PatternAssets = new List<FlockLayer3PatternProfile>(count);
                }

                bool mismatch = (_setup.PatternAssets.Count != count);
                if (!mismatch) {
                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FlockLayer3PatternProfile)ctrlLayer3
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        if (_setup.PatternAssets[i] != fromCtrl) {
                            mismatch = true;
                            break;
                        }
                    }
                }

                if (mismatch) {
                    Undo.RecordObject(_setup, "Sync Setup Patterns From Controller");

                    _setup.PatternAssets.Clear();
                    _setup.PatternAssets.Capacity = Mathf.Max(_setup.PatternAssets.Capacity, count);

                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FlockLayer3PatternProfile)ctrlLayer3
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        _setup.PatternAssets.Add(fromCtrl);
                    }

                    changed = true;
                    RebuildNoiseEditors();
                }
            }

            if (changed) {
                EditorUtility.SetDirty(_setup);
                // matrix / noise editors already rebuilt above when needed
            }

            return changed;
        }

        private static bool FishTypesMatch(SerializedProperty fishTypesProp, List<FishTypePreset> desired, int desiredCount) {
            if (fishTypesProp.arraySize != desiredCount) {
                return false;
            }

            for (int i = 0; i < desiredCount; i++) {
                var a = fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue;
                var b = desired[i];
                if (a != b) {
                    return false;
                }
            }

            return true;
        }

        private bool TrySyncSpawnerFromController(FlockController controller) {
            if (controller == null) {
                return false;
            }

            var spawner = controller.MainSpawner;
            if (spawner == null) {
                return false;
            }

            var types = controller.FishTypes;
            if (types == null || types.Length == 0) {
                return false;
            }

            // EDITOR-ONLY sync (moved out of runtime MonoBehaviour)
            FlockMainSpawnerEditor.SyncTypesFromController(spawner, types);

            return true;
        }

        private void RebuildSceneControllerEditor() {
            EnsureEditor(ref sceneControllerEditor, sceneController);
        }

        private void DestroySceneControllerEditor() {
            DestroyEditor(ref sceneControllerEditor);
        }

        // FlockEditorWindow.cs
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

            // Always ensure the matrix uses the same FishTypePreset list as the setup
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
            if (_setup == null || _setup.InteractionMatrix == null) {
                return;
            }

            FishInteractionMatrix matrix = _setup.InteractionMatrix;
            var list = _setup.FishTypes;

            // Safety: treat null as empty list
            int desiredSize = (list != null) ? list.Count : 0;

            SerializedObject so = new SerializedObject(matrix);
            SerializedProperty fishTypesProp = so.FindProperty("fishTypes");

            if (fishTypesProp == null) {
                return;
            }

            int currentSize = fishTypesProp.arraySize;

            bool changed = false;

            // 1) Size mismatch → definitely changed
            if (currentSize != desiredSize) {
                changed = true;
            } else {
                // 2) Same size → check each element for mismatch
                for (int i = 0; i < currentSize; i++) {
                    UnityEngine.Object currentRef = fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    UnityEngine.Object desiredRef = (list != null && i < list.Count) ? list[i] : null;

                    if (currentRef != desiredRef) {
                        changed = true;
                        break;
                    }
                }
            }

            // Nothing changed → bail out, avoid dirty flags / unnecessary work
            if (!changed) {
                return;
            }

            // Actually rewrite the array
            fishTypesProp.arraySize = desiredSize;

            for (int i = 0; i < desiredSize; i++) {
                FishTypePreset preset = (list != null && i < list.Count) ? list[i] : null;
                fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue = preset;
            }

            so.ApplyModifiedProperties();

            // Resize internal matrices / weights to match new fishTypes
            matrix.SyncSizeWithFishTypes();
            EditorUtility.SetDirty(matrix);
        }

        // --------------------------------------------------------------------
        // Top bar: select / create FlockSetup asset
        // --------------------------------------------------------------------

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
                _selectedNoiseIndex = -1; // reset noise selection
                _selectedTab = 0;
                DestroySpeciesEditor();
                DestroyInteractionMatrixEditor();
                DestroyGroupNoiseEditor();
                DestroyPatternAssetEditor(); // also clear pattern inspector

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

        private static void DrawNoSetupHelp() {
            EditorGUILayout.HelpBox(
                "Assign or create a FlockSetup asset.\n\n" +
                "This asset is the central config that holds your species, " +
                "interaction matrix, and noise/pattern assets.",
                MessageType.Info);
        }

        // --------------------------------------------------------------------
        // Left: species list
        // --------------------------------------------------------------------

        private void DrawSpeciesListPanel() {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(FlockEditorUI.SpeciesListPanelWidth))) {
                EditorGUILayout.LabelField("Fish Types (Presets)", EditorStyles.boldLabel);

                if (_setup.FishTypes == null) {
                    _setup.FishTypes = new List<FishTypePreset>();
                    EditorUtility.SetDirty(_setup);
                }

                _speciesListScroll = EditorGUILayout.BeginScrollView(_speciesListScroll);

                var types = _setup.FishTypes;
                for (int i = 0; i < types.Count; i++) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        var preset = types[i];

                        // Select button
                        GUIStyle buttonStyle = (i == _selectedSpeciesIndex)
                            ? EditorStyles.miniButtonMid
                            : EditorStyles.miniButton;

                        string label;
                        if (preset == null) {
                            label = "<Empty Slot>";
                        } else if (!string.IsNullOrEmpty(preset.DisplayName)) {
                            label = preset.DisplayName;
                        } else {
                            label = preset.name;
                        }

                        if (GUILayout.Button(label, buttonStyle)) {
                            if (_selectedSpeciesIndex != i) {
                                _selectedSpeciesIndex = i;
                                RebuildSpeciesEditor();
                            }
                        }

                        // Inline object field (FishTypePreset, not behaviour profile)
                        types[i] = (FishTypePreset)EditorGUILayout.ObjectField(
                            GUIContent.none,
                            preset,
                            typeof(FishTypePreset),
                            false,
                            GUILayout.Width(FlockEditorUI.SpeciesInlineObjectFieldWidth));

                        // Remove button
                        if (GUILayout.Button("X", GUILayout.Width(FlockEditorUI.RemoveRowButtonWidth))) {
                            types.RemoveAt(i);
                            EditorUtility.SetDirty(_setup);

                            if (_selectedSpeciesIndex == i) {
                                _selectedSpeciesIndex = -1;
                                DestroySpeciesEditor();
                            } else if (_selectedSpeciesIndex > i) {
                                _selectedSpeciesIndex--;
                            }

                            break;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Add Empty Slot", GUILayout.Width(FlockEditorUI.AddEmptySlotButtonWidth))) {
                        _setup.FishTypes.Add(null);
                        EditorUtility.SetDirty(_setup);
                    }

                    if (GUILayout.Button("Add New Preset", GUILayout.Width(FlockEditorUI.AddNewPresetButtonWidth))) {
                        CreateNewFishTypePreset();
                    }
                }
            }
        }

        private void CreateNewFishTypePreset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Fish Type Preset",
                "FishTypePreset",
                "asset",
                "Choose a location for the new FishTypePreset asset");

            if (string.IsNullOrEmpty(path))
                return;

            var preset = ScriptableObject.CreateInstance<FishTypePreset>();
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.FishTypes.Add(preset);
            EditorUtility.SetDirty(_setup);
            _selectedSpeciesIndex = _setup.FishTypes.Count - 1;

            RebuildSpeciesEditor();
            EditorGUIUtility.PingObject(preset);
        }


        // --------------------------------------------------------------------
        // Right: selected species detail (re-uses default inspector)
        // --------------------------------------------------------------------

        private void DrawSpeciesDetailPanel() {
            using (new EditorGUILayout.VerticalScope()) {
                EditorGUILayout.LabelField("Selected Fish Type", EditorStyles.boldLabel);

                if (_setup.FishTypes == null || _setup.FishTypes.Count == 0) {
                    EditorGUILayout.HelpBox(
                        "No fish types in this setup yet.\n\n" +
                        "Use 'Add Empty Slot' or 'Add New Preset' to create entries.",
                        MessageType.Info);
                    return;
                }

                if (_selectedSpeciesIndex < 0 || _selectedSpeciesIndex >= _setup.FishTypes.Count) {
                    EditorGUILayout.HelpBox(
                        "Select a fish type from the list on the left to edit its preset / behaviour.",
                        MessageType.Info);
                    return;
                }

                var preset = _setup.FishTypes[_selectedSpeciesIndex];
                if (preset == null) {
                    EditorGUILayout.HelpBox(
                        "This slot is empty. Assign an existing FishTypePreset or create a new one.",
                        MessageType.Warning);
                    return;
                }

                // Ensure editors are up-to-date with current preset + behaviour
                bool needRebuild = false;

                if (_presetEditor == null || _presetEditor.target != preset) {
                    needRebuild = true;
                } else {
                    var profile = preset.BehaviourProfile;
                    if (profile == null) {
                        if (_behaviourEditor != null && _behaviourEditor.target != null) {
                            needRebuild = true;
                        }
                    } else {
                        if (_behaviourEditor == null || _behaviourEditor.target != profile) {
                            needRebuild = true;
                        }
                    }
                }

                if (needRebuild) {
                    RebuildSpeciesEditor();
                }

                // -----------------------------------------------------------------
                // Toggle which inspector we are looking at: Preset vs Behaviour
                // -----------------------------------------------------------------
                var behaviourProfile = preset.BehaviourProfile;

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();

                    string[] modeLabels = { "Preset", "Behaviour" };
                    _speciesInspectorMode = Mathf.Clamp(_speciesInspectorMode, 0, 1);
                    _speciesInspectorMode = GUILayout.Toolbar(
                        _speciesInspectorMode,
                        modeLabels,
                        GUILayout.Width(FlockEditorUI.InspectorModeToolbarWidth));

                    // If there is no behaviour profile, force back to Preset view
                    if (behaviourProfile == null && _speciesInspectorMode == 1) {
                        _speciesInspectorMode = 0;
                    }
                }

                EditorGUILayout.Space();

                // -----------------------------------------------------------------
                // Actual inspector content
                // -----------------------------------------------------------------
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                if (_speciesInspectorMode == 0) {
                    // FishTypePreset inspector
                    if (_presetEditor != null) {
                        _presetEditor.OnInspectorGUI();
                    }
                } else {
                    // FishBehaviourProfile inspector
                    if (behaviourProfile == null) {
                        EditorGUILayout.HelpBox(
                            "This FishTypePreset has no BehaviourProfile assigned.\n" +
                            "Assign one in the Preset view first.",
                            MessageType.Info);
                    } else {
                        DrawBehaviourProfileInspectorCards(behaviourProfile);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        // FlockEditorWindow.cs
        // Replace your existing DrawBehaviourProfileInspectorCards with this:

        // inside FlockEditorWindow
        void DrawBehaviourProfileInspectorCards(FishBehaviourProfile target) {
            if (target == null) {
                return;
            }

            var so = new SerializedObject(target);
            so.Update();

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {

                // ---------------- Movement ----------------
                FlockEditorGUI.BeginCard("Movement");
                {
                    DrawPropertyNoDecorators(so.FindProperty("maxSpeed"));
                    DrawPropertyNoDecorators(so.FindProperty("maxAcceleration"));
                    DrawPropertyNoDecorators(so.FindProperty("desiredSpeed"));
                }
                FlockEditorGUI.EndCard();

                // ---------------- Noise ----------------
                FlockEditorGUI.BeginCard("Noise");
                {
                    DrawPropertyNoDecorators(so.FindProperty("wanderStrength"));
                    DrawPropertyNoDecorators(so.FindProperty("wanderFrequency"));
                    DrawPropertyNoDecorators(so.FindProperty("groupNoiseStrength"));
                    DrawPropertyNoDecorators(so.FindProperty("groupNoiseDirectionRate"));
                    DrawPropertyNoDecorators(so.FindProperty("groupNoiseSpeedWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("patternWeight"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Size & Schooling -------------
                FlockEditorGUI.BeginCard("Size & Schooling");
                {
                    DrawPropertyNoDecorators(so.FindProperty("bodyRadius"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingSpacingFactor"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingOuterFactor"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingStrength"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingInnerSoftness"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingRadialDamping"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingDeadzoneFraction"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Neighbourhood -------------
                FlockEditorGUI.BeginCard("Neighbourhood");
                {
                    DrawPropertyNoDecorators(so.FindProperty("neighbourRadius"));
                    DrawPropertyNoDecorators(so.FindProperty("separationRadius"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Neighbour Sampling Caps");
                {
                    DrawPropertyNoDecorators(so.FindProperty("maxNeighbourChecks"));
                    DrawPropertyNoDecorators(so.FindProperty("maxFriendlySamples"));
                    DrawPropertyNoDecorators(so.FindProperty("maxSeparationSamples"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Rule Weights (INCLUDING Influence) -------------
                FlockEditorGUI.BeginCard("Rule Weights");
                {
                    DrawPropertyNoDecorators(so.FindProperty("alignmentWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("cohesionWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("separationWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("influenceWeight")); // moved here
                }
                FlockEditorGUI.EndCard();

                // ------------- Relationships -------------
                FlockEditorGUI.BeginCard("Relationships");
                {
                    DrawPropertyNoDecorators(so.FindProperty("avoidanceWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("neutralWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("attractionResponse"));
                    DrawPropertyNoDecorators(so.FindProperty("avoidResponse"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Split Behaviour -------------
                FlockEditorGUI.BeginCard("Split Behaviour");
                {
                    DrawPropertyNoDecorators(so.FindProperty("splitPanicThreshold"));
                    DrawPropertyNoDecorators(so.FindProperty("splitLateralWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("splitAccelBoost"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Attraction -------------
                FlockEditorGUI.BeginCard("Attraction");
                {
                    DrawPropertyNoDecorators(so.FindProperty("attractionWeight"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Bounds -------------
                FlockEditorGUI.BeginCard("Bounds");
                {
                    DrawPropertyNoDecorators(so.FindProperty("boundsWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("boundsTangentialDamping"));
                    DrawPropertyNoDecorators(so.FindProperty("boundsInfluenceSuppression"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Grouping (NOW includes Group Flow + loner settings) -------------
                FlockEditorGUI.BeginCard("Grouping");
                {
                    // Group flow moved into this card
                    DrawPropertyNoDecorators(so.FindProperty("groupFlowWeight"));

                    DrawPropertyNoDecorators(so.FindProperty("minGroupSize"));
                    DrawPropertyNoDecorators(so.FindProperty("maxGroupSize"));
                    DrawPropertyNoDecorators(so.FindProperty("minGroupSizeWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("maxGroupSizeWeight"));

                    // Radius and loner tuning
                    DrawPropertyNoDecorators(so.FindProperty("groupRadiusMultiplier"));
                    DrawPropertyNoDecorators(so.FindProperty("lonerRadiusMultiplier"));
                    DrawPropertyNoDecorators(so.FindProperty("lonerCohesionBoost"));
                }
                FlockEditorGUI.EndCard();

                // ------------- Preferred Depth (all fields gated by toggle) -------------
                FlockEditorGUI.BeginCard("Preferred Depth");
                {
                    var useDepth = so.FindProperty("usePreferredDepth");
                    if (useDepth != null) {
                        DrawPropertyNoDecorators(useDepth);
                        bool enabled = useDepth.boolValue;

                        using (new EditorGUI.DisabledScope(!enabled)) {
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthMin"));
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthMax"));
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthWeight"));
                            DrawPropertyNoDecorators(so.FindProperty("depthBiasStrength"));
                            DrawPropertyNoDecorators(so.FindProperty("depthWinsOverAttractor"));
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthEdgeFraction"));
                        }
                    }
                }
                FlockEditorGUI.EndCard();
            });

            if (so.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(target);
            }
        }

        private void CreateGroupNoisePatternAsset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Group Noise Pattern",
                "GroupNoisePattern",
                "asset",
                "Choose a location for the GroupNoisePatternProfile asset");

            if (string.IsNullOrEmpty(path))
                return;

            var asset = ScriptableObject.CreateInstance<GroupNoisePatternProfile>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.GroupNoiseSettings = asset;
            EditorUtility.SetDirty(_setup);

            _selectedNoiseIndex = -1;       // select the main pattern
            RebuildNoiseEditors();

            EditorGUIUtility.PingObject(asset);
        }

        private void CreateInteractionMatrixAsset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Fish Interaction Matrix",
                "FishInteractionMatrix",
                "asset",
                "Choose a location for the FishInteractionMatrix asset");

            if (string.IsNullOrEmpty(path))
                return;

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

        private void RebuildGroupNoiseEditor() {
            if (_setup == null) {
                DestroyEditor(ref groupNoiseEditor);
                return;
            }

            EnsureEditor(ref groupNoiseEditor, _setup.GroupNoiseSettings as GroupNoisePatternProfile);
        }

        private void DestroyGroupNoiseEditor() {
            DestroyEditor(ref groupNoiseEditor);
        }

        private void RebuildSpeciesEditor() {
            if (_setup == null ||
                _setup.FishTypes == null ||
                _selectedSpeciesIndex < 0 ||
                _selectedSpeciesIndex >= _setup.FishTypes.Count) {
                DestroySpeciesEditor();
                return;
            }

            var preset = _setup.FishTypes[_selectedSpeciesIndex];
            EnsureEditor(ref _presetEditor, preset);

            var profile = preset != null ? preset.BehaviourProfile : null;
            EnsureEditor(ref _behaviourEditor, profile);
        }

        private void DestroySpeciesEditor() {
            DestroyEditor(ref _presetEditor);
            DestroyEditor(ref _behaviourEditor);
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
            DestroyPatternAssetEditor();    // add this line
            DestroySceneControllerEditor();
        }

        void DrawNoiseListPanel() {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(FlockEditorUI.NoiseListPanelWidth))) {
                _noiseListScroll = EditorGUILayout.BeginScrollView(_noiseListScroll);

                if (_noiseInspectorMode == 0) {
                    // ---------------- Group Noise Pattern ----------------
                    EditorGUILayout.LabelField("Group Noise Pattern", EditorStyles.boldLabel);

                    GroupNoisePatternProfile currentProfile =
                        _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                    using (new EditorGUILayout.HorizontalScope()) {
                        string label = currentProfile != null ? currentProfile.name : "<None>";
                        GUILayout.Label(label, GUILayout.Width(FlockEditorUI.ListNameColumnWidth));

                        EditorGUI.BeginChangeCheck();
                        currentProfile = (GroupNoisePatternProfile)EditorGUILayout.ObjectField(
                            GUIContent.none,
                            currentProfile,
                            typeof(GroupNoisePatternProfile),
                            false,
                            GUILayout.Width(FlockEditorUI.GroupNoiseInlineObjectFieldWidth));
                        if (EditorGUI.EndChangeCheck()) {
                            _setup.GroupNoiseSettings = currentProfile;
                            EditorUtility.SetDirty(_setup);
                            RebuildNoiseEditors();
                        }
                    }
                } else {
                    // ---------------- Pattern Assets list ----------------
                    EditorGUILayout.LabelField("Pattern Assets", EditorStyles.boldLabel);

                    if (_setup.PatternAssets == null) {
                        _setup.PatternAssets = new List<FlockLayer3PatternProfile>();
                        EditorUtility.SetDirty(_setup);
                    }

                    var patterns = _setup.PatternAssets;
                    int removeIndex = -1;

                    for (int i = 0; i < patterns.Count; i++) {
                        using (new EditorGUILayout.HorizontalScope()) {
                            FlockLayer3PatternProfile asset = patterns[i];

                            GUIStyle rowStyle = (_selectedNoiseIndex == i)
                                ? EditorStyles.miniButtonMid
                                : EditorStyles.miniButton;

                            string name = asset != null ? asset.name : "<Empty Slot>";
                            if (GUILayout.Button(name, rowStyle, GUILayout.Width(FlockEditorUI.ListNameColumnWidth))) {
                                _selectedNoiseIndex = i;
                                RebuildNoiseEditors();
                            }

                            EditorGUI.BeginChangeCheck();
                            asset = (FlockLayer3PatternProfile)EditorGUILayout.ObjectField(
                                GUIContent.none,
                                asset,
                                typeof(FlockLayer3PatternProfile),
                                false,
                                GUILayout.Width(FlockEditorUI.PatternInlineObjectFieldWidth));
                            if (EditorGUI.EndChangeCheck()) {
                                patterns[i] = asset;
                                EditorUtility.SetDirty(_setup);
                                if (_selectedNoiseIndex == i) {
                                    RebuildNoiseEditors();
                                }
                            }

                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(FlockEditorUI.RemoveRowButtonWidth))) {
                                removeIndex = i;
                            }
                        }
                    }

                    if (removeIndex >= 0 && removeIndex < patterns.Count) {
                        patterns.RemoveAt(removeIndex);
                        EditorUtility.SetDirty(_setup);

                        if (_selectedNoiseIndex == removeIndex) {
                            _selectedNoiseIndex = -1;
                            RebuildNoiseEditors();
                        } else if (_selectedNoiseIndex > removeIndex) {
                            _selectedNoiseIndex--;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(FlockEditorUI.SpaceMedium);

                // Bottom buttons (unique per sub-tab)
                using (new EditorGUILayout.HorizontalScope()) {
                    if (_noiseInspectorMode == 0) {
                        // Group noise: Create + Add Existing
                        using (new EditorGUI.DisabledScope(_setup == null)) {
                            if (GUILayout.Button("Create Group Pattern", GUILayout.Width(FlockEditorUI.CreateGroupPatternButtonWidth))) {
                                CreateGroupNoisePatternAsset();
                            }
                            if (GUILayout.Button("Add Existing", GUILayout.Width(FlockEditorUI.AddExistingButtonWidth))) {
                                var current = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
                                EditorGUIUtility.ShowObjectPicker<GroupNoisePatternProfile>(
                                    current,
                                    false,
                                    "",
                                    FlockEditorUI.GroupNoisePickerControlId);
                            }
                        }
                    } else {
                        // Pattern assets: Create Pattern + Add Slot
                        using (new EditorGUI.DisabledScope(_setup == null)) {
                            if (EditorGUILayout.DropdownButton(new GUIContent("Create Pattern"), FocusType.Passive, GUILayout.Width(FlockEditorUI.CreatePatternButtonWidth))) {
                                Rect r = GUILayoutUtility.GetLastRect();
                                ShowCreatePatternDropdown(r);
                            }
                        }

                        if (GUILayout.Button("Add Pattern Slot", GUILayout.Width(FlockEditorUI.AddPatternSlotButtonWidth))) {
                            _setup.PatternAssets.Add(null);
                            _selectedNoiseIndex = _setup.PatternAssets.Count - 1;
                            EditorUtility.SetDirty(_setup);
                            RebuildNoiseEditors();
                        }
                    }
                }
            }
        }

        void ShowCreatePatternDropdown(Rect buttonRect) {
            _createPatternDropdownState ??= new AdvancedDropdownState();

            _createPatternDropdown ??= new CreatePatternDropdown(
                _createPatternDropdownState,
                onPicked: CreatePatternAssetOfType);

            // Rebuild list each time (so newly added classes appear without recompile/reopen)
            _createPatternDropdown.RefreshItems();
            _createPatternDropdown.Show(buttonRect);
        }

        void DrawNoiseDetailPanel() {
            using (new EditorGUILayout.VerticalScope()) {

                _noiseDetailScroll = EditorGUILayout.BeginScrollView(_noiseDetailScroll);

                if (_noiseInspectorMode == 0) {
                    var profile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
                    if (profile == null) {
                        EditorGUILayout.HelpBox(
                            "Assign or create a GroupNoisePatternProfile to edit.",
                            MessageType.Info);

                        EditorGUILayout.EndScrollView();
                        return;
                    }

                    DrawGroupNoiseInspectorCards(profile);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                // Pattern Assets
                if (_setup.PatternAssets == null || _setup.PatternAssets.Count == 0) {
                    EditorGUILayout.HelpBox(
                        "No pattern assets registered.\nUse 'Create Pattern' or 'Add Pattern Slot'.",
                        MessageType.Info);

                    EditorGUILayout.EndScrollView();
                    return;
                }

                if (_selectedNoiseIndex < 0 || _selectedNoiseIndex >= _setup.PatternAssets.Count) {
                    EditorGUILayout.HelpBox(
                        "Select a pattern asset from the list on the left.",
                        MessageType.Info);

                    EditorGUILayout.EndScrollView();
                    return;
                }

                var target = _setup.PatternAssets[_selectedNoiseIndex];
                if (target == null) {
                    EditorGUILayout.HelpBox(
                        "This slot is empty. Assign an existing pattern asset or create a new one.",
                        MessageType.Info);

                    EditorGUILayout.EndScrollView();
                    return;
                }

                DrawPatternAssetInspectorCards(target);

                EditorGUILayout.EndScrollView();
            }
        }

        static bool ShouldUseAttributeDrivenDrawer(SerializedProperty p) {
            if (p == null || p.serializedObject == null || p.serializedObject.targetObject == null) {
                return false;
            }

            var rootType = p.serializedObject.targetObject.GetType();

            if (!TryGetFieldInfoFromPropertyPath(rootType, p.propertyPath, out FieldInfo fi) || fi == null) {
                return false;
            }

            // Anything that changes UI/validation needs the real PropertyField path.
            if (fi.GetCustomAttribute<RangeAttribute>(inherit: true) != null) return true;
            if (fi.GetCustomAttribute<MinAttribute>(inherit: true) != null) return true;

            return false;
        }


        static void DrawPropertyNoDecorators(SerializedProperty p, GUIContent labelOverride = null) {
            if (p == null) return;

            GUIContent label = labelOverride ?? EditorGUIUtility.TrTextContent(p.displayName);

            switch (p.propertyType) {
                case SerializedPropertyType.Boolean: {
                        EditorGUI.BeginChangeCheck();
                        bool v = EditorGUILayout.Toggle(label, p.boolValue);
                        if (EditorGUI.EndChangeCheck()) p.boolValue = v;
                        break;
                    }
                case SerializedPropertyType.Integer: {
                        if (ShouldUseAttributeDrivenDrawer(p)) {
                            FlockEditorGUI.PropertyFieldClamped(p, includeChildren: true, labelOverride: labelOverride);
                            break;
                        }

                        EditorGUI.BeginChangeCheck();
                        int v = EditorGUILayout.IntField(label, p.intValue);
                        if (EditorGUI.EndChangeCheck()) p.intValue = v;
                        break;
                    }

                case SerializedPropertyType.Float: {
                        if (ShouldUseAttributeDrivenDrawer(p)) {
                            FlockEditorGUI.PropertyFieldClamped(p, includeChildren: true, labelOverride: labelOverride);
                            break;
                        }

                        EditorGUI.BeginChangeCheck();
                        float v = EditorGUILayout.FloatField(label, p.floatValue);
                        if (EditorGUI.EndChangeCheck()) p.floatValue = v;
                        break;
                    }
                case SerializedPropertyType.Enum: {
                        EditorGUI.BeginChangeCheck();
                        int v = EditorGUILayout.Popup(label, p.enumValueIndex, p.enumDisplayNames);
                        if (EditorGUI.EndChangeCheck()) p.enumValueIndex = v;
                        break;
                    }
                case SerializedPropertyType.Vector2: {
                        EditorGUI.BeginChangeCheck();
                        Vector2 v = EditorGUILayout.Vector2Field(label, p.vector2Value);
                        if (EditorGUI.EndChangeCheck()) p.vector2Value = v;
                        break;
                    }
                case SerializedPropertyType.Vector3: {
                        EditorGUI.BeginChangeCheck();
                        Vector3 v = EditorGUILayout.Vector3Field(label, p.vector3Value);
                        if (EditorGUI.EndChangeCheck()) p.vector3Value = v;
                        break;
                    }
                default:
                    // Fallback uses your clamped drawer (now includes the fixed array rendering).
                    FlockEditorGUI.PropertyFieldClamped(p, includeChildren: true, labelOverride: labelOverride);
                    break;
            }
        }

        static bool TryGetHeaderForPropertyPath(Type rootType, string propertyPath, out string header) {
            header = null;
            if (!TryGetFieldInfoFromPropertyPath(rootType, propertyPath, out FieldInfo fi)) {
                return false;
            }

            var h = fi.GetCustomAttribute<HeaderAttribute>(inherit: true);
            if (h == null || string.IsNullOrEmpty(h.header)) {
                return false;
            }

            header = h.header;
            return true;
        }

        static bool TryGetFieldInfoFromPropertyPath(Type rootType, string propertyPath, out FieldInfo field) {
            field = null;
            if (rootType == null || string.IsNullOrEmpty(propertyPath)) return false;

            string[] parts = propertyPath.Split('.');
            Type currentType = rootType;
            FieldInfo currentField = null;

            for (int i = 0; i < parts.Length; i++) {
                string part = parts[i];

                if (part == "Array") continue;

                if (part.StartsWith("data[", StringComparison.Ordinal)) {
                    if (currentField == null) return false;

                    // move to element type for lists/arrays
                    Type ft = currentField.FieldType;
                    if (ft.IsArray) currentType = ft.GetElementType();
                    else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                        currentType = ft.GetGenericArguments()[0];
                    else
                        currentType = ft;

                    continue;
                }

                currentField = FindFieldInHierarchy(currentType, part);
                if (currentField == null) return false;

                currentType = currentField.FieldType;
            }

            field = currentField;
            return field != null;
        }

        static FieldInfo FindFieldInHierarchy(Type t, string name) {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            while (t != null) {
                var f = t.GetField(name, Flags);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        void DrawGroupNoiseInspectorCards(GroupNoisePatternProfile profile) {
            var so = new SerializedObject(profile);
            so.Update();

            SerializedProperty pBaseFrequency = so.FindProperty("baseFrequency");
            SerializedProperty pTimeScale = so.FindProperty("timeScale");
            SerializedProperty pPhaseOffset = so.FindProperty("phaseOffset");
            SerializedProperty pWorldScale = so.FindProperty("worldScale");
            SerializedProperty pSeed = so.FindProperty("seed");

            SerializedProperty pPatternType = so.FindProperty("patternType");

            SerializedProperty pSwirlStrength = so.FindProperty("swirlStrength");
            SerializedProperty pVerticalBias = so.FindProperty("verticalBias");

            SerializedProperty pVortexCenter = so.FindProperty("vortexCenterNorm");
            SerializedProperty pVortexRadius = so.FindProperty("vortexRadius");
            SerializedProperty pVortexTight = so.FindProperty("vortexTightness");

            SerializedProperty pSphereRadius = so.FindProperty("sphereRadius");
            SerializedProperty pSphereThick = so.FindProperty("sphereThickness");
            SerializedProperty pSphereSwirl = so.FindProperty("sphereSwirlStrength");
            SerializedProperty pSphereCenter = so.FindProperty("sphereCenterNorm");

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {

                FlockEditorGUI.BeginCard("Common");
                {
                    // draw WITHOUT decorators so Header("Common") doesn't duplicate the card title
                    DrawPropertyNoDecorators(pBaseFrequency);
                    DrawPropertyNoDecorators(pTimeScale);
                    DrawPropertyNoDecorators(pPhaseOffset);
                    DrawPropertyNoDecorators(pWorldScale);
                    DrawPropertyNoDecorators(pSeed);
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Pattern Type");
                {
                    DrawPropertyNoDecorators(pPatternType);
                }
                FlockEditorGUI.EndCard();

                var patternType = (FlockGroupNoisePatternType)(pPatternType != null ? pPatternType.enumValueIndex : 0);

                switch (patternType) {
                    case FlockGroupNoisePatternType.SimpleSine:
                        FlockEditorGUI.BeginCard("Simple Sine Extras"); {
                            DrawPropertyNoDecorators(pSwirlStrength);
                            DrawPropertyNoDecorators(pVerticalBias);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.VerticalBands:
                        FlockEditorGUI.BeginCard("Vertical Bands Extras"); {
                            DrawPropertyNoDecorators(pSwirlStrength);
                            DrawPropertyNoDecorators(pVerticalBias);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.Vortex:
                        FlockEditorGUI.BeginCard("Vortex Settings"); {
                            DrawPropertyNoDecorators(pVortexCenter);
                            DrawPropertyNoDecorators(pVortexRadius);
                            DrawPropertyNoDecorators(pVortexTight);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.SphereShell:
                        FlockEditorGUI.BeginCard("Sphere Shell Settings"); {
                            DrawPropertyNoDecorators(pSphereRadius);
                            DrawPropertyNoDecorators(pSphereThick);
                            DrawPropertyNoDecorators(pSphereSwirl);
                            DrawPropertyNoDecorators(pSphereCenter);
                        }
                        FlockEditorGUI.EndCard();
                        break;
                }
            });

            if (so.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(profile);
            }
        }

        void HandleGroupNoiseObjectPicker() {
            // Only care in Group Noise mode
            if (_noiseInspectorMode != 0 || _setup == null) {
                return;
            }

            Event e = Event.current;
            if (e == null) {
                return;
            }

            if (e.commandName != "ObjectSelectorClosed") {
                return;
            }

            if (EditorGUIUtility.GetObjectPickerControlID() != FlockEditorUI.GroupNoisePickerControlId) {
                return;
            }

            var picked = EditorGUIUtility.GetObjectPickerObject() as GroupNoisePatternProfile;
            if (picked == _setup.GroupNoiseSettings) {
                return;
            }

            _setup.GroupNoiseSettings = picked;
            EditorUtility.SetDirty(_setup);
            RebuildNoiseEditors();
        }

        void CreatePatternAssetOfType(System.Type patternType) {
            if (_setup == null || patternType == null) {
                return;
            }

            string defaultName = patternType.Name;
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Pattern Asset",
                defaultName,
                "asset",
                "Choose a location for the new pattern asset");

            if (string.IsNullOrEmpty(path)) {
                return;
            }

            var asset = ScriptableObject.CreateInstance(patternType) as FlockLayer3PatternProfile;
            if (asset == null) {
                EditorUtility.DisplayDialog("Create Pattern", "Failed to create asset instance.", "OK");
                return;
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.PatternAssets ??= new List<FlockLayer3PatternProfile>();
            _setup.PatternAssets.Add(asset);

            _selectedNoiseIndex = _setup.PatternAssets.Count - 1;
            EditorUtility.SetDirty(_setup);

            RebuildNoiseEditors();
            EditorGUIUtility.PingObject(asset);
        }

        private void RebuildNoiseEditors() {
            // Always keep both clean; rebuild only what’s active
            DestroyEditor(ref groupNoiseEditor);
            DestroyEditor(ref _patternAssetEditor);

            if (_setup == null) return;

            if (_noiseInspectorMode == 0) {
                EnsureEditor(ref groupNoiseEditor, _setup.GroupNoiseSettings as GroupNoisePatternProfile);
                return;
            }

            if (_setup.PatternAssets == null) return;

            FlockLayer3PatternProfile target = null;
            if (_selectedNoiseIndex >= 0 && _selectedNoiseIndex < _setup.PatternAssets.Count) {
                target = _setup.PatternAssets[_selectedNoiseIndex];
            }

            EnsureEditor(ref _patternAssetEditor, target);
        }

        private void DestroyPatternAssetEditor() {
            DestroyEditor(ref _patternAssetEditor);
        }


        sealed class CreatePatternDropdown : AdvancedDropdown {
            readonly Action<Type> _onPicked;

            Type[] _types = Array.Empty<Type>();

            // Unity-safe replacement for "userData"
            readonly Dictionary<int, Type> _idToType =
                new Dictionary<int, Type>(FlockEditorUI.CreatePatternTypeMapInitialCapacity);
            int _nextId = 1;

            public CreatePatternDropdown(AdvancedDropdownState state, Action<Type> onPicked)
                : base(state) {

                _onPicked = onPicked;
                minimumSize = new UnityEngine.Vector2(
                    FlockEditorUI.CreatePatternDropdownMinWidth,
                    FlockEditorUI.CreatePatternDropdownMinHeight);
            }

            public void RefreshItems() {
                _types = TypeCache.GetTypesDerivedFrom<FlockLayer3PatternProfile>()
                    .ToArray();
            }

            protected override AdvancedDropdownItem BuildRoot() {
                _idToType.Clear();
                _nextId = 1;

                var root = new AdvancedDropdownItem("Create Layer-3 Pattern");
                var icon = EditorGUIUtility.IconContent("ScriptableObject Icon")?.image as Texture2D;

                var groups = new Dictionary<string, List<Type>>(FlockEditorUI.CreatePatternGroupsInitialCapacity);

                for (int i = 0; i < _types.Length; i++) {
                    var t = _types[i];
                    if (t == null || t.IsAbstract) {
                        continue;
                    }

                    string group = GetGroupName(t);
                    if (!groups.TryGetValue(group, out var list)) {
                        list = new List<Type>(FlockEditorUI.CreatePatternGroupListInitialCapacity);
                        groups.Add(group, list);
                    }
                    list.Add(t);
                }

                var groupKeys = new List<string>(groups.Keys);
                groupKeys.Sort(StringComparer.OrdinalIgnoreCase);

                bool anyAdded = false;

                foreach (var g in groupKeys) {
                    var groupItem = new AdvancedDropdownItem(g);
                    root.AddChild(groupItem);

                    var list = groups[g];
                    list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(GetPrettyName(a), GetPrettyName(b)));

                    for (int i = 0; i < list.Count; i++) {
                        var t = list[i];

                        int id = _nextId++;
                        _idToType[id] = t;

                        var item = new AdvancedDropdownItem(GetPrettyName(t)) {
                            id = id,
                            icon = icon
                        };

                        groupItem.AddChild(item);
                        anyAdded = true;
                    }
                }

                if (!anyAdded) {
                    root.AddChild(new AdvancedDropdownItem("No concrete pattern profile types found"));
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item) {
                if (item == null) {
                    return;
                }

                if (_idToType.TryGetValue(item.id, out var t)) {
                    _onPicked?.Invoke(t);
                }
            }

            static string GetGroupName(Type t) {
                string ns = t.Namespace ?? "";
                if (string.IsNullOrEmpty(ns)) {
                    return "Patterns";
                }

                int lastDot = ns.LastIndexOf('.');
                return (lastDot >= 0 && lastDot < ns.Length - 1) ? ns.Substring(lastDot + 1) : ns;
            }

            static string GetPrettyName(Type t) {
                string name = t.Name;

                name = name.Replace("PatternProfile", "");
                name = name.Replace("Profile", "");
                name = name.Replace("Flock", "");
                name = name.Replace("Layer3", "");

                name = name.Trim();
                if (string.IsNullOrEmpty(name)) {
                    name = t.Name;
                }

                var sb = new StringBuilder(name.Length + 8);
                for (int i = 0; i < name.Length; i++) {
                    char c = name[i];
                    if (i > 0 && char.IsUpper(c) && char.IsLetterOrDigit(name[i - 1]) && !char.IsUpper(name[i - 1])) {
                        sb.Append(' ');
                    }
                    sb.Append(c);
                }

                return sb.ToString().Trim();
            }
        }

        void DrawSceneControllerInspectorCards(FlockController controller) {
            if (controller == null) return;

            var so = new SerializedObject(controller);
            so.Update();

            // Bounds-related properties for custom UI
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

                    // Custom Bounds card: draw once, hide raw fields
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

                    // Skip remaining raw bounds fields after the card
                    if (boundsCardDrawn &&
                        (it.propertyPath == "boundsType"
                         || it.propertyPath == "boundsCenter"
                         || it.propertyPath == "boundsExtents"
                         || it.propertyPath == "boundsSphereRadius")) {
                        continue;
                    }

                    // Normal section-based cards
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
                return; // controller is missing these fields
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
                    // If type is missing, just show both (legacy safety)
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

        void DrawPatternAssetInspectorCards(FlockLayer3PatternProfile target) {
            if (target == null) {
                return;
            }

            var so = new SerializedObject(target);
            so.Update();

            Type rootType = target.GetType();
            string currentSection = null;
            bool sectionOpen = false;

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {
                SerializedProperty it = so.GetIterator();
                bool enterChildren = true;

                while (it.NextVisible(enterChildren)) {
                    enterChildren = false;

                    // Top-level only; let PropertyField draw children (arrays, structs) normally.
                    if (it.depth != 0) {
                        continue;
                    }

                    if (it.propertyPath == "m_Script") {
                        continue;
                    }

                    // Start/switch cards when we hit a [Header("...")]
                    if (TryGetHeaderForPropertyPath(rootType, it.propertyPath, out string header)) {
                        if (!sectionOpen || !string.Equals(currentSection, header, StringComparison.Ordinal)) {
                            if (sectionOpen) {
                                FlockEditorGUI.EndCard();
                            }

                            currentSection = header;
                            FlockEditorGUI.BeginCard(currentSection);
                            sectionOpen = true;
                        }
                    } else if (!sectionOpen) {
                        // If there is no header at the start, still group somewhere sane.
                        currentSection = "Settings";
                        FlockEditorGUI.BeginCard(currentSection);
                        sectionOpen = true;
                    }

                    var prop = it.Copy();
                    GUIContent labelOverride =
                        (!string.IsNullOrEmpty(currentSection) && string.Equals(prop.displayName, currentSection, StringComparison.Ordinal))
                            ? GUIContent.none
                            : null;

                    DrawPropertyNoDecorators(prop, labelOverride);

                }

                if (sectionOpen) {
                    FlockEditorGUI.EndCard();
                }
            });

            if (so.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(target);
            }
        }

        static void DestroyEditor(ref UnityEditor.Editor ed) {
            if (ed == null) return;
            DestroyImmediate(ed);
            ed = null;
        }

        static void EnsureEditor(ref UnityEditor.Editor ed, UnityEngine.Object target) {
            if (target == null) {
                DestroyEditor(ref ed);
                return;
            }

            if (ed != null && ed.target == target) {
                return; // already correct
            }

            DestroyEditor(ref ed);
            ed = UnityEditor.Editor.CreateEditor(target);
        }

    }
}
#endif
