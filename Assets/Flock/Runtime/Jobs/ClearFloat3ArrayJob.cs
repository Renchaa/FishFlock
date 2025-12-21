namespace Flock.Runtime.Jobs {
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct ClearFloat3ArrayJob : IJobParallelFor {
        public NativeArray<float3> Array;

        public void Execute(int index) {
            Array[index] = float3.zero;
        }
    }
}
