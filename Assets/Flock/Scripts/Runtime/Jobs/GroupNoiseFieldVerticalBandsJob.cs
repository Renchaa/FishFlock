using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    /**
     * <summary>
     * Generates per-cell noise directions using a vertical-bands pattern with an optional vertical bias.
     * </summary>
     */
    [BurstCompile]
    public struct GroupNoiseFieldVerticalBandsJob : IJobParallelFor {
        [ReadOnly]
        public float Time;

        [ReadOnly]
        public float Frequency;

        [ReadOnly]
        public int3 GridResolution;

        [ReadOnly]
        public FlockGroupNoiseCommonSettings Common;

        [ReadOnly]
        public FlockGroupNoiseVerticalBandsPayload Payload;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> CellNoise;

        public void Execute(int index) {
            if (!CellNoise.IsCreated || (uint)index >= (uint)CellNoise.Length) {
                return;
            }

            int3 cell = GroupNoiseFieldMath.IndexToCell(index, GridResolution);
            float3 uvw = GroupNoiseFieldMath.CellToUVW(cell, GridResolution);

            float worldScale = math.max(0.001f, Common.WorldScale);
            float3 position = GroupNoiseFieldMath.UVWToP(uvw, worldScale);

            float timeScaled = GroupNoiseFieldMath.ComputeT(Time, Frequency, Common.BaseFrequency);

            uint seed = Common.Seed == 0u ? 1u : Common.Seed;
            float3 hashPhase = GroupNoiseFieldMath.ComputeHashPhase(seed, cell);

            float3 direction = EvaluateDirection(position, uvw, timeScaled, hashPhase);
            CellNoise[index] = math.normalizesafe(direction, float3.zero);
        }

        private float3 EvaluateDirection(float3 position, float3 uvw, float timeScaled, float3 hashPhase) {
            float heightPhase =
                (uvw.y - 0.5f) * GroupNoiseFieldMath.TwoPi
                + timeScaled
                + hashPhase.y;

            float verticalWave = math.sin(heightPhase);

            return new float3(
                math.sin(position.x + hashPhase.x + timeScaled),
                verticalWave + Payload.VerticalBias,
                math.sin(position.z + hashPhase.z - timeScaled));
        }
    }
}
