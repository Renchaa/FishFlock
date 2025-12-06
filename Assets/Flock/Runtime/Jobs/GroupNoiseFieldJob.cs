// =====================================
// 4) NEW JOB: GroupNoiseFieldJob.cs
// File: Assets/Flock/Runtime/Jobs/GroupNoiseFieldJob.cs
// =====================================
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    [BurstCompile]
    public struct GroupNoiseFieldJob : IJobParallelFor {
        [ReadOnly] public float Time;
        [ReadOnly] public float Frequency;

        [ReadOnly] public int3 GridResolution;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        public void Execute(int index) {
            if (!CellNoise.IsCreated
                || index < 0
                || index >= CellNoise.Length) {
                return;
            }

            int resX = GridResolution.x;
            int resY = GridResolution.y;
            int layerSize = resX * resY;

            int z = index / layerSize;
            int rem = index - z * layerSize;
            int y = rem / resX;
            int x = rem - y * resX;

            int3 cell = new int3(x, y, z);

            uint seed = (uint)(cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791);
            float basePhase = Hash01(seed) * 6.2831853f;

            float t = Time * math.max(Frequency, 0f);

            float3 dir = new float3(
                math.sin(basePhase + t),
                math.sin(basePhase * 1.7f + t * 0.9f),
                math.sin(basePhase * 2.3f + t * 1.3f));

            CellNoise[index] = math.normalizesafe(dir, float3.zero);
        }

        static float Hash01(uint seed) {
            seed ^= seed >> 17;
            seed *= 0xED5AD4BBu;
            seed ^= seed >> 11;
            seed *= 0xAC4C1B51u;
            seed ^= seed >> 15;
            seed *= 0x31848BABu;
            seed ^= seed >> 14;
            return (seed >> 8) * (1.0f / 16777216.0f);
        }
    }
}
