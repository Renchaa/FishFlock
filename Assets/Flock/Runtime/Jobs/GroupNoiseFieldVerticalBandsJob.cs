using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    [BurstCompile]
    public struct GroupNoiseFieldVerticalBandsJob : IJobParallelFor {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;
        [ReadOnly] public int3 GridResolution;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        [ReadOnly] public FlockGroupNoiseCommonSettings Common;
        [ReadOnly] public FlockGroupNoiseVerticalBandsPayload Payload;

        public void Execute(int index) {
            if (!CellNoise.IsCreated || (uint)index >= (uint)CellNoise.Length) {
                return;
            }

            int3 cell = GroupNoiseFieldMath.IndexToCell(index, GridResolution);
            float3 uvw = GroupNoiseFieldMath.CellToUVW(cell, GridResolution);
            float3 p = GroupNoiseFieldMath.UVWToP(uvw, math.max(0.001f, Common.WorldScale));
            float t = GroupNoiseFieldMath.ComputeT(Time, Frequency, Common.BaseFrequency);
            float3 phase = GroupNoiseFieldMath.ComputeHashPhase(Common.Seed == 0u ? 1u : Common.Seed, cell);

            float3 dir = Evaluate(p, uvw, t, phase);
            CellNoise[index] = math.normalizesafe(dir, float3.zero);
        }

        float3 Evaluate(float3 p, float3 uvw, float t, float3 phase) {
            float heightPhase = (uvw.y - 0.5f) * GroupNoiseFieldMath.TwoPi + t + phase.y;
            float verticalWave = math.sin(heightPhase);

            return new float3(
                math.sin(p.x + phase.x + t),
                verticalWave + Payload.VerticalBias,
                math.sin(p.z + phase.z - t));
        }
    }
}
