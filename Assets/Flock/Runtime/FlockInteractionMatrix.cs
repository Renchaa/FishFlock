using System;
using UnityEngine;

namespace Flock.Runtime {
    /**
     * <summary>
     * ScriptableObject that stores a row-based interaction definition between fish types.
     * </summary>
     */
    [CreateAssetMenu(
        fileName = "FlockInteractionMatrix",
        menuName = "Flock/Flock Interaction Matrix")]
    public sealed class FlockInteractionMatrix : ScriptableObject {
        [SerializeField]
        private Row[] rows;

        /**
         * <summary>
         * Gets the configured interaction rows.
         * </summary>
         */
        public Row[] Rows => rows;

        /**
         * <summary>
         * Tries to find the row associated with a given fish type preset.
         * </summary>
         * <param name="preset">The fish type preset to search for.</param>
         * <param name="row">The matching row when found; otherwise default.</param>
         * <param name="rowIndex">The index of the matching row when found; otherwise -1.</param>
         * <returns>True if a matching row was found; otherwise false.</returns>
         */
        public bool TryGetRowForPreset(
            FishTypePreset preset,
            out Row row,
            out int rowIndex) {
            int foundRowIndex = GetRowIndexForPreset(preset);
            if (foundRowIndex < 0) {
                row = default;
                rowIndex = -1;
                return false;
            }

            row = rows[foundRowIndex];
            rowIndex = foundRowIndex;
            return true;
        }

        // Finds a row index by preset reference, or -1 when not found.
        private int GetRowIndexForPreset(FishTypePreset preset) {
            if (preset == null || rows == null) {
                return -1;
            }

            for (int rowSearchIndex = 0; rowSearchIndex < rows.Length; rowSearchIndex += 1) {
                if (rows[rowSearchIndex].fishType == preset) {
                    return rowSearchIndex;
                }
            }

            return -1;
        }

        /**
         * <summary>
         * Represents a single fish type's interaction configuration.
         * </summary>
         */
        [Serializable]
        public struct Row {
            [Tooltip("The fish type this row applies to.")]
            public FishTypePreset fishType;

            [Tooltip("Leadership weight for this fish type.")]
            [Min(0f)]
            public float leadershipWeight;

            /**
             * <summary>
             * Fish types this fish type is allowed to school with.
             * Conceptually this is one row of the interaction matrix.
             * </summary>
             */
            public FishTypePreset[] interactsWith;
        }
    }
}
