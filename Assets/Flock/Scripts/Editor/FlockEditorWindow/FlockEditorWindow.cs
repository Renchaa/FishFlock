#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace Flock.Editor {
    /**
     * <summary>
     * Top-level flock editor window.
     * </summary>
     */
    public sealed partial class FlockEditorWindow : EditorWindow {
        // Serialized window state
        [SerializeField]
        private FlockSetup _setup;


        [SerializeField]
        private FlockController sceneController;

        [SerializeField]
        private int _selectedSpeciesIndex = -1;

        [SerializeField]
        private int _speciesInspectorModeIndex = 0;

        [SerializeField]
        private int _selectedNoiseIndex = -1; // -1 = Group Noise Pattern, >=0 = PatternAssets[i]

        [SerializeField]
        private FlockEditorTabKind _selectedTab = FlockEditorTabKind.Species;

        [SerializeField]
        private NoiseInspectorMode _noiseInspectorMode = NoiseInspectorMode.GroupNoise;


        [SerializeField]
        private FlockSetupControllerSync _sync = new FlockSetupControllerSync();

        // Scroll
        private Vector2 _speciesListScroll;
        private Vector2 _detailScroll;
        private Vector2 _noiseListScroll;
        private Vector2 _noiseDetailScroll;
        private Vector2 _interactionsScroll;

        // Cached inspectors
        private UnityEditor.Editor _presetEditor;
        private UnityEditor.Editor _behaviourEditor;
        private UnityEditor.Editor _interactionMatrixEditor;
        private UnityEditor.Editor _patternAssetEditor;
        private UnityEditor.Editor groupNoiseEditor;
        private UnityEditor.Editor sceneControllerEditor;

        // Scene tab state
        private Vector2 sceneScroll;

        // Pattern dropdown
        private AdvancedDropdownState _createPatternDropdownState;
        private CreatePatternDropdown _createPatternDropdown;

        private double _nextSceneAutoSyncTime = 0.0d;

        private SpeciesInspectorMode CurrentSpeciesInspectorMode {
            get => (SpeciesInspectorMode)_speciesInspectorModeIndex;
            set => _speciesInspectorModeIndex = (int)value;
        }

        private void OnEnable() {
            EnsureTabs();

            int selectedTabIndex = Mathf.Clamp((int)_selectedTab, 0, tabs.Length - 1);
            activeTabIndex = selectedTabIndex;
            SetActiveTab(selectedTabIndex, fireCallbacks: false);

            EditorApplication.update += OnEditorUpdate;

            ResetSceneSyncState();
        }

        private void OnDisable() {
            EditorApplication.update -= OnEditorUpdate;

            var active = GetActiveTabOrNull();
            active?.OnDeactivated(this);

            DestroySpeciesEditor();
            DestroyInteractionMatrixEditor();
            DestroyGroupNoiseEditor();
            DestroyPatternAssetEditor();
            DestroySceneControllerEditor();
        }

        // REPLACE OnGUI() WITH:
        private void OnGUI() {
            DrawSetupSelector();

            if (_setup == null) {
                DrawNoSetupHelp();
                return;
            }

            EnsureTabs();

            EditorGUILayout.Space();

            int selectedTabIndex = Mathf.Clamp((int)_selectedTab, 0, tabs.Length - 1);
            int newTabIndex = GUILayout.Toolbar(selectedTabIndex, tabLabels);

            if (newTabIndex != (int)_selectedTab) {
                SetActiveTab(newTabIndex, fireCallbacks: true);
            } else if (activeTabIndex != (int)_selectedTab) {
                SetActiveTab((int)_selectedTab, fireCallbacks: false);
            }

            EditorGUILayout.Space();

            tabs[activeTabIndex].Draw(this);
        }

        [MenuItem("Window/Flock/Flock Editor")]
        public static void Open() {
            GetWindow<FlockEditorWindow>("Flock Editor");
        }

        private static void DrawNoSetupHelp() {
            EditorGUILayout.HelpBox(
                "Assign or create a FlockSetup asset.\n\n" +
                "This asset is the central config that holds your species, " +
                "interaction matrix, and noise/pattern assets.",
                MessageType.Info);
        }

        private enum NoiseInspectorMode {
            GroupNoise = 0,
            PatternAssets = 1
        }

        private enum SpeciesInspectorMode {
            Preset = 0,
            Behaviour = 1
        }
    }
}
#endif
