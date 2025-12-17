// File: Assets/Flock/Runtime/Jobs/PatternBoxJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /// <summary>
    /// Layer-3 pattern: soft constraint towards an axis-aligned box "shell".
    /// Agents are nudged towards the faces of the box, with a configurable
    /// band thickness on either side of the faces.
    /// </summary>
    [BurstCompile]
    public struct PatternBoxJob : IJobParallelFor, IFlockLayer3PatternJob {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<int> BehaviourIds;

        // Written by this job, accumulated in FlockStepJob
        public NativeArray<float3> PatternSteering;

        [ReadOnly] public float3 Center;
        [ReadOnly] public float3 HalfExtents;   // positive per-axis half-size
        [ReadOnly] public float Thickness;      // band half-width around each face
        [ReadOnly] public float Strength;       // overall intensity
        [ReadOnly] public uint BehaviourMask;   // bitmask over behaviour indices

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
            if (!PatternSteering.IsCreated
                || !Positions.IsCreated
                || index < 0
                || index >= Positions.Length
                || index >= PatternSteering.Length) {
                return;
            }

            // Disabled / degenerate box → do nothing, keep existing steering.
            if (Strength <= 0f
                || Thickness <= 0f
                || HalfExtents.x <= 0f
                || HalfExtents.y <= 0f
                || HalfExtents.z <= 0f) {
                return;
            }

            // Per-pattern behaviour mask
            if (BehaviourIds.IsCreated) {
                int behaviourIndex = 0;

                if (index >= 0 && index < BehaviourIds.Length) {
                    behaviourIndex = BehaviourIds[index];
                }

                // BehaviourMask == uint.MaxValue → affect all behaviours
                if (BehaviourMask != uint.MaxValue) {
                    if (behaviourIndex < 0 || behaviourIndex >= 32) {
                        return;
                    }

                    uint bit = 1u << behaviourIndex;
                    if ((BehaviourMask & bit) == 0u) {
                        return;
                    }
                }
            }

            float3 pos = Positions[index];
            float3 rel = pos - Center;
            float3 absRel = math.abs(rel);
            float3 he = HalfExtents;

            // Normalised distance to center per axis (1.0 = exactly on that face)
            float3 norm = new float3(
                absRel.x / math.max(he.x, 1e-5f),
                absRel.y / math.max(he.y, 1e-5f),
                absRel.z / math.max(he.z, 1e-5f));

            // Choose dominant axis (closest to / crossing a face)
            int axis = 0;
            float axisNorm = norm.x;

            if (norm.y > axisNorm) {
                axis = 1;
                axisNorm = norm.y;
            }

            if (norm.z > axisNorm) {
                axis = 2;
                axisNorm = norm.z;
            }

            float signedD;
            float halfExtentAxis;

            switch (axis) {
                case 0:
                    signedD = rel.x;
                    halfExtentAxis = he.x;
                    break;
                case 1:
                    signedD = rel.y;
                    halfExtentAxis = he.y;
                    break;
                default:
                    signedD = rel.z;
                    halfExtentAxis = he.z;
                    break;
            }

            float d = math.abs(signedD);

            // If we're basically at the center and not near any face, skip
            if (d < 1e-5f && axisNorm < 1f) {
                return;
            }

            float bandWidth = math.max(Thickness, 0.0001f);

            // 1D "shell" band around |axis| = halfExtentAxis
            float inner = math.max(halfExtentAxis - Thickness, 0f);
            float outer = halfExtentAxis + Thickness;

            float radialScalar = 0f;

            if (d < inner) {
                // Too close to center → push outward towards face
                float tInner = math.saturate((inner - d) / bandWidth);
                radialScalar = +tInner;
            } else if (d > outer) {
                // Too far outside → pull inward back to shell
                float tOuter = math.saturate((d - outer) / bandWidth);
                radialScalar = -tOuter;
            } else {
                // Inside comfort band: soft bias toward exact face position
                float delta = d - halfExtentAxis;                 // -Thickness..+Thickness
                float normDelta = math.saturate(math.abs(delta) / bandWidth);
                float soften = 1f - normDelta;                    // 1 near the face, 0 near band edges
                float sign = delta > 0f ? -1f : 1f;               // inward/outward
                radialScalar = sign * soften * 0.25f;             // weak correction
            }

            // Axis-aligned normal for the chosen face
            float signAxis = signedD >= 0f ? 1f : -1f;

            float3 radialDir;
            switch (axis) {
                case 0:
                    radialDir = new float3(signAxis, 0f, 0f);
                    break;
                case 1:
                    radialDir = new float3(0f, signAxis, 0f);
                    break;
                default:
                    radialDir = new float3(0f, 0f, signAxis);
                    break;
            }

            // Accumulate into pattern steering so multiple patterns can stack
            PatternSteering[index] += radialDir * radialScalar * Strength;
        }
    }
}
