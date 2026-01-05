using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Flock.Runtime.Jobs {
    /**
     * <summary>
     * Copies the first <see cref="Count"/> elements from <see cref="Source"/> into <see cref="Destination"/>.
     * </summary>
     */
    [BurstCompile]
    public struct CopyIntArrayJob : IJobParallelFor {
        [ReadOnly]
        public NativeArray<int> Source;

        public NativeArray<int> Destination;

        [ReadOnly]
        public int Count;

        public void Execute(int index) {
            if ((uint)index >= (uint)Count) {
                return;
            }

            Destination[index] = Source[index];
        }
    }
}
