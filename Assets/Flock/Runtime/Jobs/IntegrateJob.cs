// File: Assets/Flock/Runtime/Jobs/IntegrateJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct IntegrateJob : IJobParallelFor {
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<float3> Velocities;

        [ReadOnly]
        public FlockEnvironmentData EnvironmentData;

        [ReadOnly]
        public float DeltaTime;

        public void Execute(int index) {
            float3 position = Positions[index];
            float3 velocity = Velocities[index];

            position += velocity * DeltaTime;

            Positions[index] = position;
        }
    }
}
