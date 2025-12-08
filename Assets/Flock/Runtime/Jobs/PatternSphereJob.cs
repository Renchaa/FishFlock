// File: Assets/Flock/Runtime/Jobs/PatternSphereJob.cs
// FULL struct – updated so it COMBINES with other layer-3 patterns instead of wiping them
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /// <summary>
    /// Layer-3 pattern: soft radial constraint towards a spherical band.
    /// No global swirl – tangential motion comes from wander / group-noise / boids.
    /// </summary>
    [BurstCompile]
    public struct PatternSphereJob : IJobParallelFor, IFlockLayer3PatternJob {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<int> BehaviourIds;

        // Written by this job, used in FlockStepJob together with BehaviourPatternWeight & GlobalPatternMultiplier
        public NativeArray<float3> PatternSteering;

        [ReadOnly] public float3 Center;
        [ReadOnly] public float Radius;       // target radius of the band
        [ReadOnly] public float Thickness;    // half-width of the band
        [ReadOnly] public float Strength;     // overall intensity (before per-behaviour weights)
        [ReadOnly] public uint BehaviourMask; // bitmask over behaviour indices

        // Shared wiring for all layer-3 jobs via IFlockLayer3PatternJob
        public void SetCommonData(
            NativeArray<float3> positions,
            NativeArray<int> behaviourIds,
            NativeArray<float3> patternSteering,
            uint behaviourMask,
            float strength) {

            Positions = positions;
            BehaviourIds = behaviourIds;
            PatternSteering = patternSteering;
            BehaviourMask = behaviourMask;
            Strength = strength;
        }

        public void Execute(int index) {
            if (!PatternSteering.IsCreated
                || !Positions.IsCreated
                || index < 0
                || index >= Positions.Length
                || index >= PatternSteering.Length) {
                return;
            }

            // If this pattern is disabled → do nothing (DO NOT zero existing steering)
            if (Radius <= 0f || Strength <= 0f || Thickness <= 0f) {
                return;
            }

            // Per-pattern behaviour mask
            if (BehaviourIds.IsCreated) {
                int behaviourIndex = 0;

                if (index >= 0 && index < BehaviourIds.Length) {
                    behaviourIndex = BehaviourIds[index];
                }

                // BehaviourMask == uint.MaxValue → affect all behaviours
                if (BehaviourMask != uint.MaxValue) {
                    if (behaviourIndex < 0 || behaviourIndex >= 32) {
                        return;
                    }

                    uint bit = 1u << behaviourIndex;
                    if ((BehaviourMask & bit) == 0u) {
                        return;
                    }
                }
            }

            float3 pos = Positions[index];
            float3 rel = pos - Center;
            float dist = math.length(rel);

            // If exactly at center – no radial direction, so skip (keep whatever other patterns did)
            if (dist < 1e-5f) {
                return;
            }

            float3 radialDir = rel / dist;

            // Band [inner, outer] where fish are "happy"
            float inner = math.max(Radius - Thickness, 0f);
            float outer = Radius + Thickness;
            float bandWidth = math.max(Thickness, 0.0001f);

            float radialScalar = 0f;

            if (dist < inner) {
                // Too close to center → push outward
                float tInner = math.saturate((inner - dist) / bandWidth);
                radialScalar = +tInner;
            } else if (dist > outer) {
                // Too far outside → pull inward
                float tOuter = math.saturate((dist - outer) / bandWidth);
                radialScalar = -tOuter;
            } else {
                // Inside the comfortable band: soft bias toward exact Radius
                float delta = dist - Radius;                          // -Thickness..+Thickness
                float norm = math.saturate(math.abs(delta) / bandWidth);
                float soften = 1f - norm;                             // 1 near Radius, 0 near edges
                float sign = delta > 0f ? -1f : 1f;                   // inwards/outwards
                radialScalar = sign * soften * 0.25f;                 // weak correction
            }

            // IMPORTANT: accumulate, don't overwrite –
            // this lets multiple layer-3 patterns stack in PatternSteering.
            PatternSteering[index] += radialDir * radialScalar * Strength;
        }
    }
}
