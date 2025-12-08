// File: Assets/Flock/Runtime/Data/IFlockLayer3PatternJob.cs
namespace Flock.Runtime.Data {
    using Unity.Collections;
    using Unity.Mathematics;

    public interface IFlockLayer3PatternJob {
        void SetCommonData(
            NativeArray<float3> positions,
            NativeArray<int> behaviourIds,
            NativeArray<float3> patternSteering,
            uint behaviourMask,
            float strength);
    }
}
