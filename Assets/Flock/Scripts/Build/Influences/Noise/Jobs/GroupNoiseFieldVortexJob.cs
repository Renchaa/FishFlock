using Flock.Scripts.Build.Influence.Noise.Utilities;
using Flock.Scripts.Build.Influence.Noise.Data;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Noise.Jobs
{
    /**
     * <summary>
     * Generates per-cell noise directions using a vortex pattern centered in the grid.
     * </summary>
     */
    [BurstCompile]
    public struct GroupNoiseFieldVortexJob : IJobParallelFor
    {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;

        [ReadOnly] public int3 GridResolution;

        [ReadOnly] public FlockGroupNoiseCommonSettings Common;
        [ReadOnly] public GroupNoiseVortexPayload Payload;

        [NativeDisableParallelForRestriction] public NativeArray<float3> CellNoise;

        public void Execute(int index)
        {
            if (!CellNoise.IsCreated || (uint)index >= (uint)CellNoise.Length)
            {
                return;
            }

            int3 cell = GroupNoiseFieldMath.IndexToCell(index, GridResolution);
            float3 uvw = GroupNoiseFieldMath.CellToUVW(cell, GridResolution);

            float worldScale = math.max(0.001f, Common.WorldScale);
            float3 position = GroupNoiseFieldMath.UVWToP(uvw, worldScale);

            float timeScaled = GroupNoiseFieldMath.ComputeT(Time, Frequency, Common.BaseFrequency);

            float3 direction = EvaluateDirection(position, timeScaled, worldScale);
            CellNoise[index] = math.normalizesafe(direction, float3.zero);
        }

        private float3 EvaluateDirection(float3 position, float timeScaled, float worldScale)
        {
            float3 centerNorm = new float3(
                math.saturate(Payload.CenterNorm.x),
                math.saturate(Payload.CenterNorm.y),
                math.saturate(Payload.CenterNorm.z));

            float3 center = (centerNorm - 0.5f) * 2f * worldScale;

            float3 relative = position - center;
            float2 relativeXZ = new float2(relative.x, relative.z);

            float distance = math.length(relativeXZ);
            float radius = math.max(0f, Payload.Radius);

            if (distance < 1e-5f || radius <= 0f)
            {
                return new float3(0f, math.sin(timeScaled) * Payload.VerticalBias, 0f);
            }

            float radiusNormalised = math.saturate(distance / radius);
            float falloff = 1f - radiusNormalised;
            if (falloff <= 0f)
            {
                return float3.zero;
            }

            float2 tangent = new float2(-relativeXZ.y, relativeXZ.x) / distance;

            float tightness = math.max(0f, Payload.Tightness);
            float orbitSpeed = falloff * (1f + tightness);

            float vertical = (0.5f - radiusNormalised) * Payload.VerticalBias;

            return new float3(
                tangent.x * orbitSpeed,
                vertical,
                tangent.y * orbitSpeed);
        }
    }
}
