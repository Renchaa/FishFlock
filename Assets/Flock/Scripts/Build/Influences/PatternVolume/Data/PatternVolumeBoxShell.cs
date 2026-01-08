using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.PatternVolume.Data
{
    /**
     * <summary>
     * Runtime payload for a Layer-3 box shell pattern.
     * </summary>
     */
    public struct PatternVolumeBoxShell
    {
        // BoxShell
        public float3 Center;
        public float3 HalfExtents;
        public float Thickness;
    }
}
