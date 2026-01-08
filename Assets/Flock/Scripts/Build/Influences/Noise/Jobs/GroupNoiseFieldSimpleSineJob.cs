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
     * Generates per-cell noise directions using a simple sine-based field with optional XZ swirl.
     * </summary>
     */
    [BurstCompile]
    public struct GroupNoiseFieldSimpleSineJob : IJobParallelFor
    {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;

        [ReadOnly] public int3 GridResolution;

        [ReadOnly] public FlockGroupNoiseCommonSettings Common;
        [ReadOnly] public FlockGroupNoiseSimpleSinePayload Payload;

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
            float3 p = GroupNoiseFieldMath.UVWToP(uvw, worldScale);

            float timeScaled = GroupNoiseFieldMath.ComputeT(Time, Frequency, Common.BaseFrequency);

            uint seed = Common.Seed == 0u ? 1u : Common.Seed;
            float3 phase = GroupNoiseFieldMath.ComputeHashPhase(seed, cell);

            float3 direction = EvaluateDirection(p, timeScaled, phase);
            CellNoise[index] = math.normalizesafe(direction, float3.zero);
        }

        private float3 EvaluateDirection(float3 position, float timeScaled, float3 phase)
        {
            float3 timeVector = new float3(
                timeScaled * Common.TimeScale.x,
                timeScaled * Common.TimeScale.y,
                timeScaled * Common.TimeScale.z);

            float3 totalPhase = position + timeVector + Common.PhaseOffset + phase;

            float3 direction = new float3(
                math.sin(totalPhase.x),
                math.sin(totalPhase.y),
                math.sin(totalPhase.z));

            ApplySwirl(ref direction, position);

            return direction;
        }

        private void ApplySwirl(ref float3 direction, float3 position)
        {
            float swirlStrength = math.max(0f, Payload.SwirlStrength);
            if (swirlStrength <= 0f)
            {
                return;
            }

            float2 swirlPosition = new float2(position.x, position.z);
            float lengthSquared = math.lengthsq(swirlPosition);
            if (lengthSquared <= 1e-6f)
            {
                return;
            }

            float inverseLength = math.rsqrt(lengthSquared);
            float2 tangent = new float2(-swirlPosition.y, swirlPosition.x) * inverseLength;

            direction.x += tangent.x * swirlStrength;
            direction.z += tangent.y * swirlStrength;
        }
    }
}
