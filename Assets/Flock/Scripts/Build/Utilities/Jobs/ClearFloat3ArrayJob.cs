using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;

namespace Flock.Scripts.Build.Utilities.Jobs
{
    /**
     * <summary>
     * Clears a <see cref="NativeArray{T}"/> of <see cref="float3"/> to <see cref="float3.zero"/>.
     * </summary>
     */
    [BurstCompile]
    public struct ClearFloat3ArrayJob : IJobParallelFor
    {
        public NativeArray<float3> Array;

        public void Execute(int index)
        {
            Array[index] = float3.zero;
        }
    }
}
