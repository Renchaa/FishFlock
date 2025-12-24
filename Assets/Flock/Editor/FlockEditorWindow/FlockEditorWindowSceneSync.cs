#if UNITY_EDITOR
using Flock.Runtime;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        void ResetSceneSyncState() {
            _sync.Reset();
        }


        private bool TryAutoSyncSetupToController(FlockController controller) {
            var r = _sync.SyncTwoWay(
                _setup,
                controller,
                afterControllerWrite: TrySyncSpawnerFromController);

            ApplySyncResultToWindow(r);
            return r.AnyChange;
        }

        private bool TryPullControllerRefsIntoSetup(FlockController controller) {
            var r = _sync.PullControllerIntoSetup(_setup, controller);

            ApplySyncResultToWindow(r);
            return r.AnyChange;
        }

        void ApplySyncResultToWindow(in FlockSetupControllerSync.SyncResult r) {
            if (!r.AnyChange) return;

            ClampEditorSelections();

            if (r.FishTypesChanged) {
                // Species tab must refresh cached editors
                DestroySpeciesEditor();
                RebuildSpeciesEditor();

                // Interactions depends on fish ordering; keep matrix fishTypes in sync even if tab is not visited.
                SyncMatrixFishTypesFromSetup();

                // If Interactions editor is already alive, rebuild so it reflects updated size immediately.
                if (_interactionMatrixEditor != null) {
                    RebuildInteractionMatrixEditor();
                }
            }

            if (r.PatternAssetsChanged || r.GroupNoiseChanged) {
                RebuildNoiseEditors();
            }

            if (r.MatrixChanged) {
                RebuildInteractionMatrixEditor();
            }
        }

        void ClampEditorSelections() {
            if (_setup == null) {
                _selectedSpeciesIndex = -1;
                _selectedNoiseIndex = -1;
                return;
            }

            // Species clamp
            int fishCount = _setup.FishTypes != null ? _setup.FishTypes.Count : 0;
            if (fishCount <= 0) {
                _selectedSpeciesIndex = -1;
            } else {
                _selectedSpeciesIndex = Mathf.Clamp(_selectedSpeciesIndex, 0, fishCount - 1);
            }

            // Noise clamp (only when in Pattern Assets mode)
            if (_noiseInspectorMode == 1) {
                int patternCount = _setup.PatternAssets != null ? _setup.PatternAssets.Count : 0;
                if (patternCount <= 0) {
                    _selectedNoiseIndex = -1;
                } else {
                    _selectedNoiseIndex = Mathf.Clamp(_selectedNoiseIndex, 0, patternCount - 1);
                }
            } else {
                _selectedNoiseIndex = -1; // group noise mode
            }
        }

    }
}
#endif
