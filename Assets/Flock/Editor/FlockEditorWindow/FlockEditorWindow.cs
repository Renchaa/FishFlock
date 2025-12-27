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
        private int _selectedTab = 0; // 0 = Species, 1 = Interactions, 2 = Noise/Patterns, 3 = Scene/Simulation

        [SerializeField]
        private FlockController sceneController;

        [SerializeField]
        private int _selectedSpeciesIndex = -1;

        [SerializeField]
        private int _speciesInspectorMode = 0;

        [SerializeField]
        private int _selectedNoiseIndex = -1; // -1 = Group Noise Pattern, >=0 = PatternAssets[i]

        [SerializeField]
        private int _noiseInspectorMode = 0;  // 0 = Group Noise, 1 = Pattern Assets

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


        private void OnEnable() {
            EnsureTabs();

            activeTabIndex = Mathf.Clamp(_selectedTab, 0, tabs.Length - 1);
            SetActiveTab(activeTabIndex, fireCallbacks: false);

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

        private void OnGUI() {
            DrawSetupSelector();

            if (_setup == null) {
                DrawNoSetupHelp();
                return;
            }

            EnsureTabs();

            EditorGUILayout.Space();

            int newTab = GUILayout.Toolbar(
                Mathf.Clamp(_selectedTab, 0, tabs.Length - 1),
                tabLabels);

            if (newTab != _selectedTab) {
                SetActiveTab(newTab, fireCallbacks: true);
            } else if (activeTabIndex != _selectedTab) {
                SetActiveTab(_selectedTab, fireCallbacks: false);
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
    }
}
#endif
