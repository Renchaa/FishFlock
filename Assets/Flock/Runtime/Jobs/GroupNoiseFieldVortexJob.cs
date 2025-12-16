using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    [BurstCompile]
    public struct GroupNoiseFieldVortexJob : IJobParallelFor {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;
        [ReadOnly] public int3 GridResolution;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        [ReadOnly] public FlockGroupNoiseCommonSettings Common;
        [ReadOnly] public FlockGroupNoiseVortexPayload Payload;

        public void Execute(int index) {
            if (!CellNoise.IsCreated || (uint)index >= (uint)CellNoise.Length) {
                return;
            }

            int3 cell = GroupNoiseFieldMath.IndexToCell(index, GridResolution);
            float3 uvw = GroupNoiseFieldMath.CellToUVW(cell, GridResolution);
            float3 p = GroupNoiseFieldMath.UVWToP(uvw, math.max(0.001f, Common.WorldScale));
            float t = GroupNoiseFieldMath.ComputeT(Time, Frequency, Common.BaseFrequency);

            float3 dir = Evaluate(p, t);
            CellNoise[index] = math.normalizesafe(dir, float3.zero);
        }

        float3 Evaluate(float3 p, float t) {
            float3 centerNorm = new float3(
                math.saturate(Payload.CenterNorm.x),
                math.saturate(Payload.CenterNorm.y),
                math.saturate(Payload.CenterNorm.z));

            float3 center = (centerNorm - 0.5f) * 2f * math.max(0.001f, Common.WorldScale);

            float3 rel = p - center;
            float2 relXZ = new float2(rel.x, rel.z);

            float dist = math.length(relXZ);
            float radius = math.max(0f, Payload.Radius);

            if (dist < 1e-5f || radius <= 0f) {
                return new float3(0f, math.sin(t) * Payload.VerticalBias, 0f);
            }

            float radiusNorm = math.saturate(dist / radius);
            float falloff = 1f - radiusNorm;
            if (falloff <= 0f) {
                return float3.zero;
            }

            float2 tangent = new float2(-relXZ.y, relXZ.x) / dist;

            float orbitSpeed = falloff * (1f + math.max(0f, Payload.Tightness));
            float vertical = (0.5f - radiusNorm) * Payload.VerticalBias;

            return new float3(
                tangent.x * orbitSpeed,
                vertical,
                tangent.y * orbitSpeed);
        }
    }
}
