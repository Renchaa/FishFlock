using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.PatternVolume.Data {

    /**
     * <summary>
     * Runtime payload for a Layer-3 sphere shell pattern.
     * </summary>
     */
    public struct PatternVolumeSphereShell {
        // SphereShell
        public float3 Center;
        public float Radius;
        public float Thickness;
    }
}
