// REPLACE FILE: Assets/Flock/Runtime/Jobs/ObstacleAvoidanceJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct ObstacleAvoidanceJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> Velocities;

        [ReadOnly] public NativeArray<int> BehaviourIds;
        [ReadOnly] public NativeArray<float> BehaviourMaxSpeed;
        [ReadOnly] public NativeArray<float> BehaviourMaxAcceleration;
        [ReadOnly] public NativeArray<float> BehaviourSeparationRadius;

        [ReadOnly] public NativeArray<FlockObstacleData> Obstacles;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> CellToObstacles;

        public float3 GridOrigin;
        public int3 GridResolution;
        public float CellSize;

        public float AvoidStrength;

        [WriteOnly] public NativeArray<float3> ObstacleSteering;

        public void Execute(int agentIndex) {
            float3 agentPosition = Positions[agentIndex];
            float3 agentVelocity = Velocities[agentIndex];

            if (!TryGetForwardAndSpeed(agentVelocity, out float3 forwardDir, out float speed)) {
                ObstacleSteering[agentIndex] = float3.zero;
                return;
            }

            int behaviourIndex = BehaviourIds[agentIndex];

            float maxSpeed = BehaviourMaxSpeed[behaviourIndex];
            float maxAcceleration = BehaviourMaxAcceleration[behaviourIndex];
            float separationRadius = BehaviourSeparationRadius[behaviourIndex];

            float lookAhead = ComputeLookAhead(separationRadius, speed, maxSpeed);

            float safeCellSize = math.max(CellSize, 0.0001f);
            int3 gridRes = GridResolution;

            int3 agentCell = GetCell(agentPosition, GridOrigin, gridRes, safeCellSize);

            ComputeSearchBounds(
                agentCell,
                gridRes,
                lookAhead,
                safeCellSize,
                out int minCellX,
                out int maxCellX,
                out int minCellY,
                out int maxCellY,
                out int minCellZ,
                out int maxCellZ);

            float3 bestDir = float3.zero;
            float bestDanger = 0.0f;

            FixedList512Bytes<int> seenObstacleIndices = default;

            int layerSize = gridRes.x * gridRes.y;

            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ += 1) {
                for (int cellY = minCellY; cellY <= maxCellY; cellY += 1) {
                    int rowBase = cellY * gridRes.x + cellZ * layerSize;

                    for (int cellX = minCellX; cellX <= maxCellX; cellX += 1) {
                        int neighbourCellId = cellX + rowBase;

                        ProcessCell(
                            neighbourCellId,
                            agentPosition,
                            forwardDir,
                            lookAhead,
                            separationRadius,
                            ref seenObstacleIndices,
                            ref bestDanger,
                            ref bestDir);
                    }
                }
            }

            if (bestDanger <= 0.0f || math.lengthsq(bestDir) < 1e-8f) {
                ObstacleSteering[agentIndex] = float3.zero;
                return;
            }

            float avoidAccel = bestDanger * maxAcceleration * math.max(0f, AvoidStrength);
            ObstacleSteering[agentIndex] = math.normalizesafe(bestDir, float3.zero) * avoidAccel;
        }

        static bool TryGetForwardAndSpeed(float3 velocity, out float3 forwardDir, out float speed) {
            float speedSqr = math.lengthsq(velocity);
            if (speedSqr < 1e-8f) {
                forwardDir = float3.zero;
                speed = 0f;
                return false;
            }

            float inverseSpeed = math.rsqrt(speedSqr);
            forwardDir = velocity * inverseSpeed;     // normalized
            speed = speedSqr * inverseSpeed;          // == sqrt(speedSqr)
            return true;
        }

        static float ComputeLookAhead(float separationRadius, float speed, float maxSpeed) {
            float baseLookAhead = math.max(separationRadius * 2.0f, 0.5f);
            float speedFactor = (maxSpeed > 1e-6f) ? math.saturate(speed / maxSpeed) : 1.0f;
            return baseLookAhead + separationRadius * 3.0f * speedFactor;
        }

        static void ComputeSearchBounds(
            int3 centerCell,
            int3 gridRes,
            float lookAhead,
            float cellSize,
            out int minCellX,
            out int maxCellX,
            out int minCellY,
            out int maxCellY,
            out int minCellZ,
            out int maxCellZ) {

            int cellRange = (int)math.ceil(lookAhead / cellSize) + 1;
            cellRange = math.max(cellRange, 1);

            minCellX = math.max(centerCell.x - cellRange, 0);
            maxCellX = math.min(centerCell.x + cellRange, gridRes.x - 1);

            minCellY = math.max(centerCell.y - cellRange, 0);
            maxCellY = math.min(centerCell.y + cellRange, gridRes.y - 1);

            minCellZ = math.max(centerCell.z - cellRange, 0);
            maxCellZ = math.min(centerCell.z + cellRange, gridRes.z - 1);
        }

        void ProcessCell(
            int cellId,
            float3 agentPosition,
            float3 forwardDir,
            float lookAhead,
            float separationRadius,
            ref FixedList512Bytes<int> seenObstacleIndices,
            ref float bestDanger,
            ref float3 bestDir) {

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
                        forwardDir,
                        lookAhead,
                        separationRadius,
                        obstacle.Position,
                        math.max(0.0f, obstacle.Radius),
                        ref bestDanger,
                        ref bestDir);
                } else {
                    EvaluateBox(
                        agentPosition,
                        forwardDir,
                        lookAhead,
                        separationRadius,
                        obstacle.Position,
                        obstacle.BoxHalfExtents,
                        obstacle.BoxRotation,
                        ref bestDanger,
                        ref bestDir);
                }

            } while (CellToObstacles.TryGetNextValue(out obstacleIndex, ref iterator));
        }

        static bool IsWithinBroadRange(
            float3 agentPosition,
            float lookAhead,
            float separationRadius,
            float3 obstaclePosition,
            float obstacleRadius) {

            float broadRadius = math.max(0.0f, obstacleRadius) + separationRadius;
            float maxRange = lookAhead + broadRadius;

            float3 toObstacle = obstaclePosition - agentPosition;
            float distToCenterSqr = math.lengthsq(toObstacle);

            return distToCenterSqr <= (maxRange * maxRange);
        }

        static bool Contains(in FixedList512Bytes<int> list, int value) {
            for (int listIndex = 0; listIndex < list.Length; listIndex += 1) {
                if (list[listIndex] == value) {
                    return true;
                }
            }
            return false;
        }

        static void EvaluateSphere(
            float3 agentPosition,
            float3 forwardDir,
            float lookAhead,
            float separationRadius,
            float3 sphereCenter,
            float sphereRadius,
            ref float bestDanger,
            ref float3 bestDir) {

            float expandedRadius = sphereRadius + separationRadius;
            float expandedRadiusSqr = expandedRadius * expandedRadius;

            float3 toCenter = sphereCenter - agentPosition;
            float distCenterSqr = math.lengthsq(toCenter);

            // Inside safety sphere: push out strongly
            if (distCenterSqr < expandedRadiusSqr) {
                float distCenter = math.sqrt(math.max(distCenterSqr, 1e-6f));
                float penetration = expandedRadius - distCenter;

                if (penetration > 0.0f) {
                    float penetrationFactor = math.saturate(penetration / math.max(expandedRadius, 1e-3f));
                    float dangerInside = 0.5f + 0.5f * penetrationFactor;

                    float3 exitDir = math.normalizesafe(
                        agentPosition - sphereCenter,
                        -forwardDir);

                    if (dangerInside > bestDanger) {
                        bestDanger = dangerInside;
                        bestDir = exitDir;
                    }
                }

                return;
            }

            // Path-based intersection against expanded sphere
            float forwardDist = math.dot(toCenter, forwardDir);
            if (forwardDist < 0.0f || forwardDist > lookAhead) {
                return;
            }

            float3 projected = forwardDir * forwardDist;
            float3 lateral = toCenter - projected;
            float lateralSqr = math.lengthsq(lateral);

            if (lateralSqr > expandedRadiusSqr) {
                return;
            }

            float lateralDist = math.sqrt(math.max(lateralSqr, 1e-6f));
            float penetrationLateral = expandedRadius - lateralDist;
            if (penetrationLateral <= 0.0f) {
                return;
            }

            float timeFactor = 1.0f - (forwardDist / math.max(lookAhead, 1e-3f));
            float penetrationFactorLateral = penetrationLateral / math.max(expandedRadius, 1e-3f);

            float danger = timeFactor * penetrationFactorLateral;

            float3 lateralDir = math.normalizesafe(
                -lateral,
                -forwardDir);

            if (danger > bestDanger) {
                bestDanger = danger;
                bestDir = lateralDir;
            }
        }

        static void EvaluateBox(
            float3 agentPosition,
            float3 forwardDir,
            float lookAhead,
            float separationRadius,
            float3 boxCenter,
            float3 boxHalfExtents,
            quaternion boxRot,
            ref float bestDanger,
            ref float3 bestDir) {

            float3 expandedHalfExtents = new float3(
                math.max(0.0f, boxHalfExtents.x) + separationRadius,
                math.max(0.0f, boxHalfExtents.y) + separationRadius,
                math.max(0.0f, boxHalfExtents.z) + separationRadius);

            // Degenerate box → ignore
            if (expandedHalfExtents.x <= 0f || expandedHalfExtents.y <= 0f || expandedHalfExtents.z <= 0f) {
                return;
            }

            quaternion inverseRot = math.inverse(boxRot);

            float3 originLocal = math.mul(inverseRot, agentPosition - boxCenter); // ray origin in box local
            float3 directionLocal = math.mul(inverseRot, forwardDir);            // ray dir in box local (normalized)

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
                // Inside: stronger response, scaled by how deep we are (approx).
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

            float3 normalWorld = math.mul(boxRot, normalLocal);
            if (math.lengthsq(normalWorld) < 1e-8f) {
                return;
            }

            bestDanger = danger;
            bestDir = normalWorld;
        }

        static bool RayAabbSegmentHit(
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

            if (timeExit < 0f) return false;
            if (timeEnter > maxTime) return false;

            hitTime = math.max(timeEnter, 0f);
            normalLocal = enterNormal;

            return math.lengthsq(normalLocal) > 1e-6f;
        }

        static bool UpdateSlab(
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

        static float3 ComputeInsideExitNormal(float3 originLocal, float3 directionLocal, float3 halfExtents) {
            float3 absOrigin = math.abs(originLocal);

            float distToFaceX = halfExtents.x - absOrigin.x;
            float distToFaceY = halfExtents.y - absOrigin.y;
            float distToFaceZ = halfExtents.z - absOrigin.z;

            int axisIndex = 0;
            float minDist = distToFaceX;

            if (distToFaceY < minDist) { minDist = distToFaceY; axisIndex = 1; }
            if (distToFaceZ < minDist) { minDist = distToFaceZ; axisIndex = 2; }

            // If we're near the center (ties), bias by direction.
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

        static int3 GetCell(float3 position, float3 origin, int3 gridRes, float cellSize) {
            float3 scaled = (position - origin) / cellSize;
            int3 cell = (int3)math.floor(scaled);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                gridRes - new int3(1, 1, 1));
        }
    }
}
