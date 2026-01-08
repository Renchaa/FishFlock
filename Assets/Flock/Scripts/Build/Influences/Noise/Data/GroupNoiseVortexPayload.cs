using System;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Noise.Data
{
    /**
     * <summary>
     * Pattern-specific payload for the Vortex group noise implementation.
     * </summary>
     */
    [Serializable]
    public struct GroupNoiseVortexPayload
    {
        // Vortex
        public float3 CenterNorm;
        public float Radius;
        public float Tightness;
        public float VerticalBias;

        /**
         * <summary>
         * Gets the default payload values.
         * </summary>
         */
        public static GroupNoiseVortexPayload Default => new GroupNoiseVortexPayload
        {
            CenterNorm = new float3(0.5f, 0.5f, 0.5f),
            Radius = 10.0f,
            Tightness = 1.0f,
            VerticalBias = 0.0f,
        };
    }
}
