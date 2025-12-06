// =====================================
// 4) NEW JOB: GroupNoiseFieldJob.cs
// File: Assets/Flock/Runtime/Jobs/GroupNoiseFieldJob.cs
// =====================================
using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    [BurstCompile]
    public struct GroupNoiseFieldJob : IJobParallelFor {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;

        [ReadOnly] public int3 GridResolution;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        [ReadOnly]
        public FlockGroupNoisePatternSettings PatternSettings;

        public void Execute(int index) {
            if (!CellNoise.IsCreated
                || index < 0
                || index >= CellNoise.Length) {
                return;
            }

            int resX = GridResolution.x;
            int resY = GridResolution.y;
            int layerSize = resX * resY;

            int z = index / layerSize;
            int rem = index - z * layerSize;
            int y = rem / resX;
            int x = rem - y * resX;

            int3 cell = new int3(x, y, z);

            // NEW: evaluate pattern-specific direction for this cell
            float3 dir = EvaluatePatternDirection(cell);

            CellNoise[index] = math.normalizesafe(dir, float3.zero);
        }

        // NEW: evaluate group-noise direction per cell based on PatternSettings
        float3 EvaluatePatternDirection(int3 cell) {
            FlockGroupNoisePatternSettings s = PatternSettings;

            // global combined frequency (environment * global knob)
            float globalFreq = math.max(Frequency, 0f);
            float baseFreq = math.max(s.BaseFrequency, 0f);
            float freq = globalFreq * baseFreq;
            float t = Time * freq;

            // normalised grid coords [0..1]
            float gridX = math.max(1, GridResolution.x);
            float gridY = math.max(1, GridResolution.y);
            float gridZ = math.max(1, GridResolution.z);

            float3 gridSize = new float3(gridX, gridY, gridZ);
            float3 uvw = (new float3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f)) / gridSize; // 0..1

            // center at 0 and apply world scale → [-1..1] * WorldScale
            float3 p = (uvw - 0.5f) * 2f;
            p *= s.WorldScale;

            // per-cell hash phases based on user-provided seed
            uint cellHash = HashCell(s.Seed, cell);
            float3 hashPhase = new float3(
                Hash01(cellHash ^ 0xA2C2A1EDu),
                Hash01(cellHash ^ 0x27D4EB2Fu),
                Hash01(cellHash ^ 0x165667B1u)
            ) * 6.2831853f;

            const int PATTERN_SIMPLE_SINE = 0;
            const int PATTERN_VERTICAL_BANDS = 1;
            const int PATTERN_VORTEX = 2;
            const int PATTERN_SPHERE_SHELL = 3;    // NEW

            int pattern = s.PatternType;

            // in EvaluatePatternDirection(...) change SphereShell call:
            switch (pattern) {
                case PATTERN_VERTICAL_BANDS:
                    return EvaluateVerticalBands(p, uvw, t, s, hashPhase);

                case PATTERN_VORTEX:
                    return EvaluateVortex(p, uvw, t, s);

                case PATTERN_SPHERE_SHELL:
                    // PASS hashPhase so each cell gets its own great-circle swirl
                    return EvaluateSphereShell(p, uvw, t, s, hashPhase);

                case PATTERN_SIMPLE_SINE:
                default:
                    return EvaluateSimpleSine(p, t, s, hashPhase);
            }

        }

        // NEW: hash cell index with a base seed
        static uint HashCell(uint baseSeed, int3 cell) {
            uint seed = baseSeed;
            seed ^= (uint)cell.x * 73856093u;
            seed ^= (uint)cell.y * 19349663u;
            seed ^= (uint)cell.z * 83492791u;
            return seed;
        }

        float3 EvaluateSphereShell(
            float3 p,
            float3 uvw,
            float t,
            FlockGroupNoisePatternSettings s,
            float3 hashPhase) {

            // Normalised center 0..1 → local space [-WorldScale..WorldScale]
            float3 centerNorm = new float3(
                math.saturate(s.SphereCenterNorm.x),
                math.saturate(s.SphereCenterNorm.y),
                math.saturate(s.SphereCenterNorm.z));

            float3 center = (centerNorm - 0.5f) * 2f * s.WorldScale;

            float3 rel = p - center;
            float dist = math.length(rel);
            float radius = s.SphereRadius;
            float thickness = math.max(s.SphereThickness, 0.001f);

            if (dist < 1e-5f || radius <= 0f) {
                // fallback: arbitrary stable direction
                return new float3(0f, 1f, 0f);
            }

            float3 radialDir = rel / dist;

            // --- RADIAL PART ---
            // inner/outer band around radius where fish are "happy"
            float inner = math.max(radius - thickness, 0f);
            float outer = radius + thickness;

            float radialScalar = 0f;

            if (dist < inner) {
                // too close to center → push outward
                float tInner = math.saturate((inner - dist) / thickness); // 0 at inner, 1 at center
                radialScalar = +math.lerp(0.5f, 1f, tInner);
            } else if (dist > outer) {
                // too far outside → pull inward
                float tOuter = math.saturate((dist - outer) / thickness); // 0 at outer, 1 far away
                radialScalar = -math.lerp(0.5f, 1f, tOuter);
            } else {
                // inside comfort band → weak bias back to middle of band
                float delta = dist - radius;                               // -thickness..+thickness
                float norm = math.saturate(math.abs(delta) / thickness);   // 0 at radius, 1 at edges
                float soften = 1f - norm;                                  // strongest near radius
                float sign = delta > 0f ? -1f : 1f;                        // outside half vs inside half
                radialScalar = sign * soften * 0.35f;
            }

            float3 radial = radialDir * radialScalar;

            // --- TANGENT / SWIRL PART ---
            // Build an orthonormal basis of the tangent plane at this point on the sphere.
            float3 any = math.abs(radialDir.y) < 0.8f
                ? new float3(0f, 1f, 0f)
                : new float3(1f, 0f, 0f);

            float3 tangent1 = math.normalizesafe(math.cross(radialDir, any), float3.zero);
            float3 tangent2 = math.normalizesafe(math.cross(radialDir, tangent1), float3.zero);

            // Angle for swirl along a random great circle (per-cell hash + time)
            float angle = t + hashPhase.x;
            float sa = math.sin(angle);
            float ca = math.cos(angle);

            float3 swirlDir = tangent1 * ca + tangent2 * sa;

            // Swirl strongest near ideal radius, fades away as we leave the band
            float distFromRadius = math.abs(dist - radius);
            float swirlFalloff = 1f - math.saturate(distFromRadius / (thickness * 2f));
            float swirlStrength = s.SphereSwirlStrength * swirlFalloff;

            float3 swirl = swirlDir * swirlStrength;

            // Combine radial pull/push + tangential swirl.
            float3 dir = radial + swirl;

            // Keep it as a pure direction (magnitude is handled later by behaviour weights).
            return math.normalizesafe(dir, radialDir);
        }

        // NEW: SimpleSine pattern – generalised version of your old hardcoded sin soup
        float3 EvaluateSimpleSine(
            float3 p,
            float t,
            FlockGroupNoisePatternSettings s,
            float3 phase) {

            float3 timeVec = new float3(
                t * s.TimeScale.x,
                t * s.TimeScale.y,
                t * s.TimeScale.z);

            float3 totalPhase = p + timeVec + s.PhaseOffset + phase;

            float3 dir = new float3(
                math.sin(totalPhase.x),
                math.sin(totalPhase.y),
                math.sin(totalPhase.z));

            // optional swirl in XZ plane controlled by SwirlStrength
            if (s.SwirlStrength > 0f) {
                float2 swirlPos = new float2(p.x, p.z);
                float lenSq = math.lengthsq(swirlPos);
                if (lenSq > 1e-6f) {
                    float2 tangent = new float2(-swirlPos.y, swirlPos.x) * math.rsqrt(lenSq);
                    float swirl = s.SwirlStrength;
                    dir.x += tangent.x * swirl;
                    dir.z += tangent.y * swirl;
                }
            }

            return dir;
        }

        // NEW: VerticalBands pattern – strong vertical component based on height
        float3 EvaluateVerticalBands(
            float3 p,
            float3 uvw,
            float t,
            FlockGroupNoisePatternSettings s,
            float3 phase) {

            // height-based banding
            float heightPhase = (uvw.y - 0.5f) * 6.2831853f + t + phase.y;
            float verticalWave = math.sin(heightPhase);

            float3 dir = new float3(
                math.sin(p.x + phase.x + t),
                verticalWave + s.VerticalBias,
                math.sin(p.z + phase.z - t));

            return dir;
        }

        // NEW: Vortex pattern – orbit around a center in XZ with optional vertical bias
        float3 EvaluateVortex(
            float3 p,
            float3 uvw,
            float t,
            FlockGroupNoisePatternSettings s) {

            float3 centerNorm = new float3(
                math.saturate(s.VortexCenterNorm.x),
                math.saturate(s.VortexCenterNorm.y),
                math.saturate(s.VortexCenterNorm.z));

            float3 center = (centerNorm - 0.5f) * 2f * s.WorldScale;

            float3 rel = p - center;
            float2 relXZ = new float2(rel.x, rel.z);

            float dist = math.length(relXZ);
            if (dist < 1e-5f || s.VortexRadius <= 0f) {
                // too close to center or radius zero → mild vertical motion as fallback
                return new float3(0f, math.sin(t) * s.VerticalBias, 0f);
            }

            float radiusNorm = math.saturate(dist / s.VortexRadius);
            float falloff = 1f - radiusNorm;
            if (falloff <= 0f) {
                // outside radius – no flow
                return float3.zero;
            }

            // tangential vector around center in XZ
            float2 tangent = new float2(-relXZ.y, relXZ.x) / dist;

            float orbitSpeed = falloff * (1f + s.VortexTightness);
            float vertical = (0.5f - radiusNorm) * s.VerticalBias;

            float3 dir = new float3(
                tangent.x * orbitSpeed,
                vertical,
                tangent.y * orbitSpeed);

            return dir;
        }

        static float Hash01(uint seed) {
            seed ^= seed >> 17;
            seed *= 0xED5AD4BBu;
            seed ^= seed >> 11;
            seed *= 0xAC4C1B51u;
            seed ^= seed >> 15;
            seed *= 0x31848BABu;
            seed ^= seed >> 14;
            return (seed >> 8) * (1.0f / 16777216.0f);
        }
    }
}
