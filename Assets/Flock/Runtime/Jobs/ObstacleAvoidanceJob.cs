// File: Assets/Flock/Runtime/Jobs/ObstacleAvoidanceJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /**
     * <summary>
     * Computes per-agent obstacle avoidance steering using a grid broad-phase over obstacles.
     * </summary>
     */
    [BurstCompile]
    public struct ObstacleAvoidanceJob : IJobParallelFor {
        #region Inputs

        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<float3> Velocities;

        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public NativeArray<float> BehaviourMaxSpeed;

        [ReadOnly]
        public NativeArray<float> BehaviourMaxAcceleration;

        [ReadOnly]
        public NativeArray<float> BehaviourSeparationRadius;

        [ReadOnly]
        public NativeArray<FlockObstacleData> Obstacles;

        [ReadOnly]
        public NativeParallelMultiHashMap<int, int> CellToObstacles;

        public float3 GridOrigin;

        public int3 GridResolution;

        public float CellSize;

        public float AvoidStrength;

        #endregion

        #region Outputs

        [WriteOnly]
        public NativeArray<float3> ObstacleSteering;

        #endregion

        public void Execute(int agentIndex) {
            float3 agentPosition = Positions[agentIndex];
            float3 agentVelocity = Velocities[agentIndex];

            if (!TryGetForwardAndSpeed(agentVelocity, out float3 forwardDirection, out float speed)) {
                ObstacleSteering[agentIndex] = float3.zero;
                return;
            }

            int behaviourIndex = BehaviourIds[agentIndex];

            float maxSpeed = BehaviourMaxSpeed[behaviourIndex];
            float maxAcceleration = BehaviourMaxAcceleration[behaviourIndex];
            float separationRadius = BehaviourSeparationRadius[behaviourIndex];

            float lookAhead = ComputeLookAhead(separationRadius, speed, maxSpeed);

            float safeCellSize = math.max(CellSize, 0.0001f);
            int3 gridResolution = GridResolution;

            int3 agentCell = GetCell(agentPosition, GridOrigin, gridResolution, safeCellSize);

            ComputeSearchBounds(
                agentCell,
                gridResolution,
                lookAhead,
                safeCellSize,
                out int minimumCellX,
                out int maximumCellX,
                out int minimumCellY,
                out int maximumCellY,
                out int minimumCellZ,
                out int maximumCellZ);

            float3 bestDirection = float3.zero;
            float bestDanger = 0.0f;

            FixedList512Bytes<int> seenObstacleIndices = default;

            int layerSize = gridResolution.x * gridResolution.y;

            for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ += 1) {
                for (int cellY = minimumCellY; cellY <= maximumCellY; cellY += 1) {
                    int rowBase = cellY * gridResolution.x + cellZ * layerSize;

                    for (int cellX = minimumCellX; cellX <= maximumCellX; cellX += 1) {
                        int neighbourCellId = cellX + rowBase;

                        ProcessCell(
                            neighbourCellId,
                            agentPosition,
                            forwardDirection,
                            lookAhead,
                            separationRadius,
                            ref seenObstacleIndices,
                            ref bestDanger,
                            ref bestDirection);
                    }
                }
            }

            if (bestDanger <= 0.0f || math.lengthsq(bestDirection) < 1e-8f) {
                ObstacleSteering[agentIndex] = float3.zero;
                return;
            }

            float avoidAcceleration = bestDanger * maxAcceleration * math.max(0f, AvoidStrength);
            ObstacleSteering[agentIndex] = math.normalizesafe(bestDirection, float3.zero) * avoidAcceleration;
        }

        private static bool TryGetForwardAndSpeed(float3 velocity, out float3 forwardDirection, out float speed) {
            float speedSquared = math.lengthsq(velocity);
            if (speedSquared < 1e-8f) {
                forwardDirection = float3.zero;
                speed = 0f;
                return false;
            }

            float inverseSpeed = math.rsqrt(speedSquared);
            forwardDirection = velocity * inverseSpeed;
            speed = speedSquared * inverseSpeed;
            return true;
        }

        private static float ComputeLookAhead(float separationRadius, float speed, float maxSpeed) {
            float baseLookAhead = math.max(separationRadius * 2.0f, 0.5f);
            float speedFactor = (maxSpeed > 1e-6f) ? math.saturate(speed / maxSpeed) : 1.0f;
            return baseLookAhead + separationRadius * 3.0f * speedFactor;
        }

        private static void ComputeSearchBounds(
            int3 centerCell,
            int3 gridResolution,
            float lookAhead,
            float cellSize,
            out int minimumCellX,
            out int maximumCellX,
            out int minimumCellY,
            out int maximumCellY,
            out int minimumCellZ,
            out int maximumCellZ) {

            int cellRange = (int)math.ceil(lookAhead / cellSize) + 1;
            cellRange = math.max(cellRange, 1);

            minimumCellX = math.max(centerCell.x - cellRange, 0);
            maximumCellX = math.min(centerCell.x + cellRange, gridResolution.x - 1);

            minimumCellY = math.max(centerCell.y - cellRange, 0);
            maximumCellY = math.min(centerCell.y + cellRange, gridResolution.y - 1);

            minimumCellZ = math.max(centerCell.z - cellRange, 0);
            maximumCellZ = math.min(centerCell.z + cellRange, gridResolution.z - 1);
        }

        private void ProcessCell(
            int cellId,
            float3 agentPosition,
            float3 forwardDirection,
            float lookAhead,
            float separationRadius,
            ref FixedList512Bytes<int> seenObstacleIndices,
            ref float bestDanger,
            ref float3 bestDirection) {

            if (!CellToObstacles.TryGetFirstValue(
                    cellId,
                    out int obstacleIndex,
                    out NativeParallelMultiHashMapIterator<int> iterator)) {
                return;
            }

            do {
                if (Contains(seenObstacleIndices, obstacleIndex)) {
                    continue;
                }

                if (seenObstacleIndices.Length < seenObstacleIndices.Capacity) {
                    seenObstacleIndices.Add(obstacleIndex);
                }

                FlockObstacleData obstacle = Obstacles[obstacleIndex];

                if (!IsWithinBroadRange(agentPosition, lookAhead, separationRadius, obstacle.Position, obstacle.Radius)) {
                    continue;
                }

                if (obstacle.Shape == FlockObstacleShape.Sphere) {
                    EvaluateSphere(
                        agentPosition,
                        forwardDirection,
                        lookAhead,
                        separationRadius,
                        obstacle.Position,
                        math.max(0.0f, obstacle.Radius),
                        ref bestDanger,
                        ref bestDirection);
                } else {
                    EvaluateBox(
                        agentPosition,
                        forwardDirection,
                        lookAhead,
                        separationRadius,
                        obstacle.Position,
                        obstacle.BoxHalfExtents,
                        obstacle.BoxRotation,
                        ref bestDanger,
                        ref bestDirection);
                }

            } while (CellToObstacles.TryGetNextValue(out obstacleIndex, ref iterator));
        }

        private static bool IsWithinBroadRange(
            float3 agentPosition,
            float lookAhead,
            float separationRadius,
            float3 obstaclePosition,
            float obstacleRadius) {

            float broadRadius = math.max(0.0f, obstacleRadius) + separationRadius;
            float maxRange = lookAhead + broadRadius;

            float3 toObstacle = obstaclePosition - agentPosition;
            float distanceToCenterSquared = math.lengthsq(toObstacle);

            return distanceToCenterSquared <= (maxRange * maxRange);
        }

        private static bool Contains(in FixedList512Bytes<int> list, int value) {
            for (int listIndex = 0; listIndex < list.Length; listIndex += 1) {
                if (list[listIndex] == value) {
                    return true;
                }
            }

            return false;
        }

        private static void EvaluateSphere(
            float3 agentPosition,
            float3 forwardDirection,
            float lookAhead,
            float separationRadius,
            float3 sphereCenter,
            float sphereRadius,
            ref float bestDanger,
            ref float3 bestDirection) {

            float expandedRadius = sphereRadius + separationRadius;
            float expandedRadiusSquared = expandedRadius * expandedRadius;

            float3 toCenter = sphereCenter - agentPosition;
            float distanceCenterSquared = math.lengthsq(toCenter);

            if (distanceCenterSquared < expandedRadiusSquared) {
                float distanceCenter = math.sqrt(math.max(distanceCenterSquared, 1e-6f));
                float penetration = expandedRadius - distanceCenter;

                if (penetration > 0.0f) {
                    float penetrationFactor = math.saturate(penetration / math.max(expandedRadius, 1e-3f));
                    float dangerInside = 0.5f + 0.5f * penetrationFactor;

                    float3 exitDirection = math.normalizesafe(
                        agentPosition - sphereCenter,
                        -forwardDirection);

                    if (dangerInside > bestDanger) {
                        bestDanger = dangerInside;
                        bestDirection = exitDirection;
                    }
                }

                return;
            }

            float forwardDistance = math.dot(toCenter, forwardDirection);
            if (forwardDistance < 0.0f || forwardDistance > lookAhead) {
                return;
            }

            float3 projected = forwardDirection * forwardDistance;
            float3 lateral = toCenter - projected;
            float lateralSquared = math.lengthsq(lateral);

            if (lateralSquared > expandedRadiusSquared) {
                return;
            }

            float lateralDistance = math.sqrt(math.max(lateralSquared, 1e-6f));
            float penetrationLateral = expandedRadius - lateralDistance;
            if (penetrationLateral <= 0.0f) {
                return;
            }

            float timeFactor = 1.0f - (forwardDistance / math.max(lookAhead, 1e-3f));
            float penetrationFactorLateral = penetrationLateral / math.max(expandedRadius, 1e-3f);

            float danger = timeFactor * penetrationFactorLateral;

            float3 lateralDirection = math.normalizesafe(
                -lateral,
                -forwardDirection);

            if (danger > bestDanger) {
                bestDanger = danger;
                bestDirection = lateralDirection;
            }
        }

        private static void EvaluateBox(
            float3 agentPosition,
            float3 forwardDirection,
            float lookAhead,
            float separationRadius,
            float3 boxCenter,
            float3 boxHalfExtents,
            quaternion boxRotation,
            ref float bestDanger,
            ref float3 bestDirection) {

            float3 expandedHalfExtents = new float3(
                math.max(0.0f, boxHalfExtents.x) + separationRadius,
                math.max(0.0f, boxHalfExtents.y) + separationRadius,
                math.max(0.0f, boxHalfExtents.z) + separationRadius);

            if (expandedHalfExtents.x <= 0f || expandedHalfExtents.y <= 0f || expandedHalfExtents.z <= 0f) {
                return;
            }

            quaternion inverseRotation = math.inverse(boxRotation);

            float3 originLocal = math.mul(inverseRotation, agentPosition - boxCenter);
            float3 directionLocal = math.mul(inverseRotation, forwardDirection);

            if (!RayAabbSegmentHit(
                    originLocal,
                    directionLocal,
                    expandedHalfExtents,
                    lookAhead,
                    out float hitTime,
                    out float3 normalLocal,
                    out bool startsInside)) {
                return;
            }

            float danger;
            if (startsInside) {
                float3 absOrigin = math.abs(originLocal);

                float distToFaceX = expandedHalfExtents.x - absOrigin.x;
                float distToFaceY = expandedHalfExtents.y - absOrigin.y;
                float distToFaceZ = expandedHalfExtents.z - absOrigin.z;

                float minFaceDist = math.min(distToFaceX, math.min(distToFaceY, distToFaceZ));
                float minHalfExtent = math.max(math.cmin(expandedHalfExtents), 1e-3f);

                float penetrationFactor = 1.0f - math.saturate(minFaceDist / minHalfExtent);
                danger = 0.5f + 0.5f * penetrationFactor;
            } else {
                float timeFactor = 1.0f - (hitTime / math.max(lookAhead, 1e-3f));
                danger = math.saturate(timeFactor);
            }

            if (danger <= bestDanger) {
                return;
            }

            float3 normalWorld = math.mul(boxRotation, normalLocal);
            if (math.lengthsq(normalWorld) < 1e-8f) {
                return;
            }

            bestDanger = danger;
            bestDirection = normalWorld;
        }

        private static bool RayAabbSegmentHit(
            float3 originLocal,
            float3 directionLocal,
            float3 halfExtents,
            float maxTime,
            out float hitTime,
            out float3 normalLocal,
            out bool startsInside) {

            hitTime = 0f;
            normalLocal = float3.zero;

            startsInside =
                math.abs(originLocal.x) <= halfExtents.x &&
                math.abs(originLocal.y) <= halfExtents.y &&
                math.abs(originLocal.z) <= halfExtents.z;

            if (startsInside) {
                normalLocal = ComputeInsideExitNormal(originLocal, directionLocal, halfExtents);
                hitTime = 0f;
                return true;
            }

            float timeEnter = 0f;
            float timeExit = maxTime;
            float3 enterNormal = float3.zero;

            const float parallelEpsilon = 1e-8f;

            if (!UpdateSlab(
                    originLocal.x,
                    directionLocal.x,
                    halfExtents.x,
                    new float3(-1f, 0f, 0f),
                    new float3(1f, 0f, 0f),
                    parallelEpsilon,
                    ref timeEnter,
                    ref timeExit,
                    ref enterNormal)) {
                return false;
            }

            if (!UpdateSlab(
                    originLocal.y,
                    directionLocal.y,
                    halfExtents.y,
                    new float3(0f, -1f, 0f),
                    new float3(0f, 1f, 0f),
                    parallelEpsilon,
                    ref timeEnter,
                    ref timeExit,
                    ref enterNormal)) {
                return false;
            }

            if (!UpdateSlab(
                    originLocal.z,
                    directionLocal.z,
                    halfExtents.z,
                    new float3(0f, 0f, -1f),
                    new float3(0f, 0f, 1f),
                    parallelEpsilon,
                    ref timeEnter,
                    ref timeExit,
                    ref enterNormal)) {
                return false;
            }

            if (timeExit < 0f) {
                return false;
            }

            if (timeEnter > maxTime) {
                return false;
            }

            hitTime = math.max(timeEnter, 0f);
            normalLocal = enterNormal;

            return math.lengthsq(normalLocal) > 1e-6f;
        }

        private static bool UpdateSlab(
            float originComponent,
            float directionComponent,
            float halfExtent,
            float3 normalWhenMinIsNear,
            float3 normalWhenMaxIsNear,
            float parallelEpsilon,
            ref float timeEnter,
            ref float timeExit,
            ref float3 enterNormal) {

            if (math.abs(directionComponent) < parallelEpsilon) {
                return !(originComponent < -halfExtent || originComponent > halfExtent);
            }

            float inverseDirection = 1.0f / directionComponent;

            float timeToMinPlane = (-halfExtent - originComponent) * inverseDirection;
            float timeToMaxPlane = (halfExtent - originComponent) * inverseDirection;

            float nearTime = math.min(timeToMinPlane, timeToMaxPlane);
            float farTime = math.max(timeToMinPlane, timeToMaxPlane);

            float3 nearNormal = (timeToMinPlane < timeToMaxPlane) ? normalWhenMinIsNear : normalWhenMaxIsNear;

            if (nearTime > timeEnter) {
                timeEnter = nearTime;
                enterNormal = nearNormal;
            }

            timeExit = math.min(timeExit, farTime);
            return timeEnter <= timeExit;
        }

        private static float3 ComputeInsideExitNormal(float3 originLocal, float3 directionLocal, float3 halfExtents) {
            float3 absOrigin = math.abs(originLocal);

            float distToFaceX = halfExtents.x - absOrigin.x;
            float distToFaceY = halfExtents.y - absOrigin.y;
            float distToFaceZ = halfExtents.z - absOrigin.z;

            int axisIndex = 0;
            float minDist = distToFaceX;

            if (distToFaceY < minDist) { minDist = distToFaceY; axisIndex = 1; }
            if (distToFaceZ < minDist) { minDist = distToFaceZ; axisIndex = 2; }

            float3 absDir = math.abs(directionLocal);
            if (minDist > math.cmin(halfExtents) * 0.999f) {
                axisIndex = absDir.x >= absDir.y
                    ? (absDir.x >= absDir.z ? 0 : 2)
                    : (absDir.y >= absDir.z ? 1 : 2);
            }

            if (axisIndex == 0) {
                float signValue = (originLocal.x != 0f) ? math.sign(originLocal.x) : (directionLocal.x >= 0f ? 1f : -1f);
                return new float3(signValue, 0f, 0f);
            }

            if (axisIndex == 1) {
                float signValue = (originLocal.y != 0f) ? math.sign(originLocal.y) : (directionLocal.y >= 0f ? 1f : -1f);
                return new float3(0f, signValue, 0f);
            }

            float signValueZ = (originLocal.z != 0f) ? math.sign(originLocal.z) : (directionLocal.z >= 0f ? 1f : -1f);
            return new float3(0f, 0f, signValueZ);
        }

        private static int3 GetCell(float3 position, float3 origin, int3 gridResolution, float cellSize) {
            float3 scaled = (position - origin) / cellSize;
            int3 cell = (int3)math.floor(scaled);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                gridResolution - new int3(1, 1, 1));
        }
    }
}
