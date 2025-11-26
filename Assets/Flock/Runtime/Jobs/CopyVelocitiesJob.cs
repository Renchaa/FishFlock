// File: Assets/Flock/Runtime/Jobs/CopyVelocitiesJob.cs
namespace Flock.Runtime.Jobs {
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct CopyVelocitiesJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float3> Source;
        [WriteOnly] public NativeArray<float3> Destination;

        public void Execute(int index) {
            Destination[index] = Source[index];
        }
    }
}
