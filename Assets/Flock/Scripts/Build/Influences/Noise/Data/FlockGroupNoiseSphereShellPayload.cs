using System;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Noise.Data
{
    /**
     * <summary>
     * Pattern-specific payload for the SphereShell group noise implementation.
     * </summary>
     */
    [Serializable]
    public struct FlockGroupNoiseSphereShellPayload
    {
        // SphereShell
        public float3 CenterNorm;
        public float Radius;
        public float Thickness;
        public float SwirlStrength;

        /**
         * <summary>
         * Gets the default payload values.
         * </summary>
         */
        public static FlockGroupNoiseSphereShellPayload Default => new FlockGroupNoiseSphereShellPayload
        {
            CenterNorm = new float3(0.5f, 0.5f, 0.5f),
            Radius = 8.0f,
            Thickness = 2.0f,
            SwirlStrength = 1.0f,
        };
    }
}
