#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /// <summary>
    /// Owns all Setup <-> Controller reference synchronization state and logic.
    /// Keeps the behaviour identical to the previous in-window implementation:
    /// - Tracks per-track snapshots via instanceIDs
    /// - Resolves conflicts via "last winner" preference
    /// - Two-way sync for:
    ///   - FishTypes
    ///   - Layer3 Patterns
    ///   - Interaction Matrix
    ///   - Group Noise Pattern
    /// </summary>
    [Serializable]
    public sealed class FlockSetupControllerSync {

        [Serializable]
        enum SyncSource {
            None,
            Setup,
            Controller
        }

        [Serializable]
        public struct SyncResult {
            public bool AnyChange;

            public bool FishTypesChanged;
            public bool PatternAssetsChanged;
            public bool MatrixChanged;
            public bool GroupNoiseChanged;
        }

        // Reentrancy guard (not serialized)
        bool _isSyncing;

        // Last-synced instanceID snapshots for fish / patterns
        [SerializeField] int[] _lastSyncedSetupFishIds;
        [SerializeField] int[] _lastSyncedControllerFishIds;
        [SerializeField] int[] _lastSyncedSetupPatternIds;
        [SerializeField] int[] _lastSyncedControllerPatternIds;

        // Single-ID tracking for InteractionMatrix and GroupNoise
        [SerializeField] int _lastSetupMatrixId;
        [SerializeField] int _lastControllerMatrixId;
        [SerializeField] int _lastSetupNoiseId;
        [SerializeField] int _lastControllerNoiseId;

        // Remember last winner to resolve conflicts when both changed
        [SerializeField] SyncSource _lastFishSyncSource = SyncSource.None;
        [SerializeField] SyncSource _lastPatternSyncSource = SyncSource.None;
        [SerializeField] SyncSource _lastMatrixSyncSource = SyncSource.None;
        [SerializeField] SyncSource _lastNoiseSyncSource = SyncSource.None;

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

        public SyncResult SyncTwoWay(
            FlockSetup setup,
            FlockController controller,
            Action<FlockController> afterControllerWrite = null) {

            var result = new SyncResult();

            if (_isSyncing || controller == null || setup == null) {
                return result;
            }

            _isSyncing = true;
            try {
                // Ensure lists exist
                setup.FishTypes ??= new List<FishTypePreset>();
                setup.PatternAssets ??= new List<FlockLayer3PatternProfile>();

                // ----- SNAPSHOT CURRENT STATE (Setup) -----
                int[] setupFishIds = BuildInstanceIdArray(setup.FishTypes);
                int[] setupPatternIds = BuildInstanceIdArray(setup.PatternAssets);
                int setupMatrixId = setup.InteractionMatrix != null ? setup.InteractionMatrix.GetInstanceID() : 0;
                int setupNoiseId = setup.GroupNoiseSettings != null ? setup.GroupNoiseSettings.GetInstanceID() : 0;

                // ----- SNAPSHOT CURRENT STATE (Controller via runtime getters) -----
                int[] controllerFishIds = BuildInstanceIdArray(controller.FishTypes);
                int[] controllerPatternIds = BuildInstanceIdArray(controller.Layer3Patterns);

                // ----- SNAPSHOT CURRENT STATE (Controller via SerializedObject for single refs) -----
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

                bool controllerDirty = false;

                // ----- FISH TYPES SYNC (two-way) -----
                if (fishWinner == SyncSource.Setup) {
                    SerializedProperty fishTypesProp = so.FindProperty("fishTypes");
                    if (fishTypesProp != null) {
                        fishTypesProp.arraySize = setup.FishTypes.Count;
                        for (int i = 0; i < setup.FishTypes.Count; i++) {
                            fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue = setup.FishTypes[i];
                        }

                        controllerDirty = true;
                        result.AnyChange = true;
                        result.FishTypesChanged = true;
                    }
                } else if (fishWinner == SyncSource.Controller) {
                    Undo.RecordObject(setup, "Sync Setup Fish Types From Controller");
                    setup.FishTypes.Clear();

                    var types = controller.FishTypes;
                    if (types != null && types.Length > 0) {
                        setup.FishTypes.AddRange(types);
                    }

                    EditorUtility.SetDirty(setup);
                    result.AnyChange = true;
                    result.FishTypesChanged = true;

                    // Refresh Setup snapshot
                    setupFishIds = BuildInstanceIdArray(setup.FishTypes);
                }

                // ----- LAYER-3 PATTERNS SYNC (two-way) -----
                if (patternWinner == SyncSource.Setup) {
                    SerializedProperty layer3Prop = so.FindProperty("layer3Patterns");
                    if (layer3Prop != null) {
                        layer3Prop.arraySize = setup.PatternAssets.Count;
                        for (int i = 0; i < setup.PatternAssets.Count; i++) {
                            layer3Prop.GetArrayElementAtIndex(i).objectReferenceValue = setup.PatternAssets[i];
                        }

                        controllerDirty = true;
                        result.AnyChange = true;
                        result.PatternAssetsChanged = true;
                    }
                } else if (patternWinner == SyncSource.Controller) {
                    Undo.RecordObject(setup, "Sync Setup Patterns From Controller");
                    setup.PatternAssets.Clear();

                    var patterns = controller.Layer3Patterns;
                    if (patterns != null && patterns.Length > 0) {
                        setup.PatternAssets.AddRange(patterns);
                    }

                    EditorUtility.SetDirty(setup);
                    result.AnyChange = true;
                    result.PatternAssetsChanged = true;

                    // Refresh Setup snapshot
                    setupPatternIds = BuildInstanceIdArray(setup.PatternAssets);
                }

                // ----- INTERACTION MATRIX SYNC (two-way) -----
                if (matrixWinner == SyncSource.Setup) {
                    if (interactionProp != null) {
                        interactionProp.objectReferenceValue = setup.InteractionMatrix;
                        controllerDirty = true;

                        result.AnyChange = true;
                        result.MatrixChanged = true;
                    }
                } else if (matrixWinner == SyncSource.Controller) {
                    if (interactionProp != null) {
                        Undo.RecordObject(setup, "Sync Setup Interaction Matrix From Controller");
                        var matrixFromCtrl = interactionProp.objectReferenceValue as FishInteractionMatrix;

                        setup.InteractionMatrix = matrixFromCtrl;
                        EditorUtility.SetDirty(setup);

                        result.AnyChange = true;
                        result.MatrixChanged = true;

                        setupMatrixId = matrixFromCtrl != null ? matrixFromCtrl.GetInstanceID() : 0;
                    }
                }

                // ----- GROUP NOISE PATTERN SYNC (two-way) -----
                if (noiseWinner == SyncSource.Setup) {
                    if (groupNoiseProp != null) {
                        groupNoiseProp.objectReferenceValue = setup.GroupNoiseSettings as GroupNoisePatternProfile;
                        controllerDirty = true;

                        result.AnyChange = true;
                        result.GroupNoiseChanged = true;
                    }
                } else if (noiseWinner == SyncSource.Controller) {
                    if (groupNoiseProp != null) {
                        Undo.RecordObject(setup, "Sync Setup Group Noise From Controller");
                        var noiseFromCtrl = groupNoiseProp.objectReferenceValue as GroupNoisePatternProfile;

                        setup.GroupNoiseSettings = noiseFromCtrl;
                        EditorUtility.SetDirty(setup);

                        result.AnyChange = true;
                        result.GroupNoiseChanged = true;

                        setupNoiseId = noiseFromCtrl != null ? noiseFromCtrl.GetInstanceID() : 0;
                    }
                }

                // ----- APPLY CONTROLLER CHANGES -----
                if (controllerDirty) {
                    so.ApplyModifiedProperties();
                    so.UpdateIfRequiredOrScript(); // keep your "post-apply refresh" behaviour
                    EditorUtility.SetDirty(controller);

                    afterControllerWrite?.Invoke(controller);

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

                return result;
            } finally {
                _isSyncing = false;
            }
        }

        /// <summary>
        /// Controller -> Setup pull, intended for "user edited controller inspector UI" path.
        /// Mirrors your previous logic and returns track flags so the window can rebuild editors if needed.
        /// </summary>
        public SyncResult PullControllerIntoSetup(FlockSetup setup, FlockController controller) {
            var result = new SyncResult();

            if (controller == null || setup == null) {
                return result;
            }

            SerializedObject so = new SerializedObject(controller);
            so.UpdateIfRequiredOrScript();

            // --- Fish Types (controller -> setup) ---
            SerializedProperty ctrlFishTypes = so.FindProperty("fishTypes");
            if (ctrlFishTypes != null) {
                int count = ctrlFishTypes.arraySize;

                setup.FishTypes ??= new List<FishTypePreset>(count);

                bool mismatch = (setup.FishTypes.Count != count);
                if (!mismatch) {
                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FishTypePreset)ctrlFishTypes
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;

                        if (setup.FishTypes[i] != fromCtrl) {
                            mismatch = true;
                            break;
                        }
                    }
                }

                if (mismatch) {
                    Undo.RecordObject(setup, "Sync Setup Fish Types From Controller");

                    setup.FishTypes.Clear();
                    setup.FishTypes.Capacity = Mathf.Max(setup.FishTypes.Capacity, count);

                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FishTypePreset)ctrlFishTypes
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;

                        setup.FishTypes.Add(fromCtrl);
                    }

                    result.AnyChange = true;
                    result.FishTypesChanged = true;
                }
            }

            // --- Interaction Matrix (controller -> setup) ---
            // Must allow null (clearing) to propagate.
            var ctrlMatrix = so.FindProperty("interactionMatrix")?.objectReferenceValue as FishInteractionMatrix;
            if (setup.InteractionMatrix != ctrlMatrix) {
                Undo.RecordObject(setup, "Sync Setup Interaction Matrix");
                setup.InteractionMatrix = ctrlMatrix;

                result.AnyChange = true;
                result.MatrixChanged = true;
            }

            // --- Group Noise (controller -> setup) ---
            // Must allow null (clearing) to propagate.
            var ctrlNoise = so.FindProperty("groupNoisePattern")?.objectReferenceValue as GroupNoisePatternProfile;
            if (setup.GroupNoiseSettings != ctrlNoise) {
                Undo.RecordObject(setup, "Sync Setup Group Noise");
                setup.GroupNoiseSettings = ctrlNoise;

                result.AnyChange = true;
                result.GroupNoiseChanged = true;
            }

            // --- Layer-3 Patterns (controller -> setup) ---
            SerializedProperty ctrlLayer3 = so.FindProperty("layer3Patterns");
            if (ctrlLayer3 != null) {
                int count = ctrlLayer3.arraySize;

                setup.PatternAssets ??= new List<FlockLayer3PatternProfile>(count);

                bool mismatch = (setup.PatternAssets.Count != count);
                if (!mismatch) {
                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FlockLayer3PatternProfile)ctrlLayer3
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;

                        if (setup.PatternAssets[i] != fromCtrl) {
                            mismatch = true;
                            break;
                        }
                    }
                }

                if (mismatch) {
                    Undo.RecordObject(setup, "Sync Setup Patterns From Controller");

                    setup.PatternAssets.Clear();
                    setup.PatternAssets.Capacity = Mathf.Max(setup.PatternAssets.Capacity, count);

                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FlockLayer3PatternProfile)ctrlLayer3
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;

                        setup.PatternAssets.Add(fromCtrl);
                    }

                    result.AnyChange = true;
                    result.PatternAssetsChanged = true;
                }
            }

            if (result.AnyChange) {
                EditorUtility.SetDirty(setup);
            }

            return result;
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

        static SyncSource DetermineWinner(bool setupChanged, bool controllerChanged, SyncSource lastSource) {
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
    }
}
#endif
