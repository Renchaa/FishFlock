namespace Flock.Runtime.Jobs {
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;

    [BurstCompile]
    public struct CopyIntArrayJob : IJobParallelFor {
        [ReadOnly] public NativeArray<int> Source;
        public NativeArray<int> Destination;
        public int Count;

        public void Execute(int index) {
            if ((uint)index >= (uint)Count) {
                return;
            }

            Destination[index] = Source[index];
        }
    }
}
