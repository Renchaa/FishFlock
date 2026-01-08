using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Flock.Scripts.Build.Influence.PatternVolume.Data;

namespace Flock.Scripts.Build.Influence.PatternVolume.Jobs {
    /**
     * <summary>
     * Layer-3 pattern: soft constraint towards an axis-aligned box shell.
     * Agents are nudged towards the faces of the box, with a configurable band thickness
     * on either side of each face.
     * </summary>
     */
    [BurstCompile]
    public struct PatternVolumeBoxShellJob : IJobParallelFor, IPatternVolumeJob {
        private const float MinimumVectorLengthSquared = 1e-8f;
        private const float MinimumComponentEpsilon = 1e-5f;
        private const float MinimumThicknessEpsilon = 0.0001f;
        private const float ComfortBandCorrectionStrength = 0.25f;

        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public float3 Center;

        [ReadOnly]
        public float3 HalfExtents;

        [ReadOnly]
        public float Thickness;

        [ReadOnly]
        public float Strength;

        [ReadOnly]
        public uint BehaviourMask;

        public NativeArray<float3> PatternSteering;

        /**
         * <summary>
         * Sets common data shared across all Layer-3 pattern jobs.
         * </summary>
         */
        public void SetCommonData(
            NativeArray<float3> positions,
            NativeArray<int> behaviourIds,
            NativeArray<float3> patternSteering,
            uint behaviourMask,
            float strength) {

            Positions = positions;
            BehaviourIds = behaviourIds;
            PatternSteering = patternSteering;
            BehaviourMask = behaviourMask;
            Strength = strength;
        }

        public void Execute(int index) {
            if (!IsIndexValid(index)) {
                return;
            }

            if (!IsBoxShellEnabled()) {
                return;
            }

            if (!PassesBehaviourMask(index)) {
                return;
            }

            float3 position = Positions[index];
            float3 relative = position - Center;

            float3 absoluteRelative = math.abs(relative);
            float3 halfExtents = HalfExtents;

            int dominantAxis = GetDominantAxis(absoluteRelative, halfExtents, out float dominantAxisNormalizedDistance);
            GetAxisDistance(relative, halfExtents, dominantAxis, out float signedAxisDistance, out float axisHalfExtent);

            float axisDistance = math.abs(signedAxisDistance);

            if (axisDistance < MinimumComponentEpsilon && dominantAxisNormalizedDistance < 1f) {
                return;
            }

            float radialScalar = ComputeRadialScalar(axisDistance, axisHalfExtent, Thickness);
            float3 radialDirection = GetAxisDirection(dominantAxis, signedAxisDistance);

            PatternSteering[index] += radialDirection * radialScalar * Strength;
        }

        private bool IsIndexValid(int index) {
            return PatternSteering.IsCreated
                   && Positions.IsCreated
                   && index >= 0
                   && index < Positions.Length
                   && index < PatternSteering.Length;
        }

        private bool IsBoxShellEnabled() {
            return Strength > 0f
                   && Thickness > 0f
                   && HalfExtents.x > 0f
                   && HalfExtents.y > 0f
                   && HalfExtents.z > 0f;
        }

        private bool PassesBehaviourMask(int index) {
            if (!BehaviourIds.IsCreated) {
                return true;
            }

            int behaviourIndex = 0;
            if (index >= 0 && index < BehaviourIds.Length) {
                behaviourIndex = BehaviourIds[index];
            }

            if (BehaviourMask == uint.MaxValue) {
                return true;
            }

            if (behaviourIndex < 0 || behaviourIndex >= 32) {
                return false;
            }

            uint bit = 1u << behaviourIndex;
            return (BehaviourMask & bit) != 0u;
        }

        private static int GetDominantAxis(
            float3 absoluteRelative,
            float3 halfExtents,
            out float dominantAxisNormalizedDistance) {

            float3 normalized = new float3(
                absoluteRelative.x / math.max(halfExtents.x, MinimumComponentEpsilon),
                absoluteRelative.y / math.max(halfExtents.y, MinimumComponentEpsilon),
                absoluteRelative.z / math.max(halfExtents.z, MinimumComponentEpsilon));

            int axis = 0;
            float axisValue = normalized.x;

            if (normalized.y > axisValue) {
                axis = 1;
                axisValue = normalized.y;
            }

            if (normalized.z > axisValue) {
                axis = 2;
                axisValue = normalized.z;
            }

            dominantAxisNormalizedDistance = axisValue;
            return axis;
        }

        private static void GetAxisDistance(
            float3 relative,
            float3 halfExtents,
            int axis,
            out float signedAxisDistance,
            out float axisHalfExtent) {

            switch (axis) {
                case 0:
                    signedAxisDistance = relative.x;
                    axisHalfExtent = halfExtents.x;
                    return;

                case 1:
                    signedAxisDistance = relative.y;
                    axisHalfExtent = halfExtents.y;
                    return;

                default:
                    signedAxisDistance = relative.z;
                    axisHalfExtent = halfExtents.z;
                    return;
            }
        }

        private static float ComputeRadialScalar(float axisDistance, float axisHalfExtent, float thickness) {
            float bandWidth = math.max(thickness, MinimumThicknessEpsilon);

            float inner = math.max(axisHalfExtent - thickness, 0f);
            float outer = axisHalfExtent + thickness;

            if (axisDistance < inner) {
                float tInner = math.saturate((inner - axisDistance) / bandWidth);
                return +tInner;
            }

            if (axisDistance > outer) {
                float tOuter = math.saturate((axisDistance - outer) / bandWidth);
                return -tOuter;
            }

            float delta = axisDistance - axisHalfExtent;
            float normalizedDelta = math.saturate(math.abs(delta) / bandWidth);
            float soften = 1f - normalizedDelta;
            float sign = delta > 0f ? -1f : 1f;

            return sign * soften * ComfortBandCorrectionStrength;
        }

        private static float3 GetAxisDirection(int axis, float signedAxisDistance) {
            float signAxis = signedAxisDistance >= 0f ? 1f : -1f;

            switch (axis) {
                case 0:
                    return new float3(signAxis, 0f, 0f);

                case 1:
                    return new float3(0f, signAxis, 0f);

                default:
                    return new float3(0f, 0f, signAxis);
            }
        }
    }
}
