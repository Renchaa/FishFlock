using Flock.Scripts.Build.Influence.Environment.Attractors.Data;

using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;

namespace Flock.Scripts.Build.Influence.Environment.Attractors.Jobs
{
    /**
     * <summary>
     * Applies indexed attractor changes to the authoritative runtime attractor array and refreshes
     * each updated attractor's normalised depth range in environment space.
     * </summary>
     */
    [BurstCompile]
    public struct ApplyIndexedAttractorChangesJob : IJobParallelFor
    {
        private const float MinimumEnvironmentHeight = 0.0001f;

        [ReadOnly] public NativeArray<IndexedAttractorChange> Changes;

        // Authoritative runtime array being edited.
        public NativeArray<FlockAttractorData> Attractors;

        // Environment vertical normalisation inputs (world -> [0..1]).
        public float EnvMinY;
        public float EnvHeight;

        public void Execute(int index)
        {
            IndexedAttractorChange change = Changes[index];

            int attractorIndex = change.Index;
            if ((uint)attractorIndex >= (uint)Attractors.Length)
            {
                return;
            }

            FlockAttractorData data = change.Data;

            float inverseEnvironmentHeight = 1.0f / math.max(EnvHeight, MinimumEnvironmentHeight);

            GetWorldVerticalSpan(data, out float worldMinY, out float worldMaxY);

            float depthMinNormalised = math.saturate((worldMinY - EnvMinY) * inverseEnvironmentHeight);
            float depthMaxNormalised = math.saturate((worldMaxY - EnvMinY) * inverseEnvironmentHeight);

            if (depthMaxNormalised < depthMinNormalised)
            {
                float temporary = depthMinNormalised;
                depthMinNormalised = depthMaxNormalised;
                depthMaxNormalised = temporary;
            }

            data.DepthMinNorm = depthMinNormalised;
            data.DepthMaxNorm = depthMaxNormalised;

            Attractors[attractorIndex] = data;
        }

        private static void GetWorldVerticalSpan(
            FlockAttractorData data,
            out float worldMinY,
            out float worldMaxY)
        {
            if (data.Shape == FlockAttractorShape.Sphere)
            {
                float radius = math.max(0f, data.Radius);
                worldMinY = data.Position.y - radius;
                worldMaxY = data.Position.y + radius;
                return;
            }

            float extentY = GetBoxProjectedExtentY(data.BoxRotation, data.BoxHalfExtents);
            worldMinY = data.Position.y - extentY;
            worldMaxY = data.Position.y + extentY;
        }

        private static float GetBoxProjectedExtentY(quaternion rotation, float3 halfExtents)
        {
            float3 right = math.mul(rotation, new float3(1f, 0f, 0f));
            float3 up = math.mul(rotation, new float3(0f, 1f, 0f));
            float3 forward = math.mul(rotation, new float3(0f, 0f, 1f));

            return
                math.abs(right.y) * halfExtents.x
                + math.abs(up.y) * halfExtents.y
                + math.abs(forward.y) * halfExtents.z;
        }
    }
}
