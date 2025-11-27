// File: Assets/Flock/Runtime/Jobs/AttractorSamplingJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct AttractorSamplingJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<int> BehaviourIds;

        [ReadOnly] public NativeArray<FlockAttractorData> Attractors;
        [ReadOnly] public NativeArray<float> BehaviourAttractionWeight;

        // Per-cell mapping to the winning Individual attractor index
        [ReadOnly] public NativeArray<int> CellToIndividualAttractor;

        [ReadOnly] public float3 GridOrigin;
        [ReadOnly] public int3 GridResolution;
        [ReadOnly] public float CellSize;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> AttractionSteering;

        public void Execute(int index) {
            float3 position = Positions[index];

            int behaviourIndex = BehaviourIds[index];
            if ((uint)behaviourIndex >= (uint)BehaviourAttractionWeight.Length) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            float typeResponse = BehaviourAttractionWeight[behaviourIndex];
            if (typeResponse <= 0.0f
                || !CellToIndividualAttractor.IsCreated
                || CellToIndividualAttractor.Length == 0) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            int cellIndex = GetCellIndex(position);
            if (cellIndex < 0
                || cellIndex >= CellToIndividualAttractor.Length) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            int attractorIndex = CellToIndividualAttractor[cellIndex];
            if (attractorIndex < 0
                || (uint)attractorIndex >= (uint)Attractors.Length) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            FlockAttractorData data = Attractors[attractorIndex];

            // Mask check: if AffectedTypesMask is not "all", require this type bit
            if (behaviourIndex < 32 && data.AffectedTypesMask != uint.MaxValue) {
                uint bit = 1u << behaviourIndex;
                if ((data.AffectedTypesMask & bit) == 0u) {
                    AttractionSteering[index] = float3.zero;
                    return;
                }
            }

            float3 offset;
            float distance;
            float t; // 0 center, 1 boundary

            if (data.Shape == FlockAttractorShape.Sphere) {
                offset = data.Position - position;
                distance = math.length(offset);

                float radius = math.max(data.Radius, 0.0001f);

                if (distance > radius) {
                    AttractionSteering[index] = float3.zero;
                    return;
                }

                t = math.saturate(distance / radius);
            } else {
                // Box: local space, normalised distance to faces
                float3 toCenter = position - data.Position;
                quaternion invRot = math.conjugate(data.BoxRotation);
                float3 local = math.mul(invRot, toCenter);
                float3 absLocal = math.abs(local);

                float3 halfExtents = math.max(
                    data.BoxHalfExtents,
                    new float3(0.0001f));

                float3 normalised = absLocal / halfExtents;
                float maxComponent = math.max(
                    normalised.x,
                    math.max(normalised.y, normalised.z));

                if (maxComponent > 1.0f) {
                    AttractionSteering[index] = float3.zero;
                    return;
                }

                t = math.saturate(maxComponent);

                // Steering direction still uses world-space center
                offset = data.Position - position;
                distance = math.length(offset);
            }

            // Inner region fraction: inner volume has NO radial pull.
            const float InnerRegionFraction = 0.6f;

            if (t <= InnerRegionFraction) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            // Map [InnerRegionFraction..1] → [0..1] near boundary
            float denom = math.max(1.0f - InnerRegionFraction, 0.0001f);
            float edgeT = (t - InnerRegionFraction) / denom;

            float falloffPower = math.max(0.1f, data.FalloffPower);
            float falloff = math.pow(edgeT, falloffPower);

            float strength = data.BaseStrength * typeResponse * falloff;
            if (strength <= 0.0f || distance < 1e-4f) {
                AttractionSteering[index] = float3.zero;
                return;
            }

            float3 dir = offset / math.max(distance, 0.0001f);
            AttractionSteering[index] = dir * strength;
        }

        int GetCellIndex(float3 position) {
            float cell = math.max(CellSize, 0.0001f);
            float3 local = (position - GridOrigin) / cell;
            int3 cellCoord = (int3)math.floor(local);
            int3 res = GridResolution;

            if (cellCoord.x < 0 || cellCoord.y < 0 || cellCoord.z < 0
                || cellCoord.x >= res.x
                || cellCoord.y >= res.y
                || cellCoord.z >= res.z) {
                return -1;
            }

            int layerSize = res.x * res.y;
            return cellCoord.x + cellCoord.y * res.x + cellCoord.z * layerSize;
        }
    }
}
