namespace Flock.Runtime.Data {
    using System;
    using Unity.Mathematics;

    [Serializable]
    public struct FlockGroupNoiseSphereShellPayload {
        public float3 CenterNorm;
        public float Radius;
        public float Thickness;
        public float SwirlStrength;

        public static FlockGroupNoiseSphereShellPayload Default => new FlockGroupNoiseSphereShellPayload {
            CenterNorm = new float3(0.5f, 0.5f, 0.5f),
            Radius = 8.0f,
            Thickness = 2.0f,
            SwirlStrength = 1.0f,
        };
    }
}
