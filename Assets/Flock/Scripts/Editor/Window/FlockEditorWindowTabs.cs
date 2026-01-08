#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flock.Scripts.Editor.Window {
    /**
     * <summary>
     * Editor window tab routing and active-tab lifecycle management.
     * </summary>
     */
    public sealed partial class FlockEditorWindow {
        private int activeTabIndex = -1;
        private IFlockEditorTab[] tabs;
        private string[] tabLabels;

        private static string[] BuildTabLabels(IFlockEditorTab[] tabs) {
            string[] labels = new string[tabs.Length];

            for (int index = 0; index < tabs.Length; index += 1) {
                labels[index] = tabs[index].Title;
            }

            return labels;
        }

        private static IFlockEditorTab[] CreateTabs() {
            return new IFlockEditorTab[] {
                new SpeciesTab(),
                new InteractionsTab(),
                new NoisePatternsTab(),
                new SceneSimulationTab()
            };
        }

        private void EnsureTabs() {
            if (tabs != null) {
                return;
            }

            tabs = CreateTabs();
            tabLabels = BuildTabLabels(tabs);
        }

        private void SetActiveTab(int newIndex, bool fireCallbacks) {
            EnsureTabs();

            newIndex = Mathf.Clamp(newIndex, 0, tabs.Length - 1);

            if (fireCallbacks
                && activeTabIndex >= 0
                && activeTabIndex < tabs.Length
                && activeTabIndex != newIndex) {
                tabs[activeTabIndex].OnDeactivated(this);
            }

            _selectedTab = (FlockEditorTabKind)newIndex;
            activeTabIndex = newIndex;

            if (fireCallbacks) {
                tabs[activeTabIndex].OnActivated(this);
            }
        }

        private IFlockEditorTab GetActiveTabOrNull() {
            EnsureTabs();

            if (activeTabIndex < 0 || activeTabIndex >= tabs.Length) {
                return null;
            }

            return tabs[activeTabIndex];
        }

        private void DrawSpeciesTab() {
            using (new EditorGUILayout.HorizontalScope()) {
                DrawSpeciesListPanel();
                DrawSpeciesDetailPanel();
            }
        }

        private void DrawInteractionsTab() {
            DrawInteractionsPanel();
        }

        private void DrawNoisePatternsTab() {
            DrawNoiseModeToolbar();

            using (new EditorGUILayout.HorizontalScope()) {
                DrawNoiseListPanel();
                DrawNoiseDetailPanel();
            }

            HandleGroupNoiseObjectPicker();
        }

        private void DrawSceneSimulationTab() {
            DrawSceneSimulationPanel();
        }

        private enum FlockEditorTabKind {
            Species = 0,
            Interactions = 1,
            NoisePatterns = 2,
            SceneSimulation = 3
        }

        private interface IFlockEditorTab {
            string Title { get; }

            void OnActivated(FlockEditorWindow window);

            void OnDeactivated(FlockEditorWindow window);

            bool OnEditorUpdate(FlockEditorWindow window);

            void Draw(FlockEditorWindow window);
        }

        private abstract class FlockEditorTabBase : IFlockEditorTab {
            public abstract string Title { get; }

            public virtual void OnActivated(FlockEditorWindow window) { }

            public virtual void OnDeactivated(FlockEditorWindow window) { }

            public virtual bool OnEditorUpdate(FlockEditorWindow window) {
                return false;
            }

            public abstract void Draw(FlockEditorWindow window);
        }

        private sealed class SpeciesTab : FlockEditorTabBase {
            public override string Title => "Species";

            public override void Draw(FlockEditorWindow window) {
                window.DrawSpeciesTab();
            }
        }

        private sealed class InteractionsTab : FlockEditorTabBase {
            public override string Title => "Interactions";

            public override void Draw(FlockEditorWindow window) {
                window.DrawInteractionsTab();
            }
        }

        private sealed class NoisePatternsTab : FlockEditorTabBase {
            public override string Title => "Noise / Patterns";

            public override void Draw(FlockEditorWindow window) {
                window.DrawNoisePatternsTab();
            }
        }

        private sealed class SceneSimulationTab : FlockEditorTabBase {
            public override string Title => "Scene / Simulation";

            private double _nextAutoSyncTime = 0.0;

            public override void OnActivated(FlockEditorWindow window) {
                _nextAutoSyncTime = 0.0;

                if (window._setup == null || window.sceneController == null) {
                    return;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode) {
                    return;
                }

                if (window.TryAutoSyncSetupToController(window.sceneController)) {
                    window.Repaint();
                }
            }

            public override bool OnEditorUpdate(FlockEditorWindow window) {
                if (window._setup == null || window.sceneController == null) {
                    return false;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode) {
                    return false;
                }

                double now = EditorApplication.timeSinceStartup;
                if (now < _nextAutoSyncTime) {
                    return false;
                }

                _nextAutoSyncTime = now + EditorUI.SceneAutoSyncIntervalSeconds;
                return window.TryAutoSyncSetupToController(window.sceneController);
            }

            public override void Draw(FlockEditorWindow window) {
                window.DrawSceneSimulationTab();
            }
        }
    }
}
#endif
