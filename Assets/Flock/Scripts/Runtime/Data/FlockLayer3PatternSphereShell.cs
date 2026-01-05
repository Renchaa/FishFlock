using Unity.Mathematics;

namespace Flock.Runtime.Data {
    /**
     * <summary>
     * Runtime payload for a Layer-3 sphere shell pattern.
     * </summary>
     */
    public struct FlockLayer3PatternSphereShell {
        // SphereShell
        public float3 Center;
        public float Radius;
        public float Thickness;
    }
}
