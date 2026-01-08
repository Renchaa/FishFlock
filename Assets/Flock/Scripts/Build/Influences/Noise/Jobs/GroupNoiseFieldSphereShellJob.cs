using Flock.Scripts.Build.Influence.Noise.Data;
using Flock.Scripts.Build.Influence.Noise.Utilities;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Noise.Jobs {
    /**
     * <summary>
     * Generates per-cell noise directions that form a spherical shell field with radial push/pull and tangential swirl.
     * </summary>
     */
    [BurstCompile]
    public struct GroupNoiseFieldSphereShellJob : IJobParallelFor {
        [ReadOnly]
        public float Time;

        [ReadOnly]
        public float Frequency;

        [ReadOnly]
        public int3 GridResolution;

        [ReadOnly]
        public FlockGroupNoiseCommonSettings Common;

        [ReadOnly]
        public FlockGroupNoiseSphereShellPayload Payload;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        public void Execute(int index) {
            if (!CellNoise.IsCreated || (uint)index >= (uint)CellNoise.Length) {
                return;
            }

            int3 cell = GroupNoiseFieldMath.IndexToCell(index, GridResolution);
            float3 uvw = GroupNoiseFieldMath.CellToUVW(cell, GridResolution);

            float worldScale = math.max(0.001f, Common.WorldScale);
            float3 position = GroupNoiseFieldMath.UVWToP(uvw, worldScale);

            float timeScaled = GroupNoiseFieldMath.ComputeT(Time, Frequency, Common.BaseFrequency);

            uint seed = Common.Seed == 0u ? 1u : Common.Seed;
            float3 hashPhase = GroupNoiseFieldMath.ComputeHashPhase(seed, cell);

            float3 direction = EvaluateDirection(position, timeScaled, hashPhase);
            CellNoise[index] = math.normalizesafe(direction, float3.zero);
        }

        private float3 EvaluateDirection(float3 position, float timeScaled, float3 hashPhase) {
            float worldScale = math.max(0.001f, Common.WorldScale);

            float3 centerNormalised = new float3(
                math.saturate(Payload.CenterNorm.x),
                math.saturate(Payload.CenterNorm.y),
                math.saturate(Payload.CenterNorm.z));

            float3 center = (centerNormalised - 0.5f) * 2f * worldScale;

            float radius = math.max(0f, Payload.Radius);
            float thickness = math.max(0.001f, Payload.Thickness);

            float3 relative = position - center;
            float distance = math.length(relative);

            if (distance < 1e-5f || radius <= 0f) {
                return new float3(0f, 1f, 0f);
            }

            float3 radialDirection = relative / distance;

            float innerRadius = math.max(radius - thickness, 0f);
            float outerRadius = radius + thickness;

            float radialScalar = ComputeRadialScalar(distance, radius, thickness, innerRadius, outerRadius);
            float3 radial = radialDirection * radialScalar;

            float3 swirlDirection = ComputeSwirlDirection(radialDirection, timeScaled, hashPhase.x);

            float distanceFromRadius = math.abs(distance - radius);
            float swirlFalloff = 1f - math.saturate(distanceFromRadius / (thickness * 2f));

            float swirlStrength = math.max(0f, Payload.SwirlStrength) * swirlFalloff;
            float3 swirl = swirlDirection * swirlStrength;

            float3 combined = radial + swirl;
            return math.normalizesafe(combined, radialDirection);
        }

        private float ComputeRadialScalar(
            float distance,
            float radius,
            float thickness,
            float innerRadius,
            float outerRadius) {
            if (distance < innerRadius) {
                float innerT = math.saturate((innerRadius - distance) / thickness);
                return +math.lerp(0.5f, 1f, innerT);
            }

            if (distance > outerRadius) {
                float outerT = math.saturate((distance - outerRadius) / thickness);
                return -math.lerp(0.5f, 1f, outerT);
            }

            float delta = distance - radius;
            float normalised = math.saturate(math.abs(delta) / thickness);
            float soften = 1f - normalised;

            float sign = delta > 0f ? -1f : 1f;
            return sign * soften * 0.35f;
        }

        private float3 ComputeSwirlDirection(float3 radialDirection, float timeScaled, float hashPhaseX) {
            float3 referenceAxis = math.abs(radialDirection.y) < 0.8f
                ? new float3(0f, 1f, 0f)
                : new float3(1f, 0f, 0f);

            float3 tangent1 = math.normalizesafe(math.cross(radialDirection, referenceAxis), float3.zero);
            float3 tangent2 = math.normalizesafe(math.cross(radialDirection, tangent1), float3.zero);

            float angle = timeScaled + hashPhaseX;
            float sine = math.sin(angle);
            float cosine = math.cos(angle);

            return tangent1 * cosine + tangent2 * sine;
        }
    }
}
