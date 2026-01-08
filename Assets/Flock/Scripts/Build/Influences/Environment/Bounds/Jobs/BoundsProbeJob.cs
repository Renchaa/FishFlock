using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;

using Flock.Scripts.Build.Agents.Fish.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Scripts.Build.Influence.Environment.Bounds.Data;

namespace Flock.Scripts.Build.Influence.Environment.Bounds.Jobs {
    /**
     * <summary>
     * Computes per-agent wall direction + danger based only on position and bounds.
     * No velocity, no obstacles. Pure environment probe.
     * </summary>
     */
    [BurstCompile]
    public struct BoundsProbeJob : IJobParallelFor {
        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public NativeArray<FlockBehaviourSettings> BehaviourSettings;

        [ReadOnly]
        public FlockEnvironmentData EnvironmentData;

        [WriteOnly]
        public NativeArray<float3> WallDirections;

        [WriteOnly]
        public NativeArray<float> WallDangers;

        public void Execute(int index) {
            float3 position = Positions[index];

            WallDirections[index] = float3.zero;
            WallDangers[index] = 0f;

            if (!TryGetSeparationMargin(index, out float margin)) {
                return;
            }

            switch (EnvironmentData.BoundsType) {
                case FlockBoundsType.Box:
                    TryProbeBoxBounds(index, position, margin);
                    return;

                case FlockBoundsType.Sphere:
                    TryProbeSphereBounds(index, position, margin);
                    return;
            }
        }

        private bool TryGetSeparationMargin(int index, out float margin) {
            int behaviourIndex = BehaviourIds[index];

            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                margin = 0f;
                return false;
            }

            float separationRadius = math.max(BehaviourSettings[behaviourIndex].SeparationRadius, 0.01f);
            margin = separationRadius;
            return true;
        }

        private void TryProbeBoxBounds(int index, float3 position, float margin) {
            float3 center = EnvironmentData.BoundsCenter;
            float3 extents = EnvironmentData.BoundsExtents;

            if (extents.x <= 0f && extents.y <= 0f && extents.z <= 0f) {
                return;
            }

            float3 minimum = center - extents;
            float3 maximum = center + extents;

            float3 accumulatedDirection = float3.zero;
            float maximumDanger = 0f;

            AccumulateWallContribution(
                positionComponent: position.x,
                wallMinimum: minimum.x,
                wallMaximum: maximum.x,
                margin: margin,
                inwardNormal: new float3(1f, 0f, 0f),
                accumulatedDirection: ref accumulatedDirection,
                maximumDanger: ref maximumDanger);

            AccumulateWallContribution(
                positionComponent: position.x,
                wallMinimum: minimum.x,
                wallMaximum: maximum.x,
                margin: margin,
                inwardNormal: new float3(-1f, 0f, 0f),
                accumulatedDirection: ref accumulatedDirection,
                maximumDanger: ref maximumDanger,
                isMaximumSide: true);

            AccumulateWallContribution(
                positionComponent: position.y,
                wallMinimum: minimum.y,
                wallMaximum: maximum.y,
                margin: margin,
                inwardNormal: new float3(0f, 1f, 0f),
                accumulatedDirection: ref accumulatedDirection,
                maximumDanger: ref maximumDanger);

            AccumulateWallContribution(
                positionComponent: position.y,
                wallMinimum: minimum.y,
                wallMaximum: maximum.y,
                margin: margin,
                inwardNormal: new float3(0f, -1f, 0f),
                accumulatedDirection: ref accumulatedDirection,
                maximumDanger: ref maximumDanger,
                isMaximumSide: true);

            AccumulateWallContribution(
                positionComponent: position.z,
                wallMinimum: minimum.z,
                wallMaximum: maximum.z,
                margin: margin,
                inwardNormal: new float3(0f, 0f, 1f),
                accumulatedDirection: ref accumulatedDirection,
                maximumDanger: ref maximumDanger);

            AccumulateWallContribution(
                positionComponent: position.z,
                wallMinimum: minimum.z,
                wallMaximum: maximum.z,
                margin: margin,
                inwardNormal: new float3(0f, 0f, -1f),
                accumulatedDirection: ref accumulatedDirection,
                maximumDanger: ref maximumDanger,
                isMaximumSide: true);

            if (math.lengthsq(accumulatedDirection) < 1e-8f || maximumDanger <= 0f) {
                return;
            }

            WallDirections[index] = math.normalizesafe(accumulatedDirection, float3.zero);
            WallDangers[index] = maximumDanger;
        }

        private void TryProbeSphereBounds(int index, float3 position, float margin) {
            float radius = EnvironmentData.BoundsRadius;

            if (radius <= 0f) {
                return;
            }

            float3 center = EnvironmentData.BoundsCenter;
            float3 offset = position - center;

            float distanceSquared = math.lengthsq(offset);

            if (distanceSquared < 1e-8f) {
                return;
            }

            float distance = math.sqrt(distanceSquared);
            float distanceToSurface = radius - distance;

            if (distanceToSurface >= margin) {
                return;
            }

            float t = 1f - math.saturate(distanceToSurface / math.max(margin, 0.0001f));

            if (t <= 0f) {
                return;
            }

            float3 inwardNormal = -offset / distance;

            WallDirections[index] = inwardNormal;
            WallDangers[index] = t;
        }

        private void AccumulateWallContribution(
            float positionComponent,
            float wallMinimum,
            float wallMaximum,
            float margin,
            float3 inwardNormal,
            ref float3 accumulatedDirection,
            ref float maximumDanger,
            bool isMaximumSide = false) {
            float distanceInterior = isMaximumSide
                ? (wallMaximum - positionComponent)
                : (positionComponent - wallMinimum);

            if (distanceInterior >= margin) {
                return;
            }

            float t = 1f - math.saturate(distanceInterior / math.max(margin, 0.0001f));

            if (t <= 0f) {
                return;
            }

            accumulatedDirection += inwardNormal * t;

            if (t > maximumDanger) {
                maximumDanger = t;
            }
        }
    }
}
