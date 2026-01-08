using System;

namespace Flock.Scripts.Build.Influence.Noise.Data
{
    /**
     * <summary>
     * Pattern-specific payload for the VerticalBands group noise implementation.
     * </summary>
     */
    [Serializable]
    public struct FlockGroupNoiseVerticalBandsPayload
    {
        // VerticalBands
        public float VerticalBias;

        /**
         * <summary>
         * Gets the default payload values.
         * </summary>
         */
        public static FlockGroupNoiseVerticalBandsPayload Default => new FlockGroupNoiseVerticalBandsPayload
        {
            VerticalBias = 0.0f,
        };
    }
}
