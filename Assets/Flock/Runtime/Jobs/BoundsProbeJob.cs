// File: Assets/Flock/Runtime/Jobs/BoundsProbeJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /// <summary>
    /// Computes per–agent wall direction + danger based only on position and bounds.
    /// No velocity, no obstacles. Pure environment probe.
    /// </summary>
    [BurstCompile]
    public struct BoundsProbeJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<int> BehaviourIds;
        [ReadOnly] public NativeArray<float> BehaviourSeparationRadius;

        [ReadOnly] public FlockEnvironmentData EnvironmentData;

        [WriteOnly] public NativeArray<float3> WallDirections; // inward direction
        [WriteOnly] public NativeArray<float> WallDangers;     // 0..1 (or slightly >1 if outside)

        public void Execute(int index) {
            float3 pos = Positions[index];

            WallDirections[index] = float3.zero;
            WallDangers[index] = 0f;

            int behaviourIndex = BehaviourIds[index];
            if ((uint)behaviourIndex >= (uint)BehaviourSeparationRadius.Length) {
                return;
            }

            // Use separation radius as "danger margin" near the boundary
            float separationRadius = math.max(BehaviourSeparationRadius[behaviourIndex], 0.01f);
            float margin = separationRadius;

            if (EnvironmentData.BoundsType == FlockBoundsType.Box) {
                float3 center = EnvironmentData.BoundsCenter;
                float3 extents = EnvironmentData.BoundsExtents;

                if (extents.x <= 0f && extents.y <= 0f && extents.z <= 0f) {
                    return;
                }

                float3 min = center - extents;
                float3 max = center + extents;

                float3 accumDir = float3.zero;
                float maxDanger = 0f;

                // X min wall (normal +X)
                AccumulateWallContribution(
                    posComponent: pos.x,
                    wallMin: min.x,
                    wallMax: max.x,
                    margin: margin,
                    inwardNormal: new float3(1f, 0f, 0f),
                    ref accumDir,
                    ref maxDanger);

                // X max wall (normal -X)
                AccumulateWallContribution(
                    posComponent: pos.x,
                    wallMin: min.x,
                    wallMax: max.x,
                    margin: margin,
                    inwardNormal: new float3(-1f, 0f, 0f),
                    ref accumDir,
                    ref maxDanger,
                    isMaxSide: true);

                // Y min wall (normal +Y)
                AccumulateWallContribution(
                    posComponent: pos.y,
                    wallMin: min.y,
                    wallMax: max.y,
                    margin: margin,
                    inwardNormal: new float3(0f, 1f, 0f),
                    ref accumDir,
                    ref maxDanger);

                // Y max wall (normal -Y)
                AccumulateWallContribution(
                    posComponent: pos.y,
                    wallMin: min.y,
                    wallMax: max.y,
                    margin: margin,
                    inwardNormal: new float3(0f, -1f, 0f),
                    ref accumDir,
                    ref maxDanger,
                    isMaxSide: true);

                // Z min wall (normal +Z)
                AccumulateWallContribution(
                    posComponent: pos.z,
                    wallMin: min.z,
                    wallMax: max.z,
                    margin: margin,
                    inwardNormal: new float3(0f, 0f, 1f),
                    ref accumDir,
                    ref maxDanger);

                // Z max wall (normal -Z)
                AccumulateWallContribution(
                    posComponent: pos.z,
                    wallMin: min.z,
                    wallMax: max.z,
                    margin: margin,
                    inwardNormal: new float3(0f, 0f, -1f),
                    ref accumDir,
                    ref maxDanger,
                    isMaxSide: true);

                if (math.lengthsq(accumDir) < 1e-8f || maxDanger <= 0f) {
                    return;
                }

                WallDirections[index] = math.normalizesafe(accumDir, float3.zero);
                WallDangers[index] = maxDanger;
            } else if (EnvironmentData.BoundsType == FlockBoundsType.Sphere) {
                float radius = EnvironmentData.BoundsRadius;
                if (radius <= 0f) {
                    return;
                }

                float3 center = EnvironmentData.BoundsCenter;
                float3 offset = pos - center;
                float distSq = math.lengthsq(offset);

                if (distSq < 1e-8f) {
                    // At center: no meaningful wall direction
                    return;
                }

                float dist = math.sqrt(distSq);
                float distanceToSurface = radius - dist; // >0 inside, <0 outside

                // Far from the wall, no danger
                if (distanceToSurface >= margin) {
                    return;
                }

                // 0 at margin, 1 on or beyond surface
                float t = 1f - math.saturate(distanceToSurface / math.max(margin, 0.0001f));
                if (t <= 0f) {
                    return;
                }

                // Inward = towards center
                float3 inwardNormal = -offset / dist;

                WallDirections[index] = inwardNormal;
                WallDangers[index] = t;
            }
        }

        /// <summary>
        /// Adds contribution of a single axis wall pair (min/max) to accumDir/maxDanger.
        /// This is symmetric for min/max, we just flip which side is "near".
        /// </summary>
        void AccumulateWallContribution(
            float posComponent,
            float wallMin,
            float wallMax,
            float margin,
            float3 inwardNormal,
            ref float3 accumDir,
            ref float maxDanger,
            bool isMaxSide = false) {

            // Distance to the relevant wall (inside: positive)
            float distInterior = isMaxSide
                ? (wallMax - posComponent)
                : (posComponent - wallMin);

            // If far from this wall, ignore
            if (distInterior >= margin) {
                return;
            }

            // distInterior < margin means we are inside the margin band or even outside.
            // Normalised closeness 0..1 (0 = at margin, 1 = on wall or outside)
            float t = 1f - math.saturate(distInterior / math.max(margin, 0.0001f));

            // If already outside, distInterior will be negative → t will clamp to 1.
            if (t <= 0f) {
                return;
            }

            accumDir += inwardNormal * t;
            if (t > maxDanger) {
                maxDanger = t;
            }
        }
    }
}
