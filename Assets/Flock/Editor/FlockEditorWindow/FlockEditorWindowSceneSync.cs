#if UNITY_EDITOR
using Flock.Runtime;
using UnityEngine;

namespace Flock.Editor {
    /**
     * <summary>
     * Applies Setup ↔ Controller synchronization results to the editor window state.
     * Handles editor-side consequences such as clamping selection indices and rebuilding
     * cached inspectors/editors when the underlying data model changes.
     * </summary>
     */
    public sealed partial class FlockEditorWindow {
        private void ResetSceneSyncState() {
            _sync.Reset();
        }

        private bool TryAutoSyncSetupToController(FlockController controller) {
            FlockSetupControllerSync.SyncResult syncResult = _sync.SyncTwoWay(
                _setup,
                controller,
                afterControllerWrite: TrySyncSpawnerFromController);

            ApplySyncResultToWindow(syncResult);
            return syncResult.AnyChange;
        }

        private void ApplySyncResultToWindow(in FlockSetupControllerSync.SyncResult syncResult) {
            if (!syncResult.AnyChange) {
                return;
            }

            ClampEditorSelections();

            if (syncResult.FishTypesChanged) {
                ApplyFishTypeChangeEffects();
            }

            if (syncResult.PatternAssetsChanged || syncResult.GroupNoiseChanged) {
                RebuildNoiseEditors();
            }

            if (syncResult.MatrixChanged) {
                RebuildInteractionMatrixEditor();
            }
        }

        private void ApplyFishTypeChangeEffects() {
            DestroySpeciesEditor();
            RebuildSpeciesEditor();

            SyncMatrixFishTypesFromSetup();

            if (_interactionMatrixEditor != null) {
                RebuildInteractionMatrixEditor();
            }
        }

        private void ClampEditorSelections() {
            if (_setup == null) {
                ResetEditorSelections();
                return;
            }

            ClampSpeciesSelection();
            ClampNoiseSelection();
        }

        private void ResetEditorSelections() {
            _selectedSpeciesIndex = -1;
            _selectedNoiseIndex = -1;
        }

        private void ClampSpeciesSelection() {
            int fishTypeCount = _setup.FishTypes?.Count ?? 0;

            if (fishTypeCount <= 0) {
                _selectedSpeciesIndex = -1;
                return;
            }

            _selectedSpeciesIndex = Mathf.Clamp(_selectedSpeciesIndex, 0, fishTypeCount - 1);
        }

        private void ClampNoiseSelection() {
            if (_noiseInspectorMode != NoiseInspectorMode.PatternAssets) {
                _selectedNoiseIndex = -1;
                return;
            }

            int patternAssetCount = _setup.PatternAssets?.Count ?? 0;

            if (patternAssetCount <= 0) {
                _selectedNoiseIndex = -1;
                return;
            }

            _selectedNoiseIndex = Mathf.Clamp(_selectedNoiseIndex, 0, patternAssetCount - 1);
        }
    }
}
#endif
