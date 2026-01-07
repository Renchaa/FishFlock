using System;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Noise.Data {
    /**
     * <summary>
     * Common (pattern-agnostic) settings for group noise generation.
     * </summary>
     */
    [Serializable]
    public struct FlockGroupNoiseCommonSettings {
        public float BaseFrequency;
        public float3 TimeScale;
        public float3 PhaseOffset;
        public float WorldScale;
        public uint Seed;

        /**
         * <summary>
         * Gets the default common settings values.
         * </summary>
         */
        public static FlockGroupNoiseCommonSettings Default => new FlockGroupNoiseCommonSettings {
            BaseFrequency = 1.0f,
            TimeScale = new float3(1.0f, 1.1f, 1.3f),
            PhaseOffset = float3.zero,
            WorldScale = 10.0f,
            Seed = 1234567u,
        };
    }
}
