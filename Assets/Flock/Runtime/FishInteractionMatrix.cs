// ==========================================
// FishInteractionMatrix.cs (Runtime)
// ==========================================
namespace Flock.Runtime {
    using System;
    using UnityEngine;
    using Flock.Runtime.Data;

    [CreateAssetMenu(
        fileName = "FishInteractionMatrix",
        menuName = "Flock/Fish Interaction Matrix")]
    public sealed class FishInteractionMatrix : ScriptableObject {
        [SerializeField]
        FishTypePreset[] fishTypes = Array.Empty<FishTypePreset>();

        // N x N symmetric interaction flags (row * N + col)
        [SerializeField, HideInInspector]
        bool[] interactionFlags = Array.Empty<bool>();

        // NEW: N x N symmetric relationship types (row * N + col)
        [SerializeField, HideInInspector]
        FishRelationType[] relationTypes = Array.Empty<FishRelationType>();

        [SerializeField, HideInInspector]
        float[] neutralWeights = Array.Empty<float>();
        // per-fish leadership weights (size N, one per fish type)
        [SerializeField, HideInInspector]
        float[] leadershipWeights = Array.Empty<float>();
        // NEW: per-fish avoidance weights (size N, one per fish type)
        [SerializeField, HideInInspector]
        float[] avoidanceWeights = Array.Empty<float>();

        // default weights used when a slot is zero / uninitialised
        [SerializeField, Min(0f)]
        float defaultLeadershipWeight = 1.0f;
        [SerializeField, Min(0f)]
        float defaultAvoidanceWeight = 1.0f;
        [SerializeField, Min(0f)]
        float defaultNeutralWeight = 1.0f;

        public FishTypePreset[] FishTypes => fishTypes;
        public int Count => fishTypes != null ? fishTypes.Length : 0;

        public float DefaultLeadershipWeight {
            get => defaultLeadershipWeight;
            set => defaultLeadershipWeight = Mathf.Max(0f, value);
        }

        public float DefaultAvoidanceWeight {
            get => defaultAvoidanceWeight;
            set => defaultAvoidanceWeight = Mathf.Max(0f, value);
        }

        public float DefaultNeutralWeight {
            get => defaultNeutralWeight;
            set => defaultNeutralWeight = Mathf.Max(0f, value);
        }

        int Index(int row, int col, int n) => row * n + col;

        // ===== REPLACE SyncSizeWithFishTypes WITH THIS VERSION =====
        // REPLACE THIS WHOLE METHOD
        public void SyncSizeWithFishTypes() {
            int n = Count;
            int requiredMatrixSize = n * n;

            // cache old data so we can preserve as much as possible
            bool[] oldFlags = interactionFlags;
            FishRelationType[] oldRelations = relationTypes;
            float[] oldLeadership = leadershipWeights;
            float[] oldAvoidance = avoidanceWeights;
            float[] oldNeutral = neutralWeights;

            int oldNFlags = 0;
            if (oldFlags != null && oldFlags.Length > 0) {
                oldNFlags = Mathf.RoundToInt(Mathf.Sqrt(oldFlags.Length));
            }

            int oldNRelations = 0;
            if (oldRelations != null && oldRelations.Length > 0) {
                oldNRelations = Mathf.RoundToInt(Mathf.Sqrt(oldRelations.Length));
            }

            // --- allocate / resize interaction matrix (bool) ---
            if (interactionFlags == null || interactionFlags.Length != requiredMatrixSize) {
                interactionFlags = new bool[requiredMatrixSize];
            }

            // --- allocate / resize relation matrix (FishRelationType) ---
            if (relationTypes == null || relationTypes.Length != requiredMatrixSize) {
                relationTypes = new FishRelationType[requiredMatrixSize];
            }

            // copy old interaction flags into new matrix where possible
            if (oldFlags != null && oldNFlags > 0) {
                int copyN = Mathf.Min(oldNFlags, n);
                for (int r = 0; r < copyN; r++) {
                    for (int c = 0; c < copyN; c++) {
                        int oldIdx = r * oldNFlags + c;
                        int newIdx = r * n + c;
                        interactionFlags[newIdx] = oldFlags[oldIdx];
                    }
                }
            }

            // copy old relations into new matrix where possible
            if (oldRelations != null && oldNRelations > 0) {
                int copyN = Mathf.Min(oldNRelations, n);
                for (int r = 0; r < copyN; r++) {
                    for (int c = 0; c < copyN; c++) {
                        int oldIdx = r * oldNRelations + c;
                        int newIdx = r * n + c;
                        relationTypes[newIdx] = oldRelations[oldIdx];
                    }
                }
            }

            // --- 1D per-type weights ---

            // leadership
            if (leadershipWeights == null || leadershipWeights.Length != n) {
                float[] newArr = new float[n];
                if (oldLeadership != null) {
                    int copyN = Mathf.Min(oldLeadership.Length, n);
                    for (int i = 0; i < copyN; i++) {
                        newArr[i] = oldLeadership[i];
                    }
                }
                leadershipWeights = newArr;
            }

            // avoidance
            if (avoidanceWeights == null || avoidanceWeights.Length != n) {
                float[] newArr = new float[n];
                if (oldAvoidance != null) {
                    int copyN = Mathf.Min(oldAvoidance.Length, n);
                    for (int i = 0; i < copyN; i++) {
                        newArr[i] = oldAvoidance[i];
                    }
                }
                avoidanceWeights = newArr;
            }

            // neutral
            if (neutralWeights == null || neutralWeights.Length != n) {
                float[] newArr = new float[n];
                if (oldNeutral != null) {
                    int copyN = Mathf.Min(oldNeutral.Length, n);
                    for (int i = 0; i < copyN; i++) {
                        newArr[i] = oldNeutral[i];
                    }
                }
                neutralWeights = newArr;
            }

            // ensure valid defaults
            for (int i = 0; i < n; i++) {
                if (leadershipWeights[i] <= 0f) {
                    leadershipWeights[i] = defaultLeadershipWeight;
                }

                if (avoidanceWeights[i] <= 0f) {
                    avoidanceWeights[i] = defaultAvoidanceWeight;
                }

                if (neutralWeights[i] <= 0f) {
                    neutralWeights[i] = defaultNeutralWeight;
                }
            }
        }

        public bool GetInteraction(int a, int b) {
            int n = Count;
            if (a < 0 || b < 0 || a >= n || b >= n || interactionFlags == null) {
                return false;
            }

            return interactionFlags[Index(a, b, n)];
        }

        public void SetSymmetricInteraction(int a, int b, bool enabled) {
            int n = Count;
            if (a < 0 || b < 0 || a >= n || b >= n) {
                return;
            }

            int idxAB = Index(a, b, n);
            int idxBA = Index(b, a, n);

            interactionFlags[idxAB] = enabled;
            interactionFlags[idxBA] = enabled;

            // if interaction is turned off, clear relationship too
            if (!enabled && relationTypes != null && relationTypes.Length == n * n) {
                relationTypes[idxAB] = FishRelationType.Neutral;
                relationTypes[idxBA] = FishRelationType.Neutral;
            }
        }

        public FishRelationType GetRelation(int a, int b) {
            int n = Count;

            if (a < 0 || b < 0 || a >= n || b >= n || relationTypes == null) {
                return FishRelationType.Neutral;
            }

            int idx = Index(a, b, n);

            if (idx < 0 || idx >= relationTypes.Length) {
                return FishRelationType.Neutral;
            }

            return relationTypes[idx];
        }


        public void SetSymmetricRelation(int a, int b, FishRelationType relation) {
            int n = Count;

            if (a < 0 || b < 0 || a >= n || b >= n || relationTypes == null) {
                return;
            }

            int idxAB = Index(a, b, n);
            int idxBA = Index(b, a, n);

            if (idxAB < 0 || idxAB >= relationTypes.Length
                || idxBA < 0 || idxBA >= relationTypes.Length) {
                return;
            }

            relationTypes[idxAB] = relation;
            relationTypes[idxBA] = relation;
        }

        // ===== existing leadership weight API (unchanged) =====
        public float GetLeadershipWeight(int typeIndex) {
            int n = Count;
            if (typeIndex < 0 || typeIndex >= n || leadershipWeights == null) {
                return defaultLeadershipWeight;
            }

            float w = leadershipWeights[typeIndex];
            return w > 0f ? w : defaultLeadershipWeight;
        }

        public void SetLeadershipWeight(int typeIndex, float weight) {
            int n = Count;
            if (typeIndex < 0 || typeIndex >= n || leadershipWeights == null) {
                return;
            }

            leadershipWeights[typeIndex] = Mathf.Max(0f, weight);
        }

        public float GetLeadershipWeight(FishTypePreset preset) {
            if (preset == null || fishTypes == null) {
                return defaultLeadershipWeight;
            }

            int n = fishTypes.Length;
            for (int i = 0; i < n; i++) {
                if (fishTypes[i] == preset) {
                    return GetLeadershipWeight(i);
                }
            }

            return defaultLeadershipWeight;
        }

        // ===== NEW: avoidance weight API =====

        public float GetAvoidanceWeight(FishTypePreset preset) {
            if (preset == null || fishTypes == null) {
                return defaultAvoidanceWeight;
            }

            int n = fishTypes.Length;
            for (int i = 0; i < n; i++) {
                if (fishTypes[i] == preset) {
                    return GetAvoidanceWeight(i);
                }
            }

            return defaultAvoidanceWeight;
        }

        // ADD these new APIs somewhere near GetLeadershipWeight / SetLeadershipWeight

        public float GetAvoidanceWeight(int typeIndex) {
            int n = Count;
            if (typeIndex < 0 || typeIndex >= n || avoidanceWeights == null) {
                return defaultAvoidanceWeight;
            }

            float w = avoidanceWeights[typeIndex];
            return w > 0f ? w : defaultAvoidanceWeight;
        }

        public void SetAvoidanceWeight(int typeIndex, float weight) {
            int n = Count;
            if (typeIndex < 0 || typeIndex >= n || avoidanceWeights == null) {
                return;
            }

            avoidanceWeights[typeIndex] = Mathf.Max(0f, weight);
        }

        public float GetNeutralWeight(int typeIndex) {
            int n = Count;
            if (typeIndex < 0 || typeIndex >= n || neutralWeights == null) {
                return defaultNeutralWeight;
            }

            float w = neutralWeights[typeIndex];
            return w > 0f ? w : defaultNeutralWeight;
        }

        public void SetNeutralWeight(int typeIndex, float weight) {
            int n = Count;
            if (typeIndex < 0 || typeIndex >= n || neutralWeights == null) {
                return;
            }

            neutralWeights[typeIndex] = Mathf.Max(0f, weight);
        }


#if UNITY_EDITOR
        void OnValidate() {
            SyncSizeWithFishTypes();
        }
#endif
    }
}
