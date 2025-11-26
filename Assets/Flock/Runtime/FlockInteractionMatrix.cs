// File: Assets/Flock/Runtime/FlockInteractionMatrix.cs
namespace Flock.Runtime {
    using System;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "FlockInteractionMatrix",
        menuName = "Flock/Flock Interaction Matrix")]
    public sealed class FlockInteractionMatrix : ScriptableObject {
        [Serializable]
        public struct Row {
            public FishTypePreset fishType;

            [Min(0f)]
            public float leadershipWeight;

            /// <summary>
            /// Types this fish type is allowed to school with.
            /// Conceptually this is one row of the interaction matrix.
            /// </summary>
            public FishTypePreset[] interactsWith;
        }

        [SerializeField]
        Row[] rows;

        public Row[] Rows => rows;

        public bool TryGetRowForPreset(
            FishTypePreset preset,
            out Row row,
            out int rowIndex) {

            if (preset != null && rows != null) {
                for (int i = 0; i < rows.Length; i += 1) {
                    if (rows[i].fishType == preset) {
                        row = rows[i];
                        rowIndex = i;
                        return true;
                    }
                }
            }

            row = default;
            rowIndex = -1;
            return false;
        }
    }   
}
