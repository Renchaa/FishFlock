#if UNITY_EDITOR
using System.Collections.Generic;
using Flock.Runtime;
using UnityEditor;
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

        FishInteractionMatrix cachedMatrix;
        UnityEditor.Editor interactionMatrixEditor; // reuse existing inspector layout logic if needed
        int selectedRelationFishIndex = -1;        // row selector inside matrix UI
        private UnityEditor.Editor sceneControllerEditor;
        private Vector2 sceneScroll;
        Vector2 interactionScroll;                // scroll view for big matrices

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
                    using (new EditorGUILayout.HorizontalScope()) {
                        DrawNoiseListPanel();
                        DrawNoiseDetailPanel();
                    }
                    break;

                case 3: // Scene / Simulation (Phase 4 wiring)
                    DrawSceneSimulationPanel();
                    break;
            }
        }

        private void DrawSceneSimulationPanel() {
            EditorGUILayout.LabelField("Scene / Simulation", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Scene controller selection
            EditorGUI.BeginChangeCheck();
            sceneController = (FlockController)EditorGUILayout.ObjectField(
                "Scene Controller",
                sceneController,
                typeof(FlockController),
                true);
            if (EditorGUI.EndChangeCheck()) {
                RebuildSceneControllerEditor();
            }

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Find In Scene", GUILayout.Width(120f))) {
                    var found = FindObjectOfType<FlockController>();
                    if (found != null) {
                        sceneController = found;
                        RebuildSceneControllerEditor();
                        EditorGUIUtility.PingObject(found);
                    } else {
                        EditorUtility.DisplayDialog(
                            "Flock Controller",
                            "No FlockController was found in the open scene.",
                            "OK");
                    }
                }

                using (new EditorGUI.DisabledScope(sceneController == null)) {
                    if (GUILayout.Button("Ping", GUILayout.Width(60f))) {
                        EditorGUIUtility.PingObject(sceneController);
                    }

                    using (new EditorGUI.DisabledScope(_setup == null)) {
                        if (GUILayout.Button("Apply Setup → Controller", GUILayout.Width(200f))) {
                            ApplySetupToController(sceneController);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    using (new EditorGUI.DisabledScope(sceneController == null)) {
                        if (GUILayout.Button("Sync Spawner Types From Controller", GUILayout.Width(260f))) {
                            SyncSpawnerFromController(sceneController);
                        }
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

            if (_setup == null) {
                EditorGUILayout.HelpBox(
                    "No FlockSetup assigned. The Scene tab can still show the controller inspector,\n" +
                    "but 'Apply Setup → Controller' is disabled.",
                    MessageType.Warning);
            } else {
                if (_setup.InteractionMatrix == null || _setup.GroupNoiseSettings == null) {
                    EditorGUILayout.HelpBox(
                        "Current FlockSetup does not have both InteractionMatrix and GroupNoiseSettings assigned.\n" +
                        "'Apply Setup → Controller' will still copy whatever is currently set (including null).",
                        MessageType.Info);
                }
            }

            if (sceneControllerEditor == null || sceneControllerEditor.target != sceneController) {
                RebuildSceneControllerEditor();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("FlockController Inspector", EditorStyles.miniBoldLabel);

            sceneScroll = EditorGUILayout.BeginScrollView(sceneScroll);
            if (sceneControllerEditor != null) {
                sceneControllerEditor.OnInspectorGUI();
            }
            EditorGUILayout.EndScrollView();
        }

        private void ApplySetupToController(FlockController controller) {
            if (controller == null || _setup == null) {
                return;
            }

            SerializedObject so = new SerializedObject(controller);

            // 1) Fish types from setup
            SerializedProperty fishTypesProp = so.FindProperty("fishTypes");
            if (fishTypesProp != null) {
                var list = _setup.FishTypes;
                int size = (list != null) ? list.Count : 0;
                fishTypesProp.arraySize = size;

                for (int i = 0; i < size; i++) {
                    fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue =
                        list != null ? list[i] : null;
                }
            }

            // 2) Interaction matrix
            SerializedProperty interactionProp = so.FindProperty("interactionMatrix");
            if (interactionProp != null) {
                interactionProp.objectReferenceValue = _setup.InteractionMatrix;
            }

            // 3) Group noise
            SerializedProperty groupNoiseProp = so.FindProperty("groupNoisePattern");
            if (groupNoiseProp != null) {
                groupNoiseProp.objectReferenceValue = _setup.GroupNoiseSettings;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
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

                using (new EditorGUI.DisabledScope(_setup == null || _setup.InteractionMatrix == null)) {
                    if (GUILayout.Button("Ping", GUILayout.Width(60f))) {
                        EditorGUIUtility.PingObject(_setup.InteractionMatrix);
                    }
                }
            }

            // Sync fish types in the matrix from the canonical FishTypes list in setup
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(
                    _setup == null ||
                    _setup.InteractionMatrix == null)) {

                    if (GUILayout.Button("Sync Fish Types From Setup", GUILayout.Width(200f))) {
                        SyncMatrixFishTypesFromSetup();
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

            _interactionsScroll = EditorGUILayout.BeginScrollView(_interactionsScroll);

            if (_interactionMatrixEditor != null) {
                _interactionMatrixEditor.OnInspectorGUI();
            }

            EditorGUILayout.EndScrollView();
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
                    Object currentRef = fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    Object desiredRef = (list != null && i < list.Count) ? list[i] : null;

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

                using (new EditorGUI.DisabledScope(_setup == null)) {
                    if (GUILayout.Button("Ping", GUILayout.Width(60f))) {
                        EditorGUIUtility.PingObject(_setup);
                    }
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
            _setup.PatternAssets = new List<ScriptableObject>(); // ensure list exists
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

        private void OnDisable() {
            DestroySpeciesEditor();
            DestroyInteractionMatrixEditor();
            DestroyGroupNoiseEditor();
            DestroyPatternAssetEditor();    // add this line
            DestroySceneControllerEditor();
        }

        // LEFT COLUMN: group pattern + pattern asset list
        // LEFT COLUMN: group pattern + pattern asset list
        // LEFT COLUMN: group pattern + pattern asset list
        void DrawNoiseListPanel() {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(320f))) {
                _noiseListScroll = EditorGUILayout.BeginScrollView(_noiseListScroll);

                // ---------------- Group Noise Pattern ----------------
                EditorGUILayout.LabelField("Group Noise Pattern", EditorStyles.boldLabel);

                GroupNoisePatternProfile currentProfile =
                    _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                using (new EditorGUILayout.HorizontalScope()) {
                    // Select row
                    GUIStyle selectStyle = (_selectedNoiseIndex == -1)
                        ? EditorStyles.miniButtonMid
                        : EditorStyles.miniButton;

                    string label = currentProfile != null ? currentProfile.name : "<None>";
                    if (GUILayout.Button(label, selectStyle, GUILayout.Width(130f))) {
                        _selectedNoiseIndex = -1;
                        RebuildNoiseEditors();
                    }

                    // Asset field (leave room for built-in object field "X")
                    EditorGUI.BeginChangeCheck();
                    currentProfile = (GroupNoisePatternProfile)EditorGUILayout.ObjectField(
                        GUIContent.none,
                        currentProfile,
                        typeof(GroupNoisePatternProfile),
                        false,
                        GUILayout.Width(170f)); // 130 + 170 = 300 < 320 incl. padding
                    if (EditorGUI.EndChangeCheck()) {
                        _setup.GroupNoiseSettings = currentProfile;
                        EditorUtility.SetDirty(_setup);
                        RebuildNoiseEditors();
                    }
                }

                EditorGUILayout.Space(8f);

                // ---------------- Pattern Assets list ----------------
                EditorGUILayout.LabelField("Pattern Assets", EditorStyles.boldLabel);

                if (_setup.PatternAssets == null) {
                    _setup.PatternAssets = new List<ScriptableObject>();
                    EditorUtility.SetDirty(_setup);
                }

                var patterns = _setup.PatternAssets;
                int removeIndex = -1;

                for (int i = 0; i < patterns.Count; i++) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        ScriptableObject asset = patterns[i];

                        GUIStyle rowStyle = (_selectedNoiseIndex == i)
                            ? EditorStyles.miniButtonMid
                            : EditorStyles.miniButton;

                        string name = asset != null ? asset.name : "<Empty Slot>";
                        if (GUILayout.Button(name, rowStyle, GUILayout.Width(130f))) {
                            _selectedNoiseIndex = i;
                            RebuildNoiseEditors();
                        }

                        EditorGUI.BeginChangeCheck();
                        asset = (ScriptableObject)EditorGUILayout.ObjectField(
                            GUIContent.none,
                            asset,
                            typeof(ScriptableObject),
                            false,
                            GUILayout.Width(150f)); // 130 + 150 + 20 = 300
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

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(2f);

                // Bottom buttons – same idea as Species tab:
                using (new EditorGUILayout.HorizontalScope()) {
                    using (new EditorGUI.DisabledScope(_setup == null)) {
                        if (GUILayout.Button("Create Group Pattern", GUILayout.Width(160f))) {
                            CreateGroupNoisePatternAsset();
                        }
                    }

                    if (GUILayout.Button("Add Pattern Slot", GUILayout.Width(140f))) {
                        patterns.Add(null);
                        _selectedNoiseIndex = patterns.Count - 1;
                        EditorUtility.SetDirty(_setup);
                        RebuildNoiseEditors();
                    }
                }
            }
        }

        // RIGHT COLUMN: inspector for currently selected noise / pattern
        void DrawNoiseDetailPanel() {
            using (new EditorGUILayout.VerticalScope()) {
                EditorGUILayout.LabelField("Selected Noise / Pattern", EditorStyles.boldLabel);

                Object targetAsset = null;
                var groupProfile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                if (_selectedNoiseIndex < 0) {
                    targetAsset = groupProfile;
                } else if (_setup.PatternAssets != null &&
                           _selectedNoiseIndex >= 0 &&
                           _selectedNoiseIndex < _setup.PatternAssets.Count) {
                    targetAsset = _setup.PatternAssets[_selectedNoiseIndex];
                }

                if (targetAsset == null) {
                    EditorGUILayout.HelpBox(
                        "Select a group noise pattern or pattern asset on the left to edit its settings.\n" +
                        "Use 'New' to create a Group Noise Pattern asset or 'Add Pattern Slot' " +
                        "to register additional pattern assets.",
                        MessageType.Info);
                    return;
                }

                UnityEditor.Editor activeEditor = null;

                if (targetAsset == groupProfile) {
                    if (groupNoiseEditor == null || groupNoiseEditor.target != targetAsset) {
                        RebuildGroupNoiseEditor();
                    }
                    activeEditor = groupNoiseEditor;
                } else {
                    if (_patternAssetEditor == null || _patternAssetEditor.target != targetAsset) {
                        DestroyPatternAssetEditor();
                        _patternAssetEditor = UnityEditor.Editor.CreateEditor(targetAsset);
                    }
                    activeEditor = _patternAssetEditor;
                }

                _noiseDetailScroll = EditorGUILayout.BeginScrollView(_noiseDetailScroll);
                if (activeEditor != null) {
                    activeEditor.OnInspectorGUI();
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void SyncSpawnerFromController(FlockController controller) {
            if (controller == null) {
                return;
            }

            var spawner = controller.MainSpawner;
            if (spawner == null) {
                EditorUtility.DisplayDialog(
                    "Flock Spawner",
                    "The selected FlockController has no FlockMainSpawner assigned.",
                    "OK");
                return;
            }

            var types = controller.FishTypes;
            if (types == null || types.Length == 0) {
                EditorUtility.DisplayDialog(
                    "Flock Spawner",
                    "FlockController has no fish types. Apply a FlockSetup first.",
                    "OK");
                return;
            }

            spawner.EditorSyncTypesFrom(types);
            EditorUtility.SetDirty(spawner);
        }

        private void RebuildNoiseEditors() {
            // main group noise editor
            RebuildGroupNoiseEditor();

            // pattern asset inspector
            DestroyPatternAssetEditor();

            if (_setup == null || _setup.PatternAssets == null) {
                return;
            }

            if (_selectedNoiseIndex >= 0 &&
                _selectedNoiseIndex < _setup.PatternAssets.Count) {

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

    }
}
#endif
