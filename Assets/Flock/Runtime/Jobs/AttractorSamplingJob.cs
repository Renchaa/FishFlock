// ==========================================
// 10) NEW JOB: AttractorSamplingJob
// File: Assets/Flock/Runtime/Jobs/AttractorSamplingJob.cs
// ==========================================
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct AttractorSamplingJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<int> BehaviourIds;

        [ReadOnly] public NativeArray<FlockAttractorData> Attractors;
        [ReadOnly] public NativeArray<float> BehaviourAttractionWeight;

        [ReadOnly] public int AttractorCount;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> AttractionSteering;

        public void Execute(int index) {
            float3 position = Positions[index];

            int behaviourIndex = BehaviourIds[index];
            if ((uint)behaviourIndex >= (uint)BehaviourAttractionWeight.Length) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            float typeResponse = BehaviourAttractionWeight[behaviourIndex];
            if (typeResponse <= 0.0f || AttractorCount <= 0) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            float3 bestDir = float3.zero;
            float bestStrength = 0.0f;

            // Inner region fraction: inside this portion of the radius/box, we do NOT pull to centre.
            // Only when fish are near the boundary we gently push them back inside.
            const float InnerRegionFraction = 0.6f; // 0.6 = inner 60% of volume is "free roaming"

            for (int i = 0; i < AttractorCount; i += 1) {
                FlockAttractorData data = Attractors[i];

                // mask check: only affect selected fish types
                if (behaviourIndex < 32) {
                    uint bit = 1u << behaviourIndex;
                    if (bit != 0u && data.AffectedTypesMask != uint.MaxValue) {
                        if ((data.AffectedTypesMask & bit) == 0u) {
                            continue;
                        }
                    }
                }

                float3 offset;
                float distance;
                float t; // 0 at center, 1 at boundary

                if (data.Shape == FlockAttractorShape.Sphere) {
                    offset = data.Position - position;
                    distance = math.length(offset);

                    float radius = math.max(data.Radius, 0.0001f);

                    // Only affect fish inside the sphere
                    if (distance > radius) {
                        continue;
                    }

                    t = math.saturate(distance / radius); // 0 center, 1 edge
                } else {
                    // Box: transform to local space and compute normalised distance to faces
                    float3 toCenter = position - data.Position;
                    quaternion invRot = math.conjugate(data.BoxRotation);
                    float3 local = math.mul(invRot, toCenter);
                    float3 absLocal = math.abs(local);

                    float3 halfExtents = data.BoxHalfExtents;
                    halfExtents = math.max(halfExtents, new float3(0.0001f));

                    float3 normalised = absLocal / halfExtents;
                    float maxComponent = math.max(normalised.x, math.max(normalised.y, normalised.z));

                    // Only affect fish inside the box
                    if (maxComponent > 1.0f) {
                        continue;
                    }

                    t = math.saturate(maxComponent); // 0 center, 1 on faces

                    // For steering direction we still use world-space center
                    offset = data.Position - position;
                    distance = math.length(offset);
                }

                // Inside inner region: NO radial pull -> they just free-roam via flocking/avoidance.
                if (t <= InnerRegionFraction) {
                    continue;
                }

                // Map [InnerRegionFraction..1] -> [0..1] for the falloff curve near the boundary
                float denom = math.max(1.0f - InnerRegionFraction, 0.0001f);
                float edgeT = (t - InnerRegionFraction) / denom; // 0 at inner border, 1 at outer surface

                float falloffPower = math.max(0.1f, data.FalloffPower);

                // Stronger pull closer to the boundary, nothing deep inside:
                float falloff = math.pow(edgeT, falloffPower); // 0 at inner border, 1 at boundary
                float strength = data.BaseStrength * typeResponse * falloff;

                if (strength <= 0.0f) {
                    continue;
                }

                if (distance < 1e-4f) {
                    continue;
                }

                float3 dir = offset / distance; // still “towards interior”, but only when near edge

                if (strength > bestStrength) {
                    bestStrength = strength;
                    bestDir = dir;
                }
            }

            if (bestStrength > 0.0f) {
                AttractionSteering[index] = bestDir * bestStrength;
            } else {
                AttractionSteering[index] = float3.zero;
            }
        }
    }
}
