using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    /**
     * <summary>
     * Math utilities for mapping grid cells into group-noise pattern space and generating
     * stable per-cell hash phases.
     * </summary>
     */
    internal static class GroupNoiseFieldMath {
        internal const float TwoPi = 6.2831853f;

        internal static int3 IndexToCell(int index, int3 resolution) {
            int resolutionX = math.max(1, resolution.x);
            int resolutionY = math.max(1, resolution.y);
            int layerSize = resolutionX * resolutionY;

            int zIndex = index / layerSize;
            int remainder = index - zIndex * layerSize;
            int yIndex = remainder / resolutionX;
            int xIndex = remainder - yIndex * resolutionX;

            return new int3(xIndex, yIndex, zIndex);
        }

        internal static float3 CellToUVW(int3 cell, int3 resolution) {
            float3 gridSize = new float3(
                math.max(1, resolution.x),
                math.max(1, resolution.y),
                math.max(1, resolution.z));

            float3 cellCenter = new float3(
                cell.x + 0.5f,
                cell.y + 0.5f,
                cell.z + 0.5f);

            return cellCenter / gridSize;
        }

        internal static float3 UVWToP(float3 uvw, float worldScale) {
            float3 centered = (uvw - 0.5f) * 2f;
            return centered * worldScale;
        }

        internal static float ComputeT(float time, float frequency, float baseFrequency) {
            float clampedFrequency = math.max(frequency, 0f);
            float clampedBaseFrequency = math.max(baseFrequency, 0f);

            return time * (clampedFrequency * clampedBaseFrequency);
        }

        internal static uint HashCell(uint baseSeed, int3 cell) {
            uint seed = baseSeed;
            seed ^= (uint)cell.x * 73856093u;
            seed ^= (uint)cell.y * 19349663u;
            seed ^= (uint)cell.z * 83492791u;

            return seed;
        }

        internal static float Hash01(uint seed) {
            seed ^= seed >> 17;
            seed *= 0xED5AD4BBu;
            seed ^= seed >> 11;
            seed *= 0xAC4C1B51u;
            seed ^= seed >> 15;
            seed *= 0x31848BABu;
            seed ^= seed >> 14;

            return (seed >> 8) * (1.0f / 16777216.0f);
        }

        internal static float3 ComputeHashPhase(uint baseSeed, int3 cell) {
            uint cellHash = HashCell(baseSeed, cell);

            float phaseX = Hash01(cellHash ^ 0xA2C2A1EDu);
            float phaseY = Hash01(cellHash ^ 0x27D4EB2Fu);
            float phaseZ = Hash01(cellHash ^ 0x165667B1u);

            return new float3(phaseX, phaseY, phaseZ) * TwoPi;
        }
    }
}
