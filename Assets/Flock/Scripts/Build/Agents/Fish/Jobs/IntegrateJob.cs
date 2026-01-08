using Flock.Scripts.Build.Influence.Environment.Bounds.Data;
using Flock.Scripts.Build.Influence.Environment.Data;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Agents.Fish.Jobs
{
    /**
     * <summary>
     * Integrates positions using velocities and clamps positions to the configured environment bounds.
     * </summary>
     */
    [BurstCompile]
    public struct IntegrateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Velocities;
        [ReadOnly] public FlockEnvironmentData EnvironmentData;
        [ReadOnly] public float DeltaTime;

        public NativeArray<float3> Positions;

        private const float MinimumDirectionLength = 1e-4f;
        private const float SurfaceInsetFactor = 0.999f;

        public void Execute(int index)
        {
            float3 position = Positions[index];
            position += Velocities[index] * DeltaTime;

            position = ClampPositionToBounds(position, EnvironmentData);

            Positions[index] = position;
        }

        private static float3 ClampPositionToBounds(float3 position, FlockEnvironmentData environmentData)
        {
            if (environmentData.BoundsType == FlockBoundsType.Box)
            {
                return ClampToBox(position, environmentData.BoundsCenter, environmentData.BoundsExtents);
            }

            if (environmentData.BoundsType == FlockBoundsType.Sphere)
            {
                return ClampToSphere(position, environmentData.BoundsCenter, environmentData.BoundsRadius);
            }

            return position;
        }

        private static float3 ClampToBox(float3 position, float3 boundsCenter, float3 boundsExtents)
        {
            float3 minimumBounds = boundsCenter - boundsExtents;
            float3 maximumBounds = boundsCenter + boundsExtents;

            return math.clamp(position, minimumBounds, maximumBounds);
        }

        private static float3 ClampToSphere(float3 position, float3 boundsCenter, float boundsRadius)
        {
            float3 offsetFromCenter = position - boundsCenter;
            float distanceSquared = math.lengthsq(offsetFromCenter);

            if (boundsRadius <= 0f || distanceSquared <= boundsRadius * boundsRadius)
            {
                return position;
            }

            float distance = math.sqrt(distanceSquared);
            float3 direction = offsetFromCenter / math.max(distance, MinimumDirectionLength);

            // Pull slightly inside to avoid jitter on exact surface.
            return boundsCenter + direction * boundsRadius * SurfaceInsetFactor;
        }
    }
}
