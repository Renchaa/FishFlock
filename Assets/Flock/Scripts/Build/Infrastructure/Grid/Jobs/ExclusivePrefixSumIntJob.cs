using Unity.Collections;
using Unity.Jobs;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs {

    /**
     * <summary>
     * Computes an exclusive prefix sum over <see cref="Counts"/> into <see cref="Starts"/>,
     * and writes the final sum into <see cref="Total"/>[0].
     * </summary>
     */
    [BurstCompile]
    public struct ExclusivePrefixSumIntJob : IJob {
        [ReadOnly]
        public NativeArray<int> Counts;

        public NativeArray<int> Starts;

        public NativeArray<int> Total;

        public void Execute() {
            int runningTotal = 0;

            for (int index = 0; index < Counts.Length; index += 1) {
                Starts[index] = runningTotal;
                runningTotal += Counts[index];
            }

            Total[0] = runningTotal;
        }
    }
}