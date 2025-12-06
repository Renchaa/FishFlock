// =====================================
// NEW FILE: FlockGroupNoisePatternSettings.cs
// File: Assets/Flock/Runtime/Data/FlockGroupNoisePatternSettings.cs
// =====================================
namespace Flock.Runtime.Data {
    using System;
    using Unity.Mathematics;

    [Serializable]
    public struct FlockGroupNoisePatternSettings {
        // Global frequency multiplier for this pattern
        public float BaseFrequency;

        // Per-axis time scales (how fast pattern evolves in x/y/z)
        public float3 TimeScale;

        // Phase offset in "world" noise space
        public float3 PhaseOffset;

        // Scales grid coordinates into pattern space
        public float WorldScale;

        // Per-pattern seed so multiple assets can differ
        public uint Seed;

        // 0 = SimpleSine, 1 = VerticalBands, 2 = Vortex
        public int PatternType;

        // Extra swirl in XZ plane (SimpleSine / Bands)
        public float SwirlStrength;

        // Vertical bias for patterns that have Y component
        public float VerticalBias;

        // Normalised [0..1] center of vortex in grid space
        public float3 VortexCenterNorm;

        // World-space radius for vortex effect (in pattern space units)
        public float VortexRadius;

        // Tightness / angular speed factor of vortex
        public float VortexTightness;

        // NEW: sphere-shell pattern params
        public float SphereRadius;
        public float SphereThickness;
        public float SphereSwirlStrength;
        public float3 SphereCenterNorm;

        // NEW: sane defaults used when no profile is assigned
        public static FlockGroupNoisePatternSettings Default => new FlockGroupNoisePatternSettings {
            BaseFrequency = 1.0f,
            TimeScale = new float3(1.0f, 1.1f, 1.3f),
            PhaseOffset = float3.zero,
            WorldScale = 1.0f,
            Seed = 1234567u,
            PatternType = 0,          // SimpleSine
            SwirlStrength = 0.0f,
            VerticalBias = 0.0f,
            VortexCenterNorm = new float3(0.5f, 0.5f, 0.5f),
            VortexRadius = 5.0f,
            VortexTightness = 1.0f,

            // NEW
            SphereRadius = 0f,
            SphereThickness = 1f,
            SphereSwirlStrength = 0f,
            SphereCenterNorm = new float3(0.5f, 0.5f, 0.5f),
        };
    }
}
