using Unity.Mathematics;

namespace Flock.Scripts.Build.Agents.Fish.Data
{
    /**
     * <summary>
     * Per-agent neighbour aggregation output.
     * Contains accumulated sums and counts consumed by steering integration.
     * </summary>
     */
    public struct NeighbourAggregate
    {
        public float3 AlignmentSum;
        public float3 CohesionSum;
        public float3 SeparationSum;
        public float3 AvoidSeparationSum;
        public float3 RadialDamping;

        public int LeaderNeighbourCount;
        public int SeparationCount;
        public int FriendlyNeighbourCount;

        public float AlignmentWeightSum;
        public float CohesionWeightSum;
        public float AvoidDanger;
    }
}
