#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /**
     * <summary>
     * Synchronizes reference data between a FlockSetup asset and a FlockController instance.
     * </summary>
     */
    [Serializable]
    public sealed class FlockSetupControllerSync {
        [SerializeField] private int[] _lastSyncedSetupFishIds;
        [SerializeField] private int[] _lastSyncedControllerFishIds;
        [SerializeField] private int[] _lastSyncedSetupPatternIds;
        [SerializeField] private int[] _lastSyncedControllerPatternIds;

        [SerializeField] private int _lastSetupMatrixId;
        [SerializeField] private int _lastControllerMatrixId;
        [SerializeField] private int _lastSetupNoiseId;
        [SerializeField] private int _lastControllerNoiseId;

        [SerializeField] private SyncSource _lastFishSyncSource = SyncSource.None;
        [SerializeField] private SyncSource _lastPatternSyncSource = SyncSource.None;
        [SerializeField] private SyncSource _lastMatrixSyncSource = SyncSource.None;
        [SerializeField] private SyncSource _lastNoiseSyncSource = SyncSource.None;

        private bool _isSyncing;

        /**
         * <summary>
         * Clears all recorded baselines and conflict-resolution state.
         * </summary>
         */
        public void Reset() {
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

        /**
         * <summary>
         * Performs a two-way synchronization between a setup asset and a controller instance.
         * </summary>
         * <param name="setup">The setup asset to synchronize.</param>
         * <param name="controller">The controller instance to synchronize.</param>
         * <param name="afterControllerWrite">Optional callback invoked after controller writes are applied.</param>
         * <returns>A result describing which tracks changed.</returns>
         */
        public SyncResult SyncTwoWay(
            FlockSetup setup,
            FlockController controller,
            Action<FlockController> afterControllerWrite = null) {
            SyncResult result = new SyncResult();

            if (!TryBeginSync(setup, controller)) {
                return result;
            }

            try {
                EnsureSetupListsInitialized(setup);

                SyncContext context = CreateSyncContext(setup, controller);
                ChangeSet changes = DetectChanges(context);
                WinnerSet winners = DetermineTrackWinners(changes);

                ApplyFishTypesSync(ref context, winners.FishWinner, ref result);
                ApplyPatternAssetsSync(ref context, winners.PatternWinner, ref result);
                ApplyInteractionMatrixSync(ref context, winners.MatrixWinner, ref result);
                ApplyGroupNoiseSync(ref context, winners.NoiseWinner, ref result);

                ApplyControllerWritesIfDirty(ref context, afterControllerWrite);
                UpdateBaselines(context, winners);

                return result;
            } finally {
                EndSync();
            }
        }

        /**
         * <summary>
         * Pulls controller references into the setup asset.
         * </summary>
         * <param name="setup">The setup asset to write into.</param>
         * <param name="controller">The controller to read from.</param>
         * <returns>A result describing which tracks changed.</returns>
         */
        public SyncResult PullControllerIntoSetup(FlockSetup setup, FlockController controller) {
            SyncResult result = new SyncResult();

            if (controller == null || setup == null) {
                return result;
            }

            SerializedObject controllerSerializedObject = new SerializedObject(controller);
            controllerSerializedObject.UpdateIfRequiredOrScript();

            SyncFishTypesFromController(controllerSerializedObject, setup, ref result);
            SyncInteractionMatrixFromController(controllerSerializedObject, setup, ref result);
            SyncGroupNoiseFromController(controllerSerializedObject, setup, ref result);
            SyncPatternAssetsFromController(controllerSerializedObject, setup, ref result);

            if (result.AnyChange) {
                EditorUtility.SetDirty(setup);
            }

            return result;
        }

        private static void EnsureSetupListsInitialized(FlockSetup setup) {
            setup.FishTypes ??= new List<FishTypePreset>();
            setup.PatternAssets ??= new List<FlockLayer3PatternProfile>();
        }

        private static SyncContext CreateSyncContext(FlockSetup setup, FlockController controller) {
            ControllerSerializedState controllerSerializedState = CreateControllerSerializedState(controller);

            return new SyncContext {
                Setup = setup,
                Controller = controller,
                ControllerSerializedState = controllerSerializedState,
                SetupSnapshot = CaptureSetupSnapshot(setup),
                ControllerSnapshot = CaptureControllerSnapshot(controller, controllerSerializedState),
                IsControllerDirty = false
            };
        }

        private static ControllerSerializedState CreateControllerSerializedState(FlockController controller) {
            SerializedObject controllerSerializedObject = new SerializedObject(controller);

            return new ControllerSerializedState {
                SerializedObject = controllerSerializedObject,
                InteractionMatrixProperty = controllerSerializedObject.FindProperty("interactionMatrix"),
                GroupNoiseProperty = controllerSerializedObject.FindProperty("groupNoisePattern")
            };
        }

        private static SetupSnapshot CaptureSetupSnapshot(FlockSetup setup) {
            return new SetupSnapshot {
                FishIds = BuildInstanceIdArray(setup.FishTypes),
                PatternIds = BuildInstanceIdArray(setup.PatternAssets),
                MatrixId = setup.InteractionMatrix != null ? setup.InteractionMatrix.GetInstanceID() : 0,
                NoiseId = setup.GroupNoiseSettings != null ? setup.GroupNoiseSettings.GetInstanceID() : 0
            };
        }

        private static ControllerSnapshot CaptureControllerSnapshot(
            FlockController controller,
            in ControllerSerializedState controllerSerializedState) {
            return new ControllerSnapshot {
                FishIds = BuildInstanceIdArray(controller.FishTypes),
                PatternIds = BuildInstanceIdArray(controller.Layer3Patterns),
                MatrixId = GetInstanceId(controllerSerializedState.InteractionMatrixProperty),
                NoiseId = GetInstanceId(controllerSerializedState.GroupNoiseProperty)
            };
        }

        private static int GetInstanceId(SerializedProperty property) {
            UnityEngine.Object unityObject = property != null ? property.objectReferenceValue : null;
            return unityObject != null ? unityObject.GetInstanceID() : 0;
        }

        private static void ApplyControllerWritesIfDirty(ref SyncContext context, Action<FlockController> afterControllerWrite) {
            if (!context.IsControllerDirty) {
                return;
            }

            context.ControllerSerializedState.SerializedObject.ApplyModifiedProperties();
            context.ControllerSerializedState.SerializedObject.UpdateIfRequiredOrScript();
            EditorUtility.SetDirty(context.Controller);

            afterControllerWrite?.Invoke(context.Controller);
            RefreshControllerState(ref context);
        }

        private static void RefreshControllerState(ref SyncContext context) {
            context.ControllerSerializedState.InteractionMatrixProperty =
                context.ControllerSerializedState.SerializedObject.FindProperty("interactionMatrix");
            context.ControllerSerializedState.GroupNoiseProperty =
                context.ControllerSerializedState.SerializedObject.FindProperty("groupNoisePattern");

            context.ControllerSnapshot = CaptureControllerSnapshot(context.Controller, context.ControllerSerializedState);
        }

        private static void WriteFishTypesFromSetupToController(ref SyncContext context, ref SyncResult result) {
            SerializedProperty fishTypesProperty = context.ControllerSerializedState.SerializedObject.FindProperty("fishTypes");
            if (fishTypesProperty == null) {
                return;
            }

            List<FishTypePreset> fishTypes = context.Setup.FishTypes;
            fishTypesProperty.arraySize = fishTypes.Count;

            for (int index = 0; index < fishTypes.Count; index += 1) {
                fishTypesProperty.GetArrayElementAtIndex(index).objectReferenceValue = fishTypes[index];
            }

            context.IsControllerDirty = true;
            result.AnyChange = true;
            result.FishTypesChanged = true;
        }

        private static void WriteFishTypesFromControllerToSetup(ref SyncContext context, ref SyncResult result) {
            Undo.RecordObject(context.Setup, "Sync Setup Fish Types From Controller");
            context.Setup.FishTypes.Clear();

            FishTypePreset[] controllerFishTypes = context.Controller.FishTypes;
            if (controllerFishTypes != null && controllerFishTypes.Length > 0) {
                context.Setup.FishTypes.AddRange(controllerFishTypes);
            }

            EditorUtility.SetDirty(context.Setup);
            result.AnyChange = true;
            result.FishTypesChanged = true;

            context.SetupSnapshot = CaptureSetupSnapshot(context.Setup);
        }

        private static void WritePatternAssetsFromSetupToController(ref SyncContext context, ref SyncResult result) {
            SerializedProperty layer3PatternsProperty = context.ControllerSerializedState.SerializedObject.FindProperty("layer3Patterns");
            if (layer3PatternsProperty == null) {
                return;
            }

            List<FlockLayer3PatternProfile> patterns = context.Setup.PatternAssets;
            layer3PatternsProperty.arraySize = patterns.Count;

            for (int index = 0; index < patterns.Count; index += 1) {
                layer3PatternsProperty.GetArrayElementAtIndex(index).objectReferenceValue = patterns[index];
            }

            context.IsControllerDirty = true;
            result.AnyChange = true;
            result.PatternAssetsChanged = true;
        }

        private static void WritePatternAssetsFromControllerToSetup(ref SyncContext context, ref SyncResult result) {
            Undo.RecordObject(context.Setup, "Sync Setup Patterns From Controller");
            context.Setup.PatternAssets.Clear();

            FlockLayer3PatternProfile[] controllerPatterns = context.Controller.Layer3Patterns;
            if (controllerPatterns != null && controllerPatterns.Length > 0) {
                context.Setup.PatternAssets.AddRange(controllerPatterns);
            }

            EditorUtility.SetDirty(context.Setup);
            result.AnyChange = true;
            result.PatternAssetsChanged = true;

            context.SetupSnapshot = CaptureSetupSnapshot(context.Setup);
        }

        private static void WriteInteractionMatrixFromSetupToController(ref SyncContext context, ref SyncResult result) {
            if (context.ControllerSerializedState.InteractionMatrixProperty == null) {
                return;
            }

            context.ControllerSerializedState.InteractionMatrixProperty.objectReferenceValue = context.Setup.InteractionMatrix;
            context.IsControllerDirty = true;

            result.AnyChange = true;
            result.MatrixChanged = true;
        }

        private static void WriteInteractionMatrixFromControllerToSetup(ref SyncContext context, ref SyncResult result) {
            if (context.ControllerSerializedState.InteractionMatrixProperty == null) {
                return;
            }

            Undo.RecordObject(context.Setup, "Sync Setup Interaction Matrix From Controller");
            FishInteractionMatrix matrixFromController =
                context.ControllerSerializedState.InteractionMatrixProperty.objectReferenceValue as FishInteractionMatrix;

            context.Setup.InteractionMatrix = matrixFromController;
            EditorUtility.SetDirty(context.Setup);

            result.AnyChange = true;
            result.MatrixChanged = true;

            context.SetupSnapshot = CaptureSetupSnapshot(context.Setup);
        }

        private static void WriteGroupNoiseFromSetupToController(ref SyncContext context, ref SyncResult result) {
            if (context.ControllerSerializedState.GroupNoiseProperty == null) {
                return;
            }

            context.ControllerSerializedState.GroupNoiseProperty.objectReferenceValue =
                context.Setup.GroupNoiseSettings as GroupNoisePatternProfile;

            context.IsControllerDirty = true;
            result.AnyChange = true;
            result.GroupNoiseChanged = true;
        }

        private static void WriteGroupNoiseFromControllerToSetup(ref SyncContext context, ref SyncResult result) {
            if (context.ControllerSerializedState.GroupNoiseProperty == null) {
                return;
            }

            Undo.RecordObject(context.Setup, "Sync Setup Group Noise From Controller");
            GroupNoisePatternProfile noiseFromController =
                context.ControllerSerializedState.GroupNoiseProperty.objectReferenceValue as GroupNoisePatternProfile;

            context.Setup.GroupNoiseSettings = noiseFromController;
            EditorUtility.SetDirty(context.Setup);

            result.AnyChange = true;
            result.GroupNoiseChanged = true;

            context.SetupSnapshot = CaptureSetupSnapshot(context.Setup);
        }

        private static void SyncFishTypesFromController(SerializedObject controllerSerializedObject, FlockSetup setup, ref SyncResult result) {
            SerializedProperty controllerFishTypesProperty = controllerSerializedObject.FindProperty("fishTypes");
            if (controllerFishTypesProperty == null) {
                return;
            }

            int count = controllerFishTypesProperty.arraySize;
            setup.FishTypes ??= new List<FishTypePreset>(count);

            if (!HasFishTypesMismatch(setup.FishTypes, controllerFishTypesProperty, count)) {
                return;
            }

            ApplyFishTypesFromController(setup, controllerFishTypesProperty, count);
            result.AnyChange = true;
            result.FishTypesChanged = true;
        }

        private static void ApplyFishTypesFromController(FlockSetup setup, SerializedProperty controllerFishTypesProperty, int count) {
            Undo.RecordObject(setup, "Sync Setup Fish Types From Controller");

            setup.FishTypes.Clear();
            setup.FishTypes.Capacity = Mathf.Max(setup.FishTypes.Capacity, count);

            for (int index = 0; index < count; index += 1) {
                FishTypePreset fromController =
                    (FishTypePreset)controllerFishTypesProperty.GetArrayElementAtIndex(index).objectReferenceValue;

                setup.FishTypes.Add(fromController);
            }
        }

        private static bool HasFishTypesMismatch(List<FishTypePreset> fishTypes, SerializedProperty controllerFishTypesProperty, int count) {
            if (fishTypes.Count != count) {
                return true;
            }

            for (int index = 0; index < count; index += 1) {
                FishTypePreset fromController =
                    (FishTypePreset)controllerFishTypesProperty.GetArrayElementAtIndex(index).objectReferenceValue;

                if (fishTypes[index] != fromController) {
                    return true;
                }
            }

            return false;
        }

        private static void SyncInteractionMatrixFromController(SerializedObject controllerSerializedObject, FlockSetup setup, ref SyncResult result) {
            FishInteractionMatrix controllerMatrix =
                controllerSerializedObject.FindProperty("interactionMatrix")?.objectReferenceValue as FishInteractionMatrix;

            if (setup.InteractionMatrix == controllerMatrix) {
                return;
            }

            Undo.RecordObject(setup, "Sync Setup Interaction Matrix");
            setup.InteractionMatrix = controllerMatrix;

            result.AnyChange = true;
            result.MatrixChanged = true;
        }

        private static void SyncGroupNoiseFromController(SerializedObject controllerSerializedObject, FlockSetup setup, ref SyncResult result) {
            GroupNoisePatternProfile controllerNoise =
                controllerSerializedObject.FindProperty("groupNoisePattern")?.objectReferenceValue as GroupNoisePatternProfile;

            if (setup.GroupNoiseSettings == controllerNoise) {
                return;
            }

            Undo.RecordObject(setup, "Sync Setup Group Noise");
            setup.GroupNoiseSettings = controllerNoise;

            result.AnyChange = true;
            result.GroupNoiseChanged = true;
        }

        private static void SyncPatternAssetsFromController(SerializedObject controllerSerializedObject, FlockSetup setup, ref SyncResult result) {
            SerializedProperty controllerLayer3PatternsProperty = controllerSerializedObject.FindProperty("layer3Patterns");
            if (controllerLayer3PatternsProperty == null) {
                return;
            }

            int count = controllerLayer3PatternsProperty.arraySize;
            setup.PatternAssets ??= new List<FlockLayer3PatternProfile>(count);

            if (!HasPatternAssetsMismatch(setup.PatternAssets, controllerLayer3PatternsProperty, count)) {
                return;
            }

            ApplyPatternAssetsFromController(setup, controllerLayer3PatternsProperty, count);
            result.AnyChange = true;
            result.PatternAssetsChanged = true;
        }

        private static void ApplyPatternAssetsFromController(FlockSetup setup, SerializedProperty controllerLayer3PatternsProperty, int count) {
            Undo.RecordObject(setup, "Sync Setup Patterns From Controller");

            setup.PatternAssets.Clear();
            setup.PatternAssets.Capacity = Mathf.Max(setup.PatternAssets.Capacity, count);

            for (int index = 0; index < count; index += 1) {
                FlockLayer3PatternProfile fromController =
                    (FlockLayer3PatternProfile)controllerLayer3PatternsProperty.GetArrayElementAtIndex(index).objectReferenceValue;

                setup.PatternAssets.Add(fromController);
            }
        }

        private static bool HasPatternAssetsMismatch(
            List<FlockLayer3PatternProfile> patternAssets,
            SerializedProperty controllerLayer3PatternsProperty,
            int count) {
            if (patternAssets.Count != count) {
                return true;
            }

            for (int index = 0; index < count; index += 1) {
                FlockLayer3PatternProfile fromController =
                    (FlockLayer3PatternProfile)controllerLayer3PatternsProperty.GetArrayElementAtIndex(index).objectReferenceValue;

                if (patternAssets[index] != fromController) {
                    return true;
                }
            }

            return false;
        }

        private static bool IntArraysEqual(int[] first, int[] second) {
            if (ReferenceEquals(first, second)) { return true; }
            if (first == null || second == null) { return false; }
            if (first.Length != second.Length) { return false; }

            for (int index = 0; index < first.Length; index += 1) {
                if (first[index] != second[index]) { return false; }
            }

            return true;
        }

        private static int[] BuildInstanceIdArray<T>(List<T> list) where T : UnityEngine.Object {
            if (list == null || list.Count == 0) {
                return Array.Empty<int>();
            }

            int[] instanceIds = new int[list.Count];
            for (int index = 0; index < list.Count; index += 1) {
                T unityObject = list[index];
                instanceIds[index] = unityObject != null ? unityObject.GetInstanceID() : 0;
            }

            return instanceIds;
        }

        private static int[] BuildInstanceIdArray<T>(T[] array) where T : UnityEngine.Object {
            if (array == null || array.Length == 0) {
                return Array.Empty<int>();
            }

            int[] instanceIds = new int[array.Length];
            for (int index = 0; index < array.Length; index += 1) {
                T unityObject = array[index];
                instanceIds[index] = unityObject != null ? unityObject.GetInstanceID() : 0;
            }

            return instanceIds;
        }

        private static SyncSource DetermineWinner(bool setupChanged, bool controllerChanged, SyncSource lastSource) {
            if (!setupChanged && !controllerChanged) {
                return SyncSource.None;
            }

            if (setupChanged && !controllerChanged) {
                return SyncSource.Setup;
            }

            if (!setupChanged && controllerChanged) {
                return SyncSource.Controller;
            }

            if (lastSource == SyncSource.Controller) {
                return SyncSource.Controller;
            }

            return SyncSource.Setup;
        }

        private bool TryBeginSync(FlockSetup setup, FlockController controller) {
            if (_isSyncing || controller == null || setup == null) {
                return false;
            }

            _isSyncing = true;
            return true;
        }

        private void EndSync() {
            _isSyncing = false;
        }

        private ChangeSet DetectChanges(in SyncContext context) {
            return new ChangeSet {
                SetupFishChanged = !IntArraysEqual(context.SetupSnapshot.FishIds, _lastSyncedSetupFishIds),
                ControllerFishChanged = !IntArraysEqual(context.ControllerSnapshot.FishIds, _lastSyncedControllerFishIds),

                SetupPatternChanged = !IntArraysEqual(context.SetupSnapshot.PatternIds, _lastSyncedSetupPatternIds),
                ControllerPatternChanged = !IntArraysEqual(context.ControllerSnapshot.PatternIds, _lastSyncedControllerPatternIds),

                SetupMatrixChanged = context.SetupSnapshot.MatrixId != _lastSetupMatrixId,
                ControllerMatrixChanged = context.ControllerSnapshot.MatrixId != _lastControllerMatrixId,

                SetupNoiseChanged = context.SetupSnapshot.NoiseId != _lastSetupNoiseId,
                ControllerNoiseChanged = context.ControllerSnapshot.NoiseId != _lastControllerNoiseId
            };
        }

        private WinnerSet DetermineTrackWinners(in ChangeSet changes) {
            return new WinnerSet {
                FishWinner = DetermineWinner(changes.SetupFishChanged, changes.ControllerFishChanged, _lastFishSyncSource),
                PatternWinner = DetermineWinner(changes.SetupPatternChanged, changes.ControllerPatternChanged, _lastPatternSyncSource),
                MatrixWinner = DetermineWinner(changes.SetupMatrixChanged, changes.ControllerMatrixChanged, _lastMatrixSyncSource),
                NoiseWinner = DetermineWinner(changes.SetupNoiseChanged, changes.ControllerNoiseChanged, _lastNoiseSyncSource)
            };
        }

        private void ApplyFishTypesSync(ref SyncContext context, SyncSource winner, ref SyncResult result) {
            if (winner == SyncSource.Setup) {
                WriteFishTypesFromSetupToController(ref context, ref result);
                return;
            }

            if (winner == SyncSource.Controller) {
                WriteFishTypesFromControllerToSetup(ref context, ref result);
            }
        }

        private void ApplyPatternAssetsSync(ref SyncContext context, SyncSource winner, ref SyncResult result) {
            if (winner == SyncSource.Setup) {
                WritePatternAssetsFromSetupToController(ref context, ref result);
                return;
            }

            if (winner == SyncSource.Controller) {
                WritePatternAssetsFromControllerToSetup(ref context, ref result);
            }
        }

        private void ApplyInteractionMatrixSync(ref SyncContext context, SyncSource winner, ref SyncResult result) {
            if (winner == SyncSource.Setup) {
                WriteInteractionMatrixFromSetupToController(ref context, ref result);
                return;
            }

            if (winner == SyncSource.Controller) {
                WriteInteractionMatrixFromControllerToSetup(ref context, ref result);
            }
        }

        private void ApplyGroupNoiseSync(ref SyncContext context, SyncSource winner, ref SyncResult result) {
            if (winner == SyncSource.Setup) {
                WriteGroupNoiseFromSetupToController(ref context, ref result);
                return;
            }

            if (winner == SyncSource.Controller) {
                WriteGroupNoiseFromControllerToSetup(ref context, ref result);
            }
        }

        private void UpdateBaselines(in SyncContext context, in WinnerSet winners) {
            UpdateFishBaselines(context, winners.FishWinner);
            UpdatePatternBaselines(context, winners.PatternWinner);
            UpdateMatrixBaselines(context, winners.MatrixWinner);
            UpdateNoiseBaselines(context, winners.NoiseWinner);
        }

        private void UpdateFishBaselines(in SyncContext context, SyncSource fishWinner) {
            if (fishWinner != SyncSource.None) {
                _lastSyncedSetupFishIds = context.SetupSnapshot.FishIds;
                _lastSyncedControllerFishIds = context.ControllerSnapshot.FishIds;
                _lastFishSyncSource = fishWinner;
                return;
            }

            if (_lastSyncedSetupFishIds == null) {
                _lastSyncedSetupFishIds = context.SetupSnapshot.FishIds;
                _lastSyncedControllerFishIds = context.ControllerSnapshot.FishIds;
                _lastFishSyncSource = SyncSource.Setup;
            }
        }

        private void UpdatePatternBaselines(in SyncContext context, SyncSource patternWinner) {
            if (patternWinner != SyncSource.None) {
                _lastSyncedSetupPatternIds = context.SetupSnapshot.PatternIds;
                _lastSyncedControllerPatternIds = context.ControllerSnapshot.PatternIds;
                _lastPatternSyncSource = patternWinner;
                return;
            }

            if (_lastSyncedSetupPatternIds == null) {
                _lastSyncedSetupPatternIds = context.SetupSnapshot.PatternIds;
                _lastSyncedControllerPatternIds = context.ControllerSnapshot.PatternIds;
                _lastPatternSyncSource = SyncSource.Setup;
            }
        }

        private void UpdateMatrixBaselines(in SyncContext context, SyncSource matrixWinner) {
            if (matrixWinner != SyncSource.None) {
                _lastSetupMatrixId = context.SetupSnapshot.MatrixId;
                _lastControllerMatrixId = context.ControllerSnapshot.MatrixId;
                _lastMatrixSyncSource = matrixWinner;
                return;
            }

            if (_lastMatrixSyncSource == SyncSource.None) {
                _lastSetupMatrixId = context.SetupSnapshot.MatrixId;
                _lastControllerMatrixId = context.ControllerSnapshot.MatrixId;
                _lastMatrixSyncSource = SyncSource.Setup;
            }
        }

        private void UpdateNoiseBaselines(in SyncContext context, SyncSource noiseWinner) {
            if (noiseWinner != SyncSource.None) {
                _lastSetupNoiseId = context.SetupSnapshot.NoiseId;
                _lastControllerNoiseId = context.ControllerSnapshot.NoiseId;
                _lastNoiseSyncSource = noiseWinner;
                return;
            }

            if (_lastNoiseSyncSource == SyncSource.None) {
                _lastSetupNoiseId = context.SetupSnapshot.NoiseId;
                _lastControllerNoiseId = context.ControllerSnapshot.NoiseId;
                _lastNoiseSyncSource = SyncSource.Setup;
            }
        }

        [Serializable]
        private enum SyncSource {
            None,
            Setup,
            Controller
        }

        [Serializable]
        public struct SyncResult {
            /**
             * <summary>
             * True if any synchronization track performed writes.
             * </summary>
             */
            public bool AnyChange;

            /**
             * <summary>
             * True if the FishTypes track changed.
             * </summary>
             */
            public bool FishTypesChanged;

            /**
             * <summary>
             * True if the Layer-3 Patterns track changed.
             * </summary>
             */
            public bool PatternAssetsChanged;

            /**
             * <summary>
             * True if the Interaction Matrix track changed.
             * </summary>
             */
            public bool MatrixChanged;

            /**
             * <summary>
             * True if the Group Noise track changed.
             * </summary>
             */
            public bool GroupNoiseChanged;
        }

        private struct SetupSnapshot {
            public int[] FishIds;
            public int[] PatternIds;
            public int MatrixId;
            public int NoiseId;
        }

        private struct ControllerSnapshot {
            public int[] FishIds;
            public int[] PatternIds;
            public int MatrixId;
            public int NoiseId;
        }

        private struct ChangeSet {
            public bool SetupFishChanged;
            public bool ControllerFishChanged;

            public bool SetupPatternChanged;
            public bool ControllerPatternChanged;

            public bool SetupMatrixChanged;
            public bool ControllerMatrixChanged;

            public bool SetupNoiseChanged;
            public bool ControllerNoiseChanged;
        }

        private struct WinnerSet {
            public SyncSource FishWinner;
            public SyncSource PatternWinner;
            public SyncSource MatrixWinner;
            public SyncSource NoiseWinner;
        }

        private struct ControllerSerializedState {
            public SerializedObject SerializedObject;
            public SerializedProperty InteractionMatrixProperty;
            public SerializedProperty GroupNoiseProperty;
        }

        private struct SyncContext {
            public FlockSetup Setup;
            public FlockController Controller;
            public ControllerSerializedState ControllerSerializedState;
            public SetupSnapshot SetupSnapshot;
            public ControllerSnapshot ControllerSnapshot;
            public bool IsControllerDirty;
        }
    }
}
#endif
