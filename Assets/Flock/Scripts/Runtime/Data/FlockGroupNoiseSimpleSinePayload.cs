using System;

namespace Flock.Runtime.Data {
    /**
     * <summary>
     * Pattern-specific payload for the SimpleSine group noise implementation.
     * </summary>
     */
    [Serializable]
    public struct FlockGroupNoiseSimpleSinePayload {
        // SimpleSine
        public float SwirlStrength;

        /**
         * <summary>
         * Gets the default payload values.
         * </summary>
         */
        public static FlockGroupNoiseSimpleSinePayload Default => new FlockGroupNoiseSimpleSinePayload {
            SwirlStrength = 0.0f,
        };
    }
}
