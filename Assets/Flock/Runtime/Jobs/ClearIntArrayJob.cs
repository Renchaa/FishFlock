// File: Assets/Flock/Runtime/Jobs/ClearIntArrayJob.cs
namespace Flock.Runtime.Jobs {
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;

    [BurstCompile]
    public struct ClearIntArrayJob : IJobParallelFor {
        public NativeArray<int> Array;

        public void Execute(int index) {
            Array[index] = 0;
        }
    }
}
