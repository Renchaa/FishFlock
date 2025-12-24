#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
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

        static SyncSource DetermineWinner(
            bool setupChanged,
            bool controllerChanged,
            SyncSource lastSource) {

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

        private bool TryAutoSyncSetupToController(FlockController controller) {
            if (_isSceneAutoSyncing || controller == null || _setup == null) {
                return false;
            }

            _isSceneAutoSyncing = true;
            try {
                _setup.FishTypes ??= new List<FishTypePreset>();
                _setup.PatternAssets ??= new List<FlockLayer3PatternProfile>();

                int[] setupFishIds = BuildInstanceIdArray(_setup.FishTypes);
                int[] setupPatternIds = BuildInstanceIdArray(_setup.PatternAssets);
                int setupMatrixId = _setup.InteractionMatrix != null ? _setup.InteractionMatrix.GetInstanceID() : 0;
                int setupNoiseId = _setup.GroupNoiseSettings != null ? _setup.GroupNoiseSettings.GetInstanceID() : 0;

                int[] controllerFishIds = BuildInstanceIdArray(controller.FishTypes);
                int[] controllerPatternIds = BuildInstanceIdArray(controller.Layer3Patterns);

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

                bool setupFishChanged = !IntArraysEqual(setupFishIds, _lastSyncedSetupFishIds);
                bool controllerFishChanged = !IntArraysEqual(controllerFishIds, _lastSyncedControllerFishIds);

                bool setupPatternChanged = !IntArraysEqual(setupPatternIds, _lastSyncedSetupPatternIds);
                bool controllerPatternChanged = !IntArraysEqual(controllerPatternIds, _lastSyncedControllerPatternIds);

                bool setupMatrixChanged = setupMatrixId != _lastSetupMatrixId;
                bool controllerMatrixChanged = controllerMatrixId != _lastControllerMatrixId;

                bool setupNoiseChanged = setupNoiseId != _lastSetupNoiseId;
                bool controllerNoiseChanged = controllerNoiseId != _lastControllerNoiseId;

                SyncSource fishWinner = DetermineWinner(setupFishChanged, controllerFishChanged, _lastFishSyncSource);
                SyncSource patternWinner = DetermineWinner(setupPatternChanged, controllerPatternChanged, _lastPatternSyncSource);
                SyncSource matrixWinner = DetermineWinner(setupMatrixChanged, controllerMatrixChanged, _lastMatrixSyncSource);
                SyncSource noiseWinner = DetermineWinner(setupNoiseChanged, controllerNoiseChanged, _lastNoiseSyncSource);

                bool anyChange = false;
                bool controllerDirty = false;

                if (fishWinner == SyncSource.Setup) {
                    SerializedProperty fishTypesProp = so.FindProperty("fishTypes");
                    if (fishTypesProp != null) {
                        fishTypesProp.arraySize = _setup.FishTypes.Count;
                        for (int i = 0; i < _setup.FishTypes.Count; i++) {
                            fishTypesProp.GetArrayElementAtIndex(i).objectReferenceValue = _setup.FishTypes[i];
                        }
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (fishWinner == SyncSource.Controller) {
                    Undo.RecordObject(_setup, "Sync Setup Fish Types From Controller");
                    _setup.FishTypes.Clear();
                    var types = controller.FishTypes;
                    if (types != null && types.Length > 0) {
                        _setup.FishTypes.AddRange(types);
                    }
                    EditorUtility.SetDirty(_setup);
                    anyChange = true;

                    setupFishIds = BuildInstanceIdArray(_setup.FishTypes);
                }

                if (patternWinner == SyncSource.Setup) {
                    SerializedProperty layer3Prop = so.FindProperty("layer3Patterns");
                    if (layer3Prop != null) {
                        layer3Prop.arraySize = _setup.PatternAssets.Count;
                        for (int i = 0; i < _setup.PatternAssets.Count; i++) {
                            layer3Prop.GetArrayElementAtIndex(i).objectReferenceValue = _setup.PatternAssets[i];
                        }
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (patternWinner == SyncSource.Controller) {
                    Undo.RecordObject(_setup, "Sync Setup Patterns From Controller");
                    _setup.PatternAssets.Clear();
                    var patterns = controller.Layer3Patterns;
                    if (patterns != null && patterns.Length > 0) {
                        _setup.PatternAssets.AddRange(patterns);
                    }
                    EditorUtility.SetDirty(_setup);
                    anyChange = true;

                    setupPatternIds = BuildInstanceIdArray(_setup.PatternAssets);
                }

                if (matrixWinner == SyncSource.Setup) {
                    if (interactionProp != null) {
                        interactionProp.objectReferenceValue = _setup.InteractionMatrix;
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (matrixWinner == SyncSource.Controller) {
                    if (interactionProp != null) {
                        Undo.RecordObject(_setup, "Sync Setup Interaction Matrix From Controller");
                        var matrixFromCtrl = interactionProp.objectReferenceValue as FishInteractionMatrix;
                        _setup.InteractionMatrix = matrixFromCtrl;
                        EditorUtility.SetDirty(_setup);
                        anyChange = true;

                        setupMatrixId = matrixFromCtrl != null ? matrixFromCtrl.GetInstanceID() : 0;
                    }
                }

                if (noiseWinner == SyncSource.Setup) {
                    if (groupNoiseProp != null) {
                        groupNoiseProp.objectReferenceValue = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
                        controllerDirty = true;
                        anyChange = true;
                    }
                } else if (noiseWinner == SyncSource.Controller) {
                    if (groupNoiseProp != null) {
                        Undo.RecordObject(_setup, "Sync Setup Group Noise From Controller");
                        var noiseFromCtrl = groupNoiseProp.objectReferenceValue as GroupNoisePatternProfile;
                        _setup.GroupNoiseSettings = noiseFromCtrl;
                        EditorUtility.SetDirty(_setup);
                        anyChange = true;

                        setupNoiseId = noiseFromCtrl != null ? noiseFromCtrl.GetInstanceID() : 0;
                    }
                }

                // ----- APPLY CONTROLLER CHANGES -----
                if (controllerDirty) {
                    so.ApplyModifiedProperties();
                    so.UpdateIfRequiredOrScript(); // <-- ADD THIS
                    EditorUtility.SetDirty(controller);

                    // keep spawner in sync with controller fish types
                    TrySyncSpawnerFromController(controller);

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

                if (fishWinner != SyncSource.None) {
                    _lastSyncedSetupFishIds = setupFishIds;
                    _lastSyncedControllerFishIds = controllerFishIds;
                    _lastFishSyncSource = fishWinner;
                } else if (_lastSyncedSetupFishIds == null) {
                    _lastSyncedSetupFishIds = setupFishIds;
                    _lastSyncedControllerFishIds = controllerFishIds;
                    _lastFishSyncSource = SyncSource.Setup;
                }

                if (patternWinner != SyncSource.None) {
                    _lastSyncedSetupPatternIds = setupPatternIds;
                    _lastSyncedControllerPatternIds = controllerPatternIds;
                    _lastPatternSyncSource = patternWinner;
                } else if (_lastSyncedSetupPatternIds == null) {
                    _lastSyncedSetupPatternIds = setupPatternIds;
                    _lastSyncedControllerPatternIds = controllerPatternIds;
                    _lastPatternSyncSource = SyncSource.Setup;
                }

                if (matrixWinner != SyncSource.None) {
                    _lastSetupMatrixId = setupMatrixId;
                    _lastControllerMatrixId = controllerMatrixId;
                    _lastMatrixSyncSource = matrixWinner;
                } else if (_lastMatrixSyncSource == SyncSource.None) {
                    _lastSetupMatrixId = setupMatrixId;
                    _lastControllerMatrixId = controllerMatrixId;
                    _lastMatrixSyncSource = SyncSource.Setup;
                }

                if (noiseWinner != SyncSource.None) {
                    _lastSetupNoiseId = setupNoiseId;
                    _lastControllerNoiseId = controllerNoiseId;
                    _lastNoiseSyncSource = noiseWinner;
                } else if (_lastNoiseSyncSource == SyncSource.None) {
                    _lastSetupNoiseId = setupNoiseId;
                    _lastControllerNoiseId = controllerNoiseId;
                    _lastNoiseSyncSource = SyncSource.Setup;
                }

                return anyChange;
            } finally {
                _isSceneAutoSyncing = false;
            }
        }

        private bool TryPullControllerRefsIntoSetup(FlockController controller) {
            if (controller == null || _setup == null) {
                return false;
            }

            SerializedObject so = new SerializedObject(controller);
            bool changed = false;

            SerializedProperty ctrlFishTypes = so.FindProperty("fishTypes");
            if (ctrlFishTypes != null) {
                int count = ctrlFishTypes.arraySize;

                if (_setup.FishTypes == null) {
                    _setup.FishTypes = new List<FishTypePreset>(count);
                }

                bool mismatch = (_setup.FishTypes.Count != count);
                if (!mismatch) {
                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FishTypePreset)ctrlFishTypes
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        if (_setup.FishTypes[i] != fromCtrl) {
                            mismatch = true;
                            break;
                        }
                    }
                }

                if (mismatch) {
                    Undo.RecordObject(_setup, "Sync Setup Fish Types From Controller");

                    _setup.FishTypes.Clear();
                    _setup.FishTypes.Capacity = Mathf.Max(_setup.FishTypes.Capacity, count);

                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FishTypePreset)ctrlFishTypes
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        _setup.FishTypes.Add(fromCtrl);
                    }

                    changed = true;
                    DestroySpeciesEditor();
                    RebuildSpeciesEditor();
                }
            }

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

            SerializedProperty ctrlLayer3 = so.FindProperty("layer3Patterns");
            if (ctrlLayer3 != null) {
                int count = ctrlLayer3.arraySize;

                if (_setup.PatternAssets == null) {
                    _setup.PatternAssets = new List<FlockLayer3PatternProfile>(count);
                }

                bool mismatch = (_setup.PatternAssets.Count != count);
                if (!mismatch) {
                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FlockLayer3PatternProfile)ctrlLayer3
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        if (_setup.PatternAssets[i] != fromCtrl) {
                            mismatch = true;
                            break;
                        }
                    }
                }

                if (mismatch) {
                    Undo.RecordObject(_setup, "Sync Setup Patterns From Controller");

                    _setup.PatternAssets.Clear();
                    _setup.PatternAssets.Capacity = Mathf.Max(_setup.PatternAssets.Capacity, count);

                    for (int i = 0; i < count; i++) {
                        var fromCtrl = (FlockLayer3PatternProfile)ctrlLayer3
                            .GetArrayElementAtIndex(i)
                            .objectReferenceValue;
                        _setup.PatternAssets.Add(fromCtrl);
                    }

                    changed = true;
                    RebuildNoiseEditors();
                }
            }

            if (changed) {
                EditorUtility.SetDirty(_setup);
            }

            return changed;
        }
    }
}
#endif
