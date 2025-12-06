// File: Assets/Flock/Runtime/Jobs/PatternSphereJob.cs
namespace Flock.Runtime.Jobs {
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /// <summary>
    /// Layer-3 pattern: soft radial constraint towards a spherical band.
    /// No global swirl – tangential motion comes from wander / group-noise / boids.
    /// </summary>
    [BurstCompile]
    public struct PatternSphereJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float3> Positions;

        // Written by this job, used in FlockStepJob together with BehaviourPatternWeight & GlobalPatternMultiplier
        public NativeArray<float3> PatternSteering;

        [ReadOnly] public float3 Center;
        [ReadOnly] public float Radius;     // target radius of the band
        [ReadOnly] public float Thickness;  // half-width of the band
        [ReadOnly] public float Strength;   // overall intensity (before per-behaviour weights)

        public void Execute(int index) {
            if (!PatternSteering.IsCreated
                || !Positions.IsCreated
                || index < 0
                || index >= Positions.Length
                || index >= PatternSteering.Length) {
                return;
            }

            // If sphere is disabled – do nothing
            if (Radius <= 0f || Strength <= 0f || Thickness <= 0f) {
                PatternSteering[index] = float3.zero;
                return;
            }

            float3 pos = Positions[index];
            float3 rel = pos - Center;
            float dist = math.length(rel);

            if (dist < 1e-5f) {
                // Exactly at center – let other forces decide
                PatternSteering[index] = float3.zero;
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
                // tInner: 0 at inner, 1 at center
                float tInner = math.saturate((inner - dist) / bandWidth);
                radialScalar = +tInner;
            } else if (dist > outer) {
                // Too far outside → pull inward
                // tOuter: 0 at outer, 1 further away
                float tOuter = math.saturate((dist - outer) / bandWidth);
                radialScalar = -tOuter;
            } else {
                // Inside the comfortable band: very soft bias toward Radius,
                // so other behaviours (wander, group noise, separation) dominate.
                float delta = dist - Radius;                          // -Thickness..+Thickness
                float norm = math.saturate(math.abs(delta) / bandWidth);
                float soften = 1f - norm;                             // 1 near Radius, 0 near inner/outer edge
                float sign = delta > 0f ? -1f : 1f;                   // slightly outwards or inwards
                radialScalar = sign * soften * 0.25f;                 // weak correction
            }

            PatternSteering[index] = radialDir * radialScalar * Strength;
        }
    }
}
