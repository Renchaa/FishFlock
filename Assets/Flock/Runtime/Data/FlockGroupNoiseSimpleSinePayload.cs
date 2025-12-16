namespace Flock.Runtime.Data {
    using System;

    [Serializable]
    public struct FlockGroupNoiseSimpleSinePayload {
        public float SwirlStrength;

        public static FlockGroupNoiseSimpleSinePayload Default => new FlockGroupNoiseSimpleSinePayload {
            SwirlStrength = 0.0f,
        };
    }
}
