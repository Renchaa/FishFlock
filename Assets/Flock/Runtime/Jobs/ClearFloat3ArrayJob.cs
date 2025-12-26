using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    /**
     * <summary>
     * Clears a <see cref="NativeArray{T}"/> of <see cref="float3"/> to <see cref="float3.zero"/>.
     * </summary>
     */
    [BurstCompile]
    public struct ClearFloat3ArrayJob : IJobParallelFor {
        public NativeArray<float3> Array;

        public void Execute(int index) {
            Array[index] = float3.zero;
        }
    }
}
