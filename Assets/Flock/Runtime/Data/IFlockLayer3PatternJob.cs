using Unity.Collections;
using Unity.Mathematics;

namespace Flock.Runtime.Data {
    /**
     * <summary>
     * Common interface for Layer-3 pattern jobs to receive shared input data.
     * </summary>
     */
    public interface IFlockLayer3PatternJob {
        /**
         * <summary>
         * Sets the common pattern job input arrays and per-pattern parameters.
         * </summary>
         * <param name="positions">Agent positions array.</param>
         * <param name="behaviourIds">Agent behaviour id array.</param>
         * <param name="patternSteering">Output steering contribution array.</param>
         * <param name="behaviourMask">Bitmask over behaviour indices (uint.MaxValue = all).</param>
         * <param name="strength">Pattern strength applied before per-behaviour weighting.</param>
         */
        void SetCommonData(
            NativeArray<float3> positions,
            NativeArray<int> behaviourIds,
            NativeArray<float3> patternSteering,
            uint behaviourMask,
            float strength);
    }
}
