using System;
using Flock.Runtime.Data;
using UnityEngine;

namespace Flock.Runtime {
    /**
     * <summary>
     * Defines per-species interaction configuration, including NxN interaction/relationship matrices and
     * per-type weight arrays aligned to the <see cref="FishTypes"/> ordering.
     * </summary>
     */
    [CreateAssetMenu(
        fileName = "FishInteractionMatrix",
        menuName = "Flock/Fish Interaction Matrix")]
    public sealed class FishInteractionMatrix : ScriptableObject {
        [SerializeField]
        private FishTypePreset[] fishTypes = Array.Empty<FishTypePreset>();

        // N x N symmetric interaction flags (row * N + col).
        [SerializeField]
        [HideInInspector]
        private bool[] interactionFlags = Array.Empty<bool>();

        // N x N symmetric relationship types (row * N + col).
        [SerializeField]
        [HideInInspector]
        private FishRelationType[] relationTypes = Array.Empty<FishRelationType>();

        [SerializeField]
        [HideInInspector]
        private float[] neutralWeights = Array.Empty<float>();

        // Per-fish leadership weights (size N, one per fish type).
        [SerializeField]
        [HideInInspector]
        private float[] leadershipWeights = Array.Empty<float>();

        // Per-fish avoidance weights (size N, one per fish type).
        [SerializeField]
        [HideInInspector]
        private float[] avoidanceWeights = Array.Empty<float>();

        // Default weights used when a slot is zero / uninitialised.
        [SerializeField]
        [Min(0f)]
        private float defaultLeadershipWeight = 1.0f;

        [SerializeField]
        [Min(0f)]
        private float defaultAvoidanceWeight = 1.0f;

        [SerializeField]
        [Min(0f)]
        private float defaultNeutralWeight = 1.0f;

        /**
         * <summary>
         * Gets the ordered list of fish types used to index matrices and per-type arrays.
         * </summary>
         */
        public FishTypePreset[] FishTypes => fishTypes;

        /**
         * <summary>
         * Gets the number of fish types currently present in <see cref="FishTypes"/>.
         * </summary>
         */
        public int Count => fishTypes != null ? fishTypes.Length : 0;

        /**
         * <summary>
         * Resizes internal matrices and per-type arrays to match the current <see cref="FishTypes"/> length,
         * preserving data where possible.
         * </summary>
         */
        public void SyncSizeWithFishTypes() {
            int typeCount = Count;
            int requiredMatrixSize = typeCount * typeCount;

            bool[] previousInteractionFlags = interactionFlags;
            FishRelationType[] previousRelationTypes = relationTypes;

            float[] previousLeadershipWeights = leadershipWeights;
            float[] previousAvoidanceWeights = avoidanceWeights;
            float[] previousNeutralWeights = neutralWeights;

            int previousInteractionSideLength = GetSquareMatrixSideLength(previousInteractionFlags);
            int previousRelationSideLength = GetSquareMatrixSideLength(previousRelationTypes);

            EnsureMatrixSize(ref interactionFlags, requiredMatrixSize);
            EnsureMatrixSize(ref relationTypes, requiredMatrixSize);

            CopySquareMatrix(previousInteractionFlags, previousInteractionSideLength, interactionFlags, typeCount);
            CopySquareMatrix(previousRelationTypes, previousRelationSideLength, relationTypes, typeCount);

            leadershipWeights = EnsurePerTypeArraySize(leadershipWeights, previousLeadershipWeights, typeCount);
            avoidanceWeights = EnsurePerTypeArraySize(avoidanceWeights, previousAvoidanceWeights, typeCount);
            neutralWeights = EnsurePerTypeArraySize(neutralWeights, previousNeutralWeights, typeCount);

            ApplyDefaultWeights(typeCount);
        }

        /**
         * <summary>
         * Gets whether interaction is enabled for the ordered pair (a, b).
         * </summary>
         * <param name="a">Row fish type index.</param>
         * <param name="b">Column fish type index.</param>
         * <returns>True if enabled; otherwise false.</returns>
         */
        public bool GetInteraction(int a, int b) {
            int typeCount = Count;
            if (a < 0 || b < 0 || a >= typeCount || b >= typeCount || interactionFlags == null) {
                return false;
            }

            return interactionFlags[GetMatrixIndex(a, b, typeCount)];
        }

        /**
         * <summary>
         * Sets interaction for (a, b) and (b, a) to the same value. If disabling interaction, also clears
         * the corresponding relationship entries to <see cref="FishRelationType.Neutral"/>.
         * </summary>
         * <param name="a">First fish type index.</param>
         * <param name="b">Second fish type index.</param>
         * <param name="enabled">Whether interaction is enabled.</param>
         */
        public void SetSymmetricInteraction(int a, int b, bool enabled) {
            int typeCount = Count;
            if (a < 0 || b < 0 || a >= typeCount || b >= typeCount) {
                return;
            }

            int indexAB = GetMatrixIndex(a, b, typeCount);
            int indexBA = GetMatrixIndex(b, a, typeCount);

            interactionFlags[indexAB] = enabled;
            interactionFlags[indexBA] = enabled;

            ClearRelationWhenInteractionDisabled(typeCount, enabled, indexAB, indexBA);
        }

        /**
         * <summary>
         * Gets the relationship for the ordered pair (a, b).
         * </summary>
         * <param name="a">Row fish type index.</param>
         * <param name="b">Column fish type index.</param>
         * <returns>The relationship value, or Neutral if unavailable.</returns>
         */
        public FishRelationType GetRelation(int a, int b) {
            int typeCount = Count;
            if (a < 0 || b < 0 || a >= typeCount || b >= typeCount || relationTypes == null) {
                return FishRelationType.Neutral;
            }

            int index = GetMatrixIndex(a, b, typeCount);
            if (index < 0 || index >= relationTypes.Length) {
                return FishRelationType.Neutral;
            }

            return relationTypes[index];
        }

        /**
         * <summary>
         * Sets the relationship for (a, b) and (b, a) to the same value.
         * </summary>
         * <param name="a">First fish type index.</param>
         * <param name="b">Second fish type index.</param>
         * <param name="relation">The relationship value to assign.</param>
         */
        public void SetSymmetricRelation(int a, int b, FishRelationType relation) {
            int typeCount = Count;
            if (a < 0 || b < 0 || a >= typeCount || b >= typeCount || relationTypes == null) {
                return;
            }

            int indexAB = GetMatrixIndex(a, b, typeCount);
            int indexBA = GetMatrixIndex(b, a, typeCount);

            if (indexAB < 0 || indexAB >= relationTypes.Length || indexBA < 0 || indexBA >= relationTypes.Length) {
                return;
            }

            relationTypes[indexAB] = relation;
            relationTypes[indexBA] = relation;
        }

        /**
         * <summary>
         * Gets the leadership weight for a fish type index.
         * </summary>
         * <param name="typeIndex">The fish type index.</param>
         * <returns>The leadership weight, or the default if unavailable or non-positive.</returns>
         */
        public float GetLeadershipWeight(int typeIndex) {
            int typeCount = Count;
            if (typeIndex < 0 || typeIndex >= typeCount || leadershipWeights == null) {
                return defaultLeadershipWeight;
            }

            float weight = leadershipWeights[typeIndex];
            return weight > 0f ? weight : defaultLeadershipWeight;
        }

        /**
         * <summary>
         * Sets the leadership weight for a fish type index.
         * </summary>
         * <param name="typeIndex">The fish type index.</param>
         * <param name="weight">The weight value (clamped to non-negative).</param>
         */
        public void SetLeadershipWeight(int typeIndex, float weight) {
            int typeCount = Count;
            if (typeIndex < 0 || typeIndex >= typeCount || leadershipWeights == null) {
                return;
            }

            leadershipWeights[typeIndex] = Mathf.Max(0f, weight);
        }

        /**
         * <summary>
         * Gets the avoidance weight for a fish type index.
         * </summary>
         * <param name="typeIndex">The fish type index.</param>
         * <returns>The avoidance weight, or the default if unavailable or non-positive.</returns>
         */
        public float GetAvoidanceWeight(int typeIndex) {
            int typeCount = Count;
            if (typeIndex < 0 || typeIndex >= typeCount || avoidanceWeights == null) {
                return defaultAvoidanceWeight;
            }

            float weight = avoidanceWeights[typeIndex];
            return weight > 0f ? weight : defaultAvoidanceWeight;
        }

        /**
         * <summary>
         * Sets the avoidance weight for a fish type index.
         * </summary>
         * <param name="typeIndex">The fish type index.</param>
         * <param name="weight">The weight value (clamped to non-negative).</param>
         */
        public void SetAvoidanceWeight(int typeIndex, float weight) {
            int typeCount = Count;
            if (typeIndex < 0 || typeIndex >= typeCount || avoidanceWeights == null) {
                return;
            }

            avoidanceWeights[typeIndex] = Mathf.Max(0f, weight);
        }

        /**
         * <summary>
         * Gets the neutral weight for a fish type index.
         * </summary>
         * <param name="typeIndex">The fish type index.</param>
         * <returns>The neutral weight, or the default if unavailable or non-positive.</returns>
         */
        public float GetNeutralWeight(int typeIndex) {
            int typeCount = Count;
            if (typeIndex < 0 || typeIndex >= typeCount || neutralWeights == null) {
                return defaultNeutralWeight;
            }

            float weight = neutralWeights[typeIndex];
            return weight > 0f ? weight : defaultNeutralWeight;
        }

        /**
         * <summary>
         * Sets the neutral weight for a fish type index.
         * </summary>
         * <param name="typeIndex">The fish type index.</param>
         * <param name="weight">The weight value (clamped to non-negative).</param>
         */
        public void SetNeutralWeight(int typeIndex, float weight) {
            int typeCount = Count;
            if (typeIndex < 0 || typeIndex >= typeCount || neutralWeights == null) {
                return;
            }

            neutralWeights[typeIndex] = Mathf.Max(0f, weight);
        }

        private static int GetMatrixIndex(int rowIndex, int columnIndex, int matrixSideLength) {
            return rowIndex * matrixSideLength + columnIndex;
        }

        private static int GetSquareMatrixSideLength<T>(T[] matrixValues) {
            if (matrixValues == null || matrixValues.Length <= 0) {
                return 0;
            }

            return Mathf.RoundToInt(Mathf.Sqrt(matrixValues.Length));
        }

        private static void EnsureMatrixSize<T>(ref T[] matrixValues, int requiredSize) {
            if (matrixValues != null && matrixValues.Length == requiredSize) {
                return;
            }

            matrixValues = new T[requiredSize];
        }

        private static void CopySquareMatrix<T>(
            T[] sourceMatrix,
            int sourceSideLength,
            T[] destinationMatrix,
            int destinationSideLength) {
            if (sourceMatrix == null || sourceSideLength <= 0) {
                return;
            }

            int copySideLength = Mathf.Min(sourceSideLength, destinationSideLength);
            for (int rowIndex = 0; rowIndex < copySideLength; rowIndex++) {
                for (int columnIndex = 0; columnIndex < copySideLength; columnIndex++) {
                    int sourceIndex = rowIndex * sourceSideLength + columnIndex;
                    int destinationIndex = rowIndex * destinationSideLength + columnIndex;
                    destinationMatrix[destinationIndex] = sourceMatrix[sourceIndex];
                }
            }
        }

        private static float[] EnsurePerTypeArraySize(float[] currentWeights, float[] previousWeights, int typeCount) {
            if (currentWeights != null && currentWeights.Length == typeCount) {
                return currentWeights;
            }

            float[] newWeights = new float[typeCount];
            if (previousWeights == null) {
                return newWeights;
            }

            int copyCount = Mathf.Min(previousWeights.Length, typeCount);
            for (int typeIndex = 0; typeIndex < copyCount; typeIndex++) {
                newWeights[typeIndex] = previousWeights[typeIndex];
            }

            return newWeights;
        }

        private void ApplyDefaultWeights(int typeCount) {
            for (int typeIndex = 0; typeIndex < typeCount; typeIndex++) {
                if (leadershipWeights[typeIndex] <= 0f) {
                    leadershipWeights[typeIndex] = defaultLeadershipWeight;
                }

                if (avoidanceWeights[typeIndex] <= 0f) {
                    avoidanceWeights[typeIndex] = defaultAvoidanceWeight;
                }

                if (neutralWeights[typeIndex] <= 0f) {
                    neutralWeights[typeIndex] = defaultNeutralWeight;
                }
            }
        }

        private void ClearRelationWhenInteractionDisabled(int typeCount, bool enabled, int indexAB, int indexBA) {
            // If interaction is turned off, clear relationship too.
            if (enabled || relationTypes == null || relationTypes.Length != typeCount * typeCount) {
                return;
            }

            relationTypes[indexAB] = FishRelationType.Neutral;
            relationTypes[indexBA] = FishRelationType.Neutral;
        }
    }
}
