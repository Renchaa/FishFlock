// File: Assets/Flock/Runtime/Jobs/ObstacleAvoidanceJob.cs
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

        [ReadOnly] public float3 GridOrigin;
        [ReadOnly] public int3 GridResolution;
        [ReadOnly] public float CellSize;

        [ReadOnly] public float AvoidStrength;

        [WriteOnly] public NativeArray<float3> ObstacleSteering;

        public void Execute(int index) {
            float3 pos = Positions[index];
            float3 vel = Velocities[index];

            float speedSq = math.lengthsq(vel);
            if (speedSq < 1e-8f) {
                // No meaningful direction → no predictive avoidance this frame.
                ObstacleSteering[index] = float3.zero;
                return;
            }

            // Compute forward strictly from actual velocity, no world-space fallback.
            float invSpeed = math.rsqrt(speedSq);
            float3 forward = vel * invSpeed;  // normalized
            float speed = 1.0f / invSpeed;

            int behaviourIndex = BehaviourIds[index];
            if ((uint)behaviourIndex >= (uint)BehaviourMaxSpeed.Length) {
                ObstacleSteering[index] = float3.zero;
                return;
            }

            float maxSpeed = BehaviourMaxSpeed[behaviourIndex];
            float maxAcceleration = BehaviourMaxAcceleration[behaviourIndex];
            float separationRadius = BehaviourSeparationRadius[behaviourIndex];

            float baseLookAhead = math.max(separationRadius * 2.0f, 0.5f);
            float speedFactor = (maxSpeed > 1e-6f)
                ? math.saturate(speed / maxSpeed)
                : 1.0f;
            float lookAhead = baseLookAhead + separationRadius * 3.0f * speedFactor;

            float bestDanger = 0.0f;
            float3 bestDir = float3.zero;

            // --- 1) Obstacles ---
            int3 res = GridResolution;
            float3 local = (pos - GridOrigin) / math.max(CellSize, 0.0001f);
            int3 cell = (int3)math.floor(local);

            if (!(cell.x < 0 || cell.y < 0 || cell.z < 0
                || cell.x >= res.x || cell.y >= res.y || cell.z >= res.z)) {

                int layerSize = res.x * res.y;
                const int ObstacleCellRange = 2;

                for (int dz = -ObstacleCellRange; dz <= ObstacleCellRange; dz += 1) {
                    int z = cell.z + dz;
                    if (z < 0 || z >= res.z) {
                        continue;
                    }

                    for (int dy = -ObstacleCellRange; dy <= ObstacleCellRange; dy += 1) {
                        int y = cell.y + dy;
                        if (y < 0 || y >= res.y) {
                            continue;
                        }

                        for (int dx = -ObstacleCellRange; dx <= ObstacleCellRange; dx += 1) {
                            int x = cell.x + dx;
                            if (x < 0 || x >= res.x) {
                                continue;
                            }

                            int neighborCellIndex = x + y * res.x + z * layerSize;

                            NativeParallelMultiHashMapIterator<int> it;
                            int obstacleIndex;
                            if (!CellToObstacles.TryGetFirstValue(neighborCellIndex, out obstacleIndex, out it)) {
                                continue;
                            }

                            do {
                                FlockObstacleData obstacle = Obstacles[obstacleIndex];

                                float3 toObstacle = obstacle.Position - pos;
                                float distCenterSq = math.lengthsq(toObstacle);

                                float expandedRadius = obstacle.Radius + separationRadius;
                                float expandedRadiusSq = expandedRadius * expandedRadius;

                                float maxRange = lookAhead + expandedRadius;
                                float maxRangeSq = maxRange * maxRange;

                                if (distCenterSq > maxRangeSq) {
                                    continue;
                                }

                                // Inside safety sphere: push out strongly
                                if (distCenterSq < expandedRadiusSq) {
                                    float distCenter = math.sqrt(math.max(distCenterSq, 1e-6f));
                                    float penetration = expandedRadius - distCenter;
                                    if (penetration <= 0.0f) {
                                        continue;
                                    }

                                    float penetrationFactor = math.saturate(penetration / expandedRadius);
                                    float dangerInside = 0.5f + 0.5f * penetrationFactor;

                                    float3 exitDir = math.normalizesafe(
                                        pos - obstacle.Position,
                                        -forward);

                                    if (dangerInside > bestDanger) {
                                        bestDanger = dangerInside;
                                        bestDir = exitDir;
                                    }

                                    continue;
                                }

                                // Path-based intersection
                                float forwardDist = math.dot(toObstacle, forward);
                                if (forwardDist < 0.0f || forwardDist > lookAhead) {
                                    continue;
                                }

                                float3 projected = forward * forwardDist;
                                float3 lateral = toObstacle - projected;
                                float lateralSq = math.lengthsq(lateral);

                                if (lateralSq > expandedRadiusSq) {
                                    continue;
                                }

                                float lateralDist = math.sqrt(math.max(lateralSq, 1e-6f));
                                float penetrationLateral = expandedRadius - lateralDist;
                                if (penetrationLateral <= 0.0f) {
                                    continue;
                                }

                                float timeFactor = 1.0f - (forwardDist / lookAhead);
                                float penetrationFactorLateral = penetrationLateral / expandedRadius;

                                float danger = timeFactor * penetrationFactorLateral;

                                float3 lateralDir = math.normalizesafe(
                                    -lateral,
                                    -forward);

                                if (danger > bestDanger) {
                                    bestDanger = danger;
                                    bestDir = lateralDir;
                                }
                            } while (CellToObstacles.TryGetNextValue(out obstacleIndex, ref it));
                        }
                    }
                }
            }

            if (bestDanger <= 0.0f || math.lengthsq(bestDir) < 1e-8f) {
                ObstacleSteering[index] = float3.zero;
                return;
            }

            float avoidAccel = bestDanger * maxAcceleration * AvoidStrength;
            ObstacleSteering[index] = math.normalizesafe(bestDir, float3.zero) * avoidAccel;
        }
    }
}
