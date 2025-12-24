#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {

        // Active runtime tab index (separate from serialized _selectedTab)
        int _activeTabIndex = -1;

        IFlockEditorTab[] _tabs;
        string[] _tabLabels;

        interface IFlockEditorTab {
            string Title { get; }

            // Called when the tab becomes the active tab
            void OnActivated(FlockEditorWindow w);

            // Called when the tab stops being the active tab
            void OnDeactivated(FlockEditorWindow w);

            // Called from EditorApplication.update, ONLY for the active tab.
            // Return true if the window should repaint.
            bool OnEditorUpdate(FlockEditorWindow w);

            // Draw UI
            void Draw(FlockEditorWindow w);
        }

        abstract class FlockEditorTabBase : IFlockEditorTab {
            public abstract string Title { get; }
            public virtual void OnActivated(FlockEditorWindow w) { }
            public virtual void OnDeactivated(FlockEditorWindow w) { }
            public virtual bool OnEditorUpdate(FlockEditorWindow w) => false;
            public abstract void Draw(FlockEditorWindow w);
        }

        void EnsureTabs() {
            if (_tabs != null) return;

            _tabs = new IFlockEditorTab[] {
                new SpeciesTab(),
                new InteractionsTab(),
                new NoisePatternsTab(),
                new SceneSimulationTab()
            };

            _tabLabels = new string[_tabs.Length];
            for (int i = 0; i < _tabs.Length; i++) {
                _tabLabels[i] = _tabs[i].Title;
            }
        }

        void SetActiveTab(int newIndex, bool fireCallbacks) {
            EnsureTabs();

            newIndex = Mathf.Clamp(newIndex, 0, _tabs.Length - 1);

            // Deactivate previous
            if (fireCallbacks && _activeTabIndex >= 0 && _activeTabIndex < _tabs.Length && _activeTabIndex != newIndex) {
                _tabs[_activeTabIndex].OnDeactivated(this);
            }

            _selectedTab = newIndex;
            _activeTabIndex = newIndex;

            // Activate new
            if (fireCallbacks) {
                _tabs[_activeTabIndex].OnActivated(this);
            }
        }

        IFlockEditorTab GetActiveTabOrNull() {
            EnsureTabs();
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Length) return null;
            return _tabs[_activeTabIndex];
        }

        // Wrappers so we keep ALL your existing per-tab code as-is.
        void DrawSpeciesTab() {
            using (new EditorGUILayout.HorizontalScope()) {
                DrawSpeciesListPanel();
                DrawSpeciesDetailPanel();
            }
        }

        void DrawInteractionsTab() {
            DrawInteractionsPanel();
        }

        void DrawNoisePatternsTab() {
            DrawNoiseModeToolbar();
            using (new EditorGUILayout.HorizontalScope()) {
                DrawNoiseListPanel();
                DrawNoiseDetailPanel();
            }
            HandleGroupNoiseObjectPicker();
        }

        void DrawSceneSimulationTab() {
            DrawSceneSimulationPanel();
        }

        sealed class SpeciesTab : FlockEditorTabBase {
            public override string Title => "Species";
            public override void Draw(FlockEditorWindow w) => w.DrawSpeciesTab();
        }

        sealed class InteractionsTab : FlockEditorTabBase {
            public override string Title => "Interactions";
            public override void Draw(FlockEditorWindow w) => w.DrawInteractionsTab();
        }

        sealed class NoisePatternsTab : FlockEditorTabBase {
            public override string Title => "Noise / Patterns";
            public override void Draw(FlockEditorWindow w) => w.DrawNoisePatternsTab();
        }

        sealed class SceneSimulationTab : FlockEditorTabBase {
            public override string Title => "Scene / Simulation";

            double _nextAutoSyncTime = 0.0;

            public override void OnActivated(FlockEditorWindow w) {
                // force immediate sync attempt on enter (if possible)
                _nextAutoSyncTime = 0.0;

                if (w._setup == null || w.sceneController == null) {
                    return;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode) {
                    return;
                }

                if (w.TryAutoSyncSetupToController(w.sceneController)) {
                    w.Repaint();
                }
            }

            public override bool OnEditorUpdate(FlockEditorWindow w) {
                if (w._setup == null || w.sceneController == null) {
                    return false;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode) {
                    return false;
                }

                double now = EditorApplication.timeSinceStartup;
                if (now < _nextAutoSyncTime) {
                    return false;
                }

                _nextAutoSyncTime = now + FlockEditorUI.SceneAutoSyncIntervalSeconds;

                // If sync changed anything -> repaint.
                return w.TryAutoSyncSetupToController(w.sceneController);
            }

            public override void Draw(FlockEditorWindow w) => w.DrawSceneSimulationTab();
        }
    }
}
#endif
