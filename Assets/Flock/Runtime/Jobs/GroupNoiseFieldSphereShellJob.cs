using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    [BurstCompile]
    public struct GroupNoiseFieldSphereShellJob : IJobParallelFor {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;
        [ReadOnly] public int3 GridResolution;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        [ReadOnly] public FlockGroupNoiseCommonSettings Common;
        [ReadOnly] public FlockGroupNoiseSphereShellPayload Payload;

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

        float3 Evaluate(float3 p, float t, float3 hashPhase) {
            float worldScale = math.max(0.001f, Common.WorldScale);

            float3 centerNorm = new float3(
                math.saturate(Payload.CenterNorm.x),
                math.saturate(Payload.CenterNorm.y),
                math.saturate(Payload.CenterNorm.z));

            float3 center = (centerNorm - 0.5f) * 2f * worldScale;

            float radius = math.max(0f, Payload.Radius);
            float thickness = math.max(0.001f, Payload.Thickness);

            float3 rel = p - center;
            float dist = math.length(rel);

            if (dist < 1e-5f || radius <= 0f) {
                return new float3(0f, 1f, 0f);
            }

            float3 radialDir = rel / dist;

            float inner = math.max(radius - thickness, 0f);
            float outer = radius + thickness;

            float radialScalar;

            if (dist < inner) {
                float tInner = math.saturate((inner - dist) / thickness);
                radialScalar = +math.lerp(0.5f, 1f, tInner);
            } else if (dist > outer) {
                float tOuter = math.saturate((dist - outer) / thickness);
                radialScalar = -math.lerp(0.5f, 1f, tOuter);
            } else {
                float delta = dist - radius;
                float norm = math.saturate(math.abs(delta) / thickness);
                float soften = 1f - norm;
                float sign = delta > 0f ? -1f : 1f;
                radialScalar = sign * soften * 0.35f;
            }

            float3 radial = radialDir * radialScalar;

            float3 any = math.abs(radialDir.y) < 0.8f
                ? new float3(0f, 1f, 0f)
                : new float3(1f, 0f, 0f);

            float3 tangent1 = math.normalizesafe(math.cross(radialDir, any), float3.zero);
            float3 tangent2 = math.normalizesafe(math.cross(radialDir, tangent1), float3.zero);

            float angle = t + hashPhase.x;
            float sa = math.sin(angle);
            float ca = math.cos(angle);

            float3 swirlDir = tangent1 * ca + tangent2 * sa;

            float distFromRadius = math.abs(dist - radius);
            float swirlFalloff = 1f - math.saturate(distFromRadius / (thickness * 2f));

            float swirlStrength = math.max(0f, Payload.SwirlStrength) * swirlFalloff;
            float3 swirl = swirlDir * swirlStrength;

            float3 dir = radial + swirl;
            return math.normalizesafe(dir, radialDir);
        }
    }
}
