namespace Flock.Runtime.Data {
    using System;
    using Unity.Mathematics;

    [Serializable]
    public struct FlockGroupNoiseVortexPayload {
        public float3 CenterNorm;
        public float Radius;
        public float Tightness;
        public float VerticalBias;

        public static FlockGroupNoiseVortexPayload Default => new FlockGroupNoiseVortexPayload {
            CenterNorm = new float3(0.5f, 0.5f, 0.5f),
            Radius = 10.0f,
            Tightness = 1.0f,
            VerticalBias = 0.0f,
        };
    }
}
