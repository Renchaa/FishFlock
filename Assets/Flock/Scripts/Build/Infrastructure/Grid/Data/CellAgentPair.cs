using System;

namespace Flock.Scripts.Build.Infrastructure.Grid.Data {
    /**
     * <summary>
     * Pair of (cell id, agent index) used for sorting and building per-cell agent ranges.
     * Sorting is by <see cref="CellId"/>, then <see cref="AgentIndex"/>.
     * </summary>
     */
    public struct CellAgentPair : IComparable<CellAgentPair> {
        public int CellId;

        public int AgentIndex;

        public int CompareTo(CellAgentPair other) {
            int cellComparison = CellId.CompareTo(other.CellId);
            return cellComparison != 0 ? cellComparison : AgentIndex.CompareTo(other.AgentIndex);
        }
    }
}