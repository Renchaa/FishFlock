// File: Assets/Flock/Runtime/Data/NeighbourAggregate.cs
namespace Flock.Runtime.Data {
    using Unity.Mathematics;

    /// <summary>
    /// Per-agent neighbour aggregation output (Phase 2).
    /// This contains the accumulated sums/counts that were previously local variables in FlockStepJob.Execute().
    /// </summary>
    public struct NeighbourAggregate {
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
