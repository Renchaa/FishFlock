using System;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Noise.Data
{
    /**
     * <summary>
     * Runtime settings snapshot for group noise pattern generation.
     * </summary>
     */
    [Serializable]
    public struct FlockGroupNoisePatternSettings
    {
        // Common
        public float BaseFrequency;
        public float3 TimeScale;
        public float3 PhaseOffset;
        public float WorldScale;
        public uint Seed;

        // Pattern Type
        public int PatternType;

        // SimpleSine / VerticalBands Extras
        public float SwirlStrength;
        public float VerticalBias;

        // Vortex
        public float3 VortexCenterNorm;
        public float VortexRadius;
        public float VortexTightness;

        // Sphere Shell
        public float SphereRadius;
        public float SphereThickness;
        public float SphereSwirlStrength;
        public float3 SphereCenterNorm;

        /**
        * <summary>
        * Gets sane defaults used when no profile is assigned.
        * </summary>
        */
        public static FlockGroupNoisePatternSettings Default => new FlockGroupNoisePatternSettings
        {
            BaseFrequency = 1.0f,
            TimeScale = new float3(1.0f, 1.1f, 1.3f),
            PhaseOffset = float3.zero,
            WorldScale = 1.0f,
            Seed = 1234567u,

            PatternType = 0,

            SwirlStrength = 0.0f,
            VerticalBias = 0.0f,

            VortexCenterNorm = new float3(0.5f, 0.5f, 0.5f),
            VortexRadius = 5.0f,
            VortexTightness = 1.0f,

            SphereRadius = 0f,
            SphereThickness = 1f,
            SphereSwirlStrength = 0f,
            SphereCenterNorm = new float3(0.5f, 0.5f, 0.5f),
        };
    }
}
