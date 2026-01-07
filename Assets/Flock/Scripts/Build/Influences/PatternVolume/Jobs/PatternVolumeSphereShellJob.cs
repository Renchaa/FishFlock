// File: Assets/Flock/Runtime/Jobs/PatternSphereJob.cs
using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.PatternVolume.Jobs {
    /**
     * <summary>
     * Layer-3 pattern that applies a soft radial constraint towards a spherical band.
     * Steering is accumulated into <see cref="PatternSteering"/> so multiple patterns can stack.
     * </summary>
     */
    [BurstCompile]
    public struct PatternVolumeSphereShellJob : IJobParallelFor, IPatternVolumeJob {
        private const float MinimumDistanceEpsilon = 1e-5f;
        private const float MinimumThicknessEpsilon = 0.0001f;
        private const float ComfortBandCorrectionStrength = 0.25f;

        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly] 
        public NativeArray<int> BehaviourIds;

        [ReadOnly] 
        public float3 Center;

        [ReadOnly] 
        public float Radius;

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

        /**
         * <summary>
         * Computes and accumulates sphere-shell steering for a single agent.
         * </summary>
         */
        public void Execute(int index) {
            if (!IsIndexValid(index) || !IsPatternEnabled() || !IsBehaviourAllowed(index)) {
                return;
            }

            float3 position = Positions[index];
            float3 relativePosition = position - Center;

            float distanceFromCenter = math.length(relativePosition);
            if (distanceFromCenter < MinimumDistanceEpsilon) {
                return;
            }

            float3 radialDirection = relativePosition / distanceFromCenter;
            float radialScalar = ComputeRadialScalar(distanceFromCenter);

            PatternSteering[index] += radialDirection * radialScalar * Strength;
        }

        private bool IsIndexValid(int index) {
            return PatternSteering.IsCreated
                && Positions.IsCreated
                && index >= 0
                && index < Positions.Length
                && index < PatternSteering.Length;
        }

        private bool IsPatternEnabled() {
            return Radius > 0f && Strength > 0f && Thickness > 0f;
        }

        private bool IsBehaviourAllowed(int index) {
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

        private float ComputeRadialScalar(float distanceFromCenter) {
            float innerRadius = math.max(Radius - Thickness, 0f);
            float outerRadius = Radius + Thickness;
            float bandWidth = math.max(Thickness, MinimumThicknessEpsilon);

            if (distanceFromCenter < innerRadius) {
                float innerT = math.saturate((innerRadius - distanceFromCenter) / bandWidth);
                return +innerT;
            }

            if (distanceFromCenter > outerRadius) {
                float outerT = math.saturate((distanceFromCenter - outerRadius) / bandWidth);
                return -outerT;
            }

            float delta = distanceFromCenter - Radius;
            float normalized = math.saturate(math.abs(delta) / bandWidth);
            float soften = 1f - normalized;
            float sign = delta > 0f ? -1f : 1f;

            return sign * soften * ComfortBandCorrectionStrength;
        }
    }
}
