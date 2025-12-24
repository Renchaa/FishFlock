using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    /**
     * <summary>
     * Samples the winning per-cell individual attractor and writes a per-agent attraction steering vector.
     * </summary>
     */
    [BurstCompile]
    public struct AttractorSamplingJob : IJobParallelFor {
        // Inputs
        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public NativeArray<FlockAttractorData> Attractors;

        [ReadOnly]
        public NativeArray<float> BehaviourAttractionWeight;

        [ReadOnly]
        public NativeArray<int> CellToIndividualAttractor;

        [ReadOnly]
        public float3 GridOrigin;

        [ReadOnly]
        public int3 GridResolution;

        [ReadOnly]
        public float CellSize;

        [ReadOnly]
        public NativeArray<byte> BehaviourUsePreferredDepth;

        [ReadOnly]
        public NativeArray<float> BehaviourPreferredDepthMin;

        [ReadOnly]
        public NativeArray<float> BehaviourPreferredDepthMax;

        [ReadOnly]
        public NativeArray<byte> BehaviourDepthWinsOverAttractor;

        // Outputs
        [NativeDisableParallelForRestriction]
        public NativeArray<float3> AttractionSteering;

        /// <inheritdoc />
        public void Execute(int index) {
            if (TryComputeAttractionSteering(index, out float3 steering)) {
                AttractionSteering[index] = steering;
                return;
            }

            AttractionSteering[index] = float3.zero;
        }

        private bool TryComputeAttractionSteering(int index, out float3 steering) {
            float3 position = Positions[index];

            if (!TryGetTypeResponse(index, out int behaviourIndex, out float typeResponse)) {
                steering = float3.zero;
                return false;
            }

            if (!TryGetAttractorForPosition(position, out FlockAttractorData attractorData)) {
                steering = float3.zero;
                return false;
            }

            if (!PassesAffectedTypesMask(behaviourIndex, attractorData)) {
                steering = float3.zero;
                return false;
            }

            if (ShouldIgnoreAttractorDueToDepthConflict(behaviourIndex, attractorData)) {
                steering = float3.zero;
                return false;
            }

            if (!TryGetNormalisedDistanceToBoundary(position, attractorData, out float3 offset, out float distance, out float normalisedDistance)) {
                steering = float3.zero;
                return false;
            }

            return TryComputeBoundaryAttractionSteering(attractorData, typeResponse, offset, distance, normalisedDistance, out steering);
        }

        private bool TryGetTypeResponse(int agentIndex, out int behaviourIndex, out float typeResponse) {
            behaviourIndex = BehaviourIds[agentIndex];

            if ((uint)behaviourIndex >= (uint)BehaviourAttractionWeight.Length) {
                typeResponse = 0f;
                return false;
            }

            typeResponse = BehaviourAttractionWeight[behaviourIndex];

            return typeResponse > 0.0f
                   && CellToIndividualAttractor.IsCreated
                   && CellToIndividualAttractor.Length != 0;
        }

        private bool TryGetAttractorForPosition(float3 position, out FlockAttractorData attractorData) {
            int cellIndex = GetCellIndex(position);

            if ((uint)cellIndex >= (uint)CellToIndividualAttractor.Length) {
                attractorData = default;
                return false;
            }

            int attractorIndex = CellToIndividualAttractor[cellIndex];

            if ((uint)attractorIndex >= (uint)Attractors.Length) {
                attractorData = default;
                return false;
            }

            attractorData = Attractors[attractorIndex];
            return true;
        }

        private static bool PassesAffectedTypesMask(int behaviourIndex, in FlockAttractorData data) {
            if (behaviourIndex < 32 && data.AffectedTypesMask != uint.MaxValue) {
                uint bit = 1u << behaviourIndex;
                return (data.AffectedTypesMask & bit) != 0u;
            }

            return true;
        }

        private bool ShouldIgnoreAttractorDueToDepthConflict(int behaviourIndex, in FlockAttractorData data) {
            if (!HasPreferredDepthData(behaviourIndex) || BehaviourUsePreferredDepth[behaviourIndex] == 0) {
                return false;
            }

            float preferredMinimum = BehaviourPreferredDepthMin[behaviourIndex];
            float preferredMaximum = BehaviourPreferredDepthMax[behaviourIndex];

            float attractorMinimum = data.DepthMinNorm;
            float attractorMaximum = data.DepthMaxNorm;

            NormaliseBand(ref preferredMinimum, ref preferredMaximum);
            NormaliseBand(ref attractorMinimum, ref attractorMaximum);

            float overlapMinimum = math.max(preferredMinimum, attractorMinimum);
            float overlapMaximum = math.min(preferredMaximum, attractorMaximum);

            const float MinimumOverlap = 0.001f;

            bool depthWins =
                BehaviourDepthWinsOverAttractor.IsCreated
                && BehaviourDepthWinsOverAttractor.Length > behaviourIndex
                && BehaviourDepthWinsOverAttractor[behaviourIndex] != 0;

            return (overlapMaximum - overlapMinimum) <= MinimumOverlap && depthWins;
        }

        private bool HasPreferredDepthData(int behaviourIndex) {
            return BehaviourUsePreferredDepth.IsCreated
                   && BehaviourPreferredDepthMin.IsCreated
                   && BehaviourPreferredDepthMax.IsCreated
                   && BehaviourUsePreferredDepth.Length > behaviourIndex
                   && BehaviourPreferredDepthMin.Length > behaviourIndex
                   && BehaviourPreferredDepthMax.Length > behaviourIndex;
        }

        private static void NormaliseBand(ref float minimum, ref float maximum) {
            if (maximum >= minimum) {
                return;
            }

            float temporary = minimum;
            minimum = maximum;
            maximum = temporary;
        }

        private static bool TryGetNormalisedDistanceToBoundary(
            float3 position,
            in FlockAttractorData data,
            out float3 offset,
            out float distance,
            out float normalisedDistanceToBoundary) {
            if (data.Shape == FlockAttractorShape.Sphere) {
                offset = data.Position - position;
                distance = math.length(offset);

                float radius = math.max(data.Radius, 0.0001f);

                if (distance > radius) {
                    normalisedDistanceToBoundary = 0f;
                    return false;
                }

                normalisedDistanceToBoundary = math.saturate(distance / radius);
                return true;
            }

            float3 toCenter = position - data.Position;
            quaternion inverseRotation = math.conjugate(data.BoxRotation);
            float3 local = math.mul(inverseRotation, toCenter);

            float3 absoluteLocal = math.abs(local);
            float3 halfExtents = math.max(data.BoxHalfExtents, new float3(0.0001f));

            float3 normalised = absoluteLocal / halfExtents;

            float maxComponent = math.max(normalised.x, math.max(normalised.y, normalised.z));

            if (maxComponent > 1.0f) {
                offset = float3.zero;
                distance = 0f;
                normalisedDistanceToBoundary = 0f;
                return false;
            }

            normalisedDistanceToBoundary = math.saturate(maxComponent);

            offset = data.Position - position;
            distance = math.length(offset);
            return true;
        }

        private static bool TryComputeBoundaryAttractionSteering(
            in FlockAttractorData data,
            float typeResponse,
            float3 offset,
            float distance,
            float normalisedDistanceToBoundary,
            out float3 steering) {
            const float InnerRegionFraction = 0.6f;

            if (normalisedDistanceToBoundary <= InnerRegionFraction) {
                steering = float3.zero;
                return false;
            }

            float denominator = math.max(1.0f - InnerRegionFraction, 0.0001f);
            float edgeDistance = (normalisedDistanceToBoundary - InnerRegionFraction) / denominator;

            float falloffPower = math.max(0.1f, data.FalloffPower);
            float falloff = math.pow(edgeDistance, falloffPower);

            float strength = data.BaseStrength * typeResponse * falloff;

            if (strength <= 0.0f || distance < 1e-4f) {
                steering = float3.zero;
                return false;
            }

            float3 direction = offset / math.max(distance, 0.0001f);
            steering = direction * strength;
            return true;
        }

        private int GetCellIndex(float3 position) {
            float safeCellSize = math.max(CellSize, 0.0001f);
            float3 local = (position - GridOrigin) / safeCellSize;

            int3 cellCoordinates = (int3)math.floor(local);
            int3 gridResolution = GridResolution;

            if (cellCoordinates.x < 0 || cellCoordinates.y < 0 || cellCoordinates.z < 0
                || cellCoordinates.x >= gridResolution.x
                || cellCoordinates.y >= gridResolution.y
                || cellCoordinates.z >= gridResolution.z) {
                return -1;
            }

            int layerSize = gridResolution.x * gridResolution.y;

            return cellCoordinates.x
                   + cellCoordinates.y * gridResolution.x
                   + cellCoordinates.z * layerSize;
        }
    }
}
