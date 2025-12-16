#if UNITY_EDITOR
using Flock.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
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
        const int GroupNoisePickerControlId = 701231;
        private bool _isSceneAutoSyncing = false;
        private double _nextSceneAutoSyncTime = 0.0;

        private UnityEditor.Editor sceneControllerEditor;
        private Vector2 sceneScroll;
        private AdvancedDropdownState _createPatternDropdownState;
        private CreatePatternDropdown _createPatternDropdown;

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
                GUILayout.Width(240f));
            if (EditorGUI.EndChangeCheck()) {
                // Rebuild only what's relevant for the active sub-tab
                RebuildNoiseEditors();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
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

                // One-shot sync now; continuous sync remains in OnEditorUpdate (throttled).
                if (!EditorApplication.isPlayingOrWillChangePlaymode && sceneController != null) {
                    TryAutoSyncSetupToController(sceneController);
                }
            }


            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Find In Scene", GUILayout.Width(120f))) {
                    var found = FindObjectOfType<FlockController>();
                    if (found != null) {
                        sceneController = found;

                        // Invalidate cached editor; it will rebuild below when needed.
                        DestroySceneControllerEditor();

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

            if (sceneControllerEditor != null) {
                // If user edits controller references here, pull them back into setup.
                EditorGUI.BeginChangeCheck();
                sceneControllerEditor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) {
                        TryPullControllerRefsIntoSetup(sceneController);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnEditorUpdate() {
            if (_setup == null || sceneController == null) {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                return;
            }

            // throttle to avoid hammering serialization every frame
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextSceneAutoSyncTime) {
                return;
            }
            _nextSceneAutoSyncTime = now + 0.2;

            if (TryAutoSyncSetupToController(sceneController)) {
                Repaint();
            }
        }

        private bool TryAutoSyncSetupToController(FlockController controller) {
            if (_isSceneAutoSyncing || controller == null || _setup == null) {
                return false;
            }

            _isSceneAutoSyncing = true;
            try {
                bool changed = false;

                SerializedObject so = new SerializedObject(controller);

                // Fish types (setup -> controller)
                SerializedProperty fishTypesProp = so.FindProperty("fishTypes");
                if (fishTypesProp != null) {
                    var desired = _setup.FishTypes;
                    int desiredCount = desired != null ? desired.Count : 0;

                    if (!FishTypesMatch(fishTypesProp, desired, desiredCount)) {
                        fishTypesProp.arraySize = desiredCount;
                        for (int i = 0; i < desiredCount; i++) {
                            fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue = desired[i];
                        }
                        changed = true;
                    }
                }

                // Interaction matrix (setup -> controller)
                SerializedProperty interactionProp = so.FindProperty("interactionMatrix");
                if (interactionProp != null) {
                    if (interactionProp.objectReferenceValue != _setup.InteractionMatrix) {
                        interactionProp.objectReferenceValue = _setup.InteractionMatrix;
                        changed = true;
                    }
                }

                // Group noise (setup -> controller)
                SerializedProperty groupNoiseProp = so.FindProperty("groupNoisePattern");
                if (groupNoiseProp != null) {
                    if (groupNoiseProp.objectReferenceValue != _setup.GroupNoiseSettings) {
                        groupNoiseProp.objectReferenceValue = _setup.GroupNoiseSettings;
                        changed = true;
                    }
                }

                if (changed) {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(controller);

                    // keep spawner types in lockstep too (silent)
                    TrySyncSpawnerFromController(controller);
                }

                return changed;
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

            var ctrlMatrix = so.FindProperty("interactionMatrix")?.objectReferenceValue as FishInteractionMatrix;
            if (ctrlMatrix != null && _setup.InteractionMatrix != ctrlMatrix) {
                Undo.RecordObject(_setup, "Sync Setup Interaction Matrix");
                _setup.InteractionMatrix = ctrlMatrix;
                changed = true;
            }

            var ctrlNoise = so.FindProperty("groupNoisePattern")?.objectReferenceValue as GroupNoisePatternProfile;
            if (ctrlNoise != null && _setup.GroupNoiseSettings != ctrlNoise) {
                Undo.RecordObject(_setup, "Sync Setup Group Noise");
                _setup.GroupNoiseSettings = ctrlNoise;
                changed = true;
            }

            if (changed) {
                EditorUtility.SetDirty(_setup);
                RebuildInteractionMatrixEditor();
                RebuildNoiseEditors();
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

            spawner.EditorSyncTypesFrom(types);
            EditorUtility.SetDirty(spawner);
            return true;
        }

        private void RebuildSceneControllerEditor() {
            DestroySceneControllerEditor();

            if (sceneController == null) {
                return;
            }

            sceneControllerEditor = UnityEditor.Editor.CreateEditor(sceneController);
        }

        private void DestroySceneControllerEditor() {
            if (sceneControllerEditor != null) {
                DestroyImmediate(sceneControllerEditor);
                sceneControllerEditor = null;
            }
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
                    if (GUILayout.Button("Create Matrix Asset", GUILayout.Width(150f))) {
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
                EditorGUILayout.Space(2f);

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
            }

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Create Setup", GUILayout.Width(120f))) {
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
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(280f))) {
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
                            GUILayout.Width(90f));

                        // Remove button
                        if (GUILayout.Button("X", GUILayout.Width(20f))) {
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
                    if (GUILayout.Button("Add Empty Slot", GUILayout.Width(130f))) {
                        _setup.FishTypes.Add(null);
                        EditorUtility.SetDirty(_setup);
                    }

                    if (GUILayout.Button("Add New Preset", GUILayout.Width(130f))) {
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
                        GUILayout.Width(200f));

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
                    } else if (_behaviourEditor != null) {
                        _behaviourEditor.OnInspectorGUI();
                    }
                }

                EditorGUILayout.EndScrollView();
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
            DestroyInteractionMatrixEditor();

            if (_setup == null || _setup.InteractionMatrix == null)
                return;

            _interactionMatrixEditor = UnityEditor.Editor.CreateEditor(_setup.InteractionMatrix);
        }

        private void DestroyInteractionMatrixEditor() {
            if (_interactionMatrixEditor != null) {
                DestroyImmediate(_interactionMatrixEditor);
                _interactionMatrixEditor = null;
            }
        }


        private void RebuildGroupNoiseEditor() {
            DestroyGroupNoiseEditor();

            if (_setup == null || _setup.GroupNoiseSettings == null)
                return;

            var profile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
            if (profile == null)
                return;

            groupNoiseEditor = UnityEditor.Editor.CreateEditor(profile);
        }

        private void DestroyGroupNoiseEditor() {
            if (groupNoiseEditor != null) {
                DestroyImmediate(groupNoiseEditor);
                groupNoiseEditor = null;
            }
        }

        private void RebuildSpeciesEditor() {
            DestroySpeciesEditor();

            if (_setup == null ||
                _setup.FishTypes == null ||
                _selectedSpeciesIndex < 0 ||
                _selectedSpeciesIndex >= _setup.FishTypes.Count) {
                return;
            }

            var preset = _setup.FishTypes[_selectedSpeciesIndex];
            if (preset == null) {
                return;
            }

            // Main: FishTypePreset inspector
            _presetEditor = UnityEditor.Editor.CreateEditor(preset);

            // Secondary: BehaviourProfile inspector (optional)
            var profile = preset.BehaviourProfile;
            if (profile != null) {
                _behaviourEditor = UnityEditor.Editor.CreateEditor(profile);
            }
        }

        private void DestroySpeciesEditor() {
            if (_presetEditor != null) {
                DestroyImmediate(_presetEditor);
                _presetEditor = null;
            }

            if (_behaviourEditor != null) {
                DestroyImmediate(_behaviourEditor);
                _behaviourEditor = null;
            }
        }

        private void OnEnable() {
            EditorApplication.update += OnEditorUpdate;
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
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(320f))) {
                _noiseListScroll = EditorGUILayout.BeginScrollView(_noiseListScroll);

                if (_noiseInspectorMode == 0) {
                    // ---------------- Group Noise Pattern ----------------
                    EditorGUILayout.LabelField("Group Noise Pattern", EditorStyles.boldLabel);

                    GroupNoisePatternProfile currentProfile =
                        _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                    using (new EditorGUILayout.HorizontalScope()) {
                        string label = currentProfile != null ? currentProfile.name : "<None>";
                        GUILayout.Label(label, GUILayout.Width(130f));

                        EditorGUI.BeginChangeCheck();
                        currentProfile = (GroupNoisePatternProfile)EditorGUILayout.ObjectField(
                            GUIContent.none,
                            currentProfile,
                            typeof(GroupNoisePatternProfile),
                            false,
                            GUILayout.Width(170f));
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
                            if (GUILayout.Button(name, rowStyle, GUILayout.Width(130f))) {
                                _selectedNoiseIndex = i;
                                RebuildNoiseEditors();
                            }

                            EditorGUI.BeginChangeCheck();
                            asset = (FlockLayer3PatternProfile)EditorGUILayout.ObjectField(
                                GUIContent.none,
                                asset,
                                typeof(FlockLayer3PatternProfile),
                                false,
                                GUILayout.Width(150f));
                            if (EditorGUI.EndChangeCheck()) {
                                patterns[i] = asset;
                                EditorUtility.SetDirty(_setup);
                                if (_selectedNoiseIndex == i) {
                                    RebuildNoiseEditors();
                                }
                            }

                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20f))) {
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
                EditorGUILayout.Space(4f);

                // Bottom buttons (unique per sub-tab)
                using (new EditorGUILayout.HorizontalScope()) {
                    if (_noiseInspectorMode == 0) {
                        // Group noise: Create + Add Existing
                        using (new EditorGUI.DisabledScope(_setup == null)) {
                            if (GUILayout.Button("Create Group Pattern", GUILayout.Width(160f))) {
                                CreateGroupNoisePatternAsset();
                            }
                            if (GUILayout.Button("Add Existing", GUILayout.Width(140f))) {
                                var current = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
                                EditorGUIUtility.ShowObjectPicker<GroupNoisePatternProfile>(
                                    current,
                                    false,
                                    "",
                                    GroupNoisePickerControlId);
                            }
                        }
                    } else {
                        // Pattern assets: Create Pattern + Add Slot
                        using (new EditorGUI.DisabledScope(_setup == null)) {
                            if (EditorGUILayout.DropdownButton(new GUIContent("Create Pattern"), FocusType.Passive, GUILayout.Width(160f))) {
                                Rect r = GUILayoutUtility.GetLastRect();
                                ShowCreatePatternDropdown(r);
                            }
                        }

                        if (GUILayout.Button("Add Pattern Slot", GUILayout.Width(140f))) {
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


        // RIGHT COLUMN: inspector for currently selected noise / pattern
        void DrawNoiseDetailPanel() {
            using (new EditorGUILayout.VerticalScope()) {
                EditorGUILayout.LabelField("Selected Noise / Pattern", EditorStyles.boldLabel);

                if (_noiseInspectorMode == 0) {
                    // Group noise inspector
                    UnityEngine.Object targetAsset = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
                    if (targetAsset == null) {
                        EditorGUILayout.HelpBox(
                            "Assign or create a GroupNoisePatternProfile to edit.",
                            MessageType.Info);
                        return;
                    }

                    if (groupNoiseEditor == null || groupNoiseEditor.target != targetAsset) {
                        RebuildGroupNoiseEditor();
                    }

                    _noiseDetailScroll = EditorGUILayout.BeginScrollView(_noiseDetailScroll);
                    if (groupNoiseEditor != null) {
                        groupNoiseEditor.OnInspectorGUI();
                    }
                    EditorGUILayout.EndScrollView();
                    return;
                }

                // Pattern asset inspector
                if (_setup.PatternAssets == null || _setup.PatternAssets.Count == 0) {
                    EditorGUILayout.HelpBox(
                        "No pattern assets registered.\nUse 'Create Pattern' or 'Add Pattern Slot'.",
                        MessageType.Info);
                    return;
                }

                if (_selectedNoiseIndex < 0 || _selectedNoiseIndex >= _setup.PatternAssets.Count) {
                    EditorGUILayout.HelpBox(
                        "Select a pattern asset from the list on the left.",
                        MessageType.Info);
                    return;
                }

                UnityEngine.Object targetPattern = _setup.PatternAssets[_selectedNoiseIndex];
                if (targetPattern == null) {
                    EditorGUILayout.HelpBox(
                        "This slot is empty. Assign an existing pattern asset or create a new one.",
                        MessageType.Info);
                    return;
                }

                if (_patternAssetEditor == null || _patternAssetEditor.target != targetPattern) {
                    DestroyPatternAssetEditor();
                    _patternAssetEditor = UnityEditor.Editor.CreateEditor(targetPattern);
                }

                _noiseDetailScroll = EditorGUILayout.BeginScrollView(_noiseDetailScroll);
                if (_patternAssetEditor != null) {
                    _patternAssetEditor.OnInspectorGUI();
                }
                EditorGUILayout.EndScrollView();
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

            if (EditorGUIUtility.GetObjectPickerControlID() != GroupNoisePickerControlId) {
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
            // Always clear both, then rebuild only what's needed
            DestroyGroupNoiseEditor();
            DestroyPatternAssetEditor();

            if (_setup == null) {
                return;
            }

            if (_noiseInspectorMode == 0) {
                // Group Noise mode
                RebuildGroupNoiseEditor();
                return;
            }

            // Pattern Assets mode
            if (_setup.PatternAssets == null) {
                return;
            }

            if (_selectedNoiseIndex >= 0 && _selectedNoiseIndex < _setup.PatternAssets.Count) {
                var asset = _setup.PatternAssets[_selectedNoiseIndex];
                if (asset != null) {
                    _patternAssetEditor = UnityEditor.Editor.CreateEditor(asset);
                }
            }
        }

        private void DestroyPatternAssetEditor() {
            if (_patternAssetEditor != null) {
                DestroyImmediate(_patternAssetEditor);
                _patternAssetEditor = null;
            }
        }

        sealed class CreatePatternDropdown : AdvancedDropdown {
            readonly Action<Type> _onPicked;

            Type[] _types = Array.Empty<Type>();

            // Unity-safe replacement for "userData"
            readonly Dictionary<int, Type> _idToType = new Dictionary<int, Type>(64);
            int _nextId = 1;

            public CreatePatternDropdown(AdvancedDropdownState state, Action<Type> onPicked)
                : base(state) {

                _onPicked = onPicked;
                minimumSize = new UnityEngine.Vector2(320f, 420f);
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

                var groups = new Dictionary<string, List<Type>>(8);

                for (int i = 0; i < _types.Length; i++) {
                    var t = _types[i];
                    if (t == null || t.IsAbstract) {
                        continue;
                    }

                    string group = GetGroupName(t);
                    if (!groups.TryGetValue(group, out var list)) {
                        list = new List<Type>(8);
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
    }
}
#endif
