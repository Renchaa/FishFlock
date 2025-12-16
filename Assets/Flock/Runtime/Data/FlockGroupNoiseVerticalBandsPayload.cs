namespace Flock.Runtime.Data {
    using System;

    [Serializable]
    public struct FlockGroupNoiseVerticalBandsPayload {
        public float VerticalBias;

        public static FlockGroupNoiseVerticalBandsPayload Default => new FlockGroupNoiseVerticalBandsPayload {
            VerticalBias = 0.0f,
        };
    }
}
