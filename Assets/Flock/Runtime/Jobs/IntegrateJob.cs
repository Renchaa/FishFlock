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

            // Basic integration
            position += velocity * DeltaTime;

            // --- Bounds: position-only clamping (no velocity changes here) ---
            if (EnvironmentData.BoundsType == FlockBoundsType.Box) {
                float3 min = EnvironmentData.BoundsCenter - EnvironmentData.BoundsExtents;
                float3 max = EnvironmentData.BoundsCenter + EnvironmentData.BoundsExtents;

                position = math.clamp(position, min, max);
            } else if (EnvironmentData.BoundsType == FlockBoundsType.Sphere) {
                float3 offset = position - EnvironmentData.BoundsCenter;
                float distSq = math.lengthsq(offset);
                float radius = EnvironmentData.BoundsRadius;

                if (radius > 0f && distSq > radius * radius) {
                    float dist = math.sqrt(distSq);
                    float3 dir = offset / math.max(dist, 1e-4f);

                    // Pull slightly inside to avoid jitter on exact surface
                    position = EnvironmentData.BoundsCenter + dir * radius * 0.999f;
                }
            }

            Positions[index] = position;
        }

    }
}
