using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    [BurstCompile]
    public struct GroupNoiseFieldSimpleSineJob : IJobParallelFor {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;
        [ReadOnly] public int3 GridResolution;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        [ReadOnly] public FlockGroupNoiseCommonSettings Common;
        [ReadOnly] public FlockGroupNoiseSimpleSinePayload Payload;

        public void Execute(int index) {
            if (!CellNoise.IsCreated || (uint)index >= (uint)CellNoise.Length) {
                return;
            }

            int3 cell = GroupNoiseFieldMath.IndexToCell(index, GridResolution);
            float3 uvw = GroupNoiseFieldMath.CellToUVW(cell, GridResolution);
            float3 p = GroupNoiseFieldMath.UVWToP(uvw, math.max(0.001f, Common.WorldScale));
            float t = GroupNoiseFieldMath.ComputeT(Time, Frequency, Common.BaseFrequency);
            float3 phase = GroupNoiseFieldMath.ComputeHashPhase(Common.Seed == 0u ? 1u : Common.Seed, cell);

            float3 dir = Evaluate(p, t, phase);
            CellNoise[index] = math.normalizesafe(dir, float3.zero);
        }

        float3 Evaluate(float3 p, float t, float3 phase) {
            float3 timeVec = new float3(
                t * Common.TimeScale.x,
                t * Common.TimeScale.y,
                t * Common.TimeScale.z);

            float3 totalPhase = p + timeVec + Common.PhaseOffset + phase;

            float3 dir = new float3(
                math.sin(totalPhase.x),
                math.sin(totalPhase.y),
                math.sin(totalPhase.z));

            float swirl = math.max(0f, Payload.SwirlStrength);
            if (swirl > 0f) {
                float2 swirlPos = new float2(p.x, p.z);
                float lenSq = math.lengthsq(swirlPos);
                if (lenSq > 1e-6f) {
                    float2 tangent = new float2(-swirlPos.y, swirlPos.x) * math.rsqrt(lenSq);
                    dir.x += tangent.x * swirl;
                    dir.z += tangent.y * swirl;
                }
            }

            return dir;
        }
    }
}
