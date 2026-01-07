using System;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Noise.Data {
    /**
     * <summary>
     * Runtime settings snapshot for group noise pattern generation.
     * </summary>
     */
    [Serializable]
    public struct FlockGroupNoisePatternSettings {
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
    }
}
