using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    internal static class GroupNoiseFieldMath {
        internal const float TwoPi = 6.2831853f;

        internal static int3 IndexToCell(int index, int3 res) {
            int resX = math.max(1, res.x);
            int resY = math.max(1, res.y);
            int layerSize = resX * resY;

            int z = index / layerSize;
            int rem = index - z * layerSize;
            int y = rem / resX;
            int x = rem - y * resX;

            return new int3(x, y, z);
        }

        internal static float3 CellToUVW(int3 cell, int3 res) {
            float3 gridSize = new float3(
                math.max(1, res.x),
                math.max(1, res.y),
                math.max(1, res.z));

            return (new float3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f)) / gridSize;
        }

        internal static float3 UVWToP(float3 uvw, float worldScale) {
            float3 p = (uvw - 0.5f) * 2f;
            return p * worldScale;
        }

        internal static float ComputeT(float time, float frequency, float baseFrequency) {
            float globalFreq = math.max(frequency, 0f);
            float baseFreq = math.max(baseFrequency, 0f);
            return time * (globalFreq * baseFreq);
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

            return new float3(
                Hash01(cellHash ^ 0xA2C2A1EDu),
                Hash01(cellHash ^ 0x27D4EB2Fu),
                Hash01(cellHash ^ 0x165667B1u)
            ) * TwoPi;
        }
    }
}
