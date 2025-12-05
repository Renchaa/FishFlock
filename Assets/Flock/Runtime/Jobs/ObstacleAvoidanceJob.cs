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

        // NEW: environment data so we can treat bounds as "implicit obstacles"
        [ReadOnly] public FlockEnvironmentData EnvironmentData;

        [WriteOnly] public NativeArray<float3> ObstacleSteering;

        public void Execute(int index) {
            float3 pos = Positions[index];
            float3 vel = Velocities[index];

            float speedSq = math.lengthsq(vel);
            if (speedSq < 1e-6f) {
                ObstacleSteering[index] = float3.zero;
                return;
            }

            int behaviourIndex = BehaviourIds[index];
            if ((uint)behaviourIndex >= (uint)BehaviourMaxSpeed.Length) {
                ObstacleSteering[index] = float3.zero;
                return;
            }

            float maxSpeed = BehaviourMaxSpeed[behaviourIndex];
            float maxAcceleration = BehaviourMaxAcceleration[behaviourIndex];
            float separationRadius = BehaviourSeparationRadius[behaviourIndex];

            float3 forward = math.normalizesafe(vel, new float3(0.0f, 0.0f, 1.0f));

            float baseLookAhead = math.max(separationRadius * 2.0f, 0.5f);
            float speed = math.sqrt(speedSq);
            float speedFactor = (maxSpeed > 1e-6f)
                ? math.saturate(speed / maxSpeed)
                : 1.0f;
            float lookAhead = baseLookAhead + separationRadius * 3.0f * speedFactor;

            float bestDanger = 0.0f;
            float3 bestDir = float3.zero;

            // --- 1) Obstacles (as before, just writing into bestDanger/bestDir) ---
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

                                // Path-based intersection (same as before)
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

            // --- 2) Bounds as implicit obstacles (predictive wall avoidance) ---
            if (EnvironmentData.BoundsType == FlockBoundsType.Box) {
                float wallDanger;
                float3 wallDir;
                ComputeBoundsAvoidance(
                    pos,
                    forward,
                    separationRadius,
                    lookAhead,
                    out wallDanger,
                    out wallDir);

                if (wallDanger > bestDanger) {
                    bestDanger = wallDanger;
                    bestDir = wallDir;
                }
            }

            if (bestDanger <= 0.0f) {
                ObstacleSteering[index] = float3.zero;
                return;
            }

            float avoidAccel = bestDanger * maxAcceleration * AvoidStrength;
            ObstacleSteering[index] = bestDir * avoidAccel;
        }

        void ComputeBoundsAvoidance(
            float3 pos,
            float3 forward,
            float separationRadius,
            float lookAhead,
            out float danger,
            out float3 dir) {

            danger = 0.0f;
            dir = float3.zero;

            if (lookAhead <= 0.0f || math.lengthsq(forward) < 1e-6f) {
                return;
            }

            if (EnvironmentData.BoundsType != FlockBoundsType.Box) {
                // For now we only implement predictive avoidance for box bounds.
                return;
            }

            float3 center = EnvironmentData.BoundsCenter;
            float3 extents = EnvironmentData.BoundsExtents;

            if (extents.x <= 0.0f && extents.y <= 0.0f && extents.z <= 0.0f) {
                return;
            }

            float3 min = center - extents;
            float3 max = center + extents;

            float baseMargin = math.max(separationRadius, 0.01f);

            float3 margin;
            margin.x = math.min(extents.x * 0.9f, baseMargin);
            margin.y = math.min(extents.y * 0.9f, baseMargin);
            margin.z = math.min(extents.z * 0.9f, baseMargin);

            EvaluateWallXMin(pos, forward, lookAhead, min.x + margin.x, margin.x, ref danger, ref dir);
            EvaluateWallXMax(pos, forward, lookAhead, max.x - margin.x, margin.x, ref danger, ref dir);
            EvaluateWallYMin(pos, forward, lookAhead, min.y + margin.y, margin.y, ref danger, ref dir);
            EvaluateWallYMax(pos, forward, lookAhead, max.y - margin.y, margin.y, ref danger, ref dir);
            EvaluateWallZMin(pos, forward, lookAhead, min.z + margin.z, margin.z, ref danger, ref dir);
            EvaluateWallZMax(pos, forward, lookAhead, max.z - margin.z, margin.z, ref danger, ref dir);
        }

        void EvaluateWallXMin(
            float3 pos,
            float3 forward,
            float lookAhead,
            float planeX,
            float margin,
            ref float bestDanger,
            ref float3 bestDir) {

            // Interior is x >= planeX, normal points +X
            float distInterior = pos.x - planeX;

            // Already too close / outside: push back strongly
            if (distInterior < 0.0f) {
                float penetration = -distInterior;
                float penetrationFactor = math.saturate(penetration / math.max(margin, 0.01f));
                float localDanger = 0.5f + 0.5f * penetrationFactor;
                float3 localDir = new float3(1.0f, 0.0f, 0.0f);

                if (localDanger > bestDanger) {
                    bestDanger = localDanger;
                    bestDir = localDir;
                }
            } else if (forward.x < -1e-4f) {
                // Predictive: will we hit the inner plane within lookAhead?
                float t = (planeX - pos.x) / forward.x; // forward.x < 0
                if (t >= 0.0f && t <= lookAhead) {
                    float timeFactor = 1.0f - (t / lookAhead);
                    float angleFactor = math.saturate(-forward.x);
                    float localDanger = timeFactor * angleFactor;

                    float3 normal = new float3(1.0f, 0.0f, 0.0f);
                    float3 bounceDir = math.reflect(forward, normal);
                    bounceDir = math.normalizesafe(bounceDir, normal);

                    if (localDanger > bestDanger) {
                        bestDanger = localDanger;
                        bestDir = bounceDir;
                    }
                }
            }
        }

        void EvaluateWallXMax(
            float3 pos,
            float3 forward,
            float lookAhead,
            float planeX,
            float margin,
            ref float bestDanger,
            ref float3 bestDir) {

            // Interior is x <= planeX, normal points -X
            float distInterior = planeX - pos.x;

            if (distInterior < 0.0f) {
                float penetration = -distInterior;
                float penetrationFactor = math.saturate(penetration / math.max(margin, 0.01f));
                float localDanger = 0.5f + 0.5f * penetrationFactor;
                float3 localDir = new float3(-1.0f, 0.0f, 0.0f);

                if (localDanger > bestDanger) {
                    bestDanger = localDanger;
                    bestDir = localDir;
                }
            } else if (forward.x > 1e-4f) {
                float t = (planeX - pos.x) / forward.x; // forward.x > 0
                if (t >= 0.0f && t <= lookAhead) {
                    float timeFactor = 1.0f - (t / lookAhead);
                    float angleFactor = math.saturate(forward.x);
                    float localDanger = timeFactor * angleFactor;

                    float3 normal = new float3(-1.0f, 0.0f, 0.0f);
                    float3 bounceDir = math.reflect(forward, normal);
                    bounceDir = math.normalizesafe(bounceDir, normal);

                    if (localDanger > bestDanger) {
                        bestDanger = localDanger;
                        bestDir = bounceDir;
                    }
                }
            }
        }

        void EvaluateWallYMin(
            float3 pos,
            float3 forward,
            float lookAhead,
            float planeY,
            float margin,
            ref float bestDanger,
            ref float3 bestDir) {

            // Interior is y >= planeY, normal points +Y
            float distInterior = pos.y - planeY;

            if (distInterior < 0.0f) {
                float penetration = -distInterior;
                float penetrationFactor = math.saturate(penetration / math.max(margin, 0.01f));
                float localDanger = 0.5f + 0.5f * penetrationFactor;
                float3 localDir = new float3(0.0f, 1.0f, 0.0f);

                if (localDanger > bestDanger) {
                    bestDanger = localDanger;
                    bestDir = localDir;
                }
            } else if (forward.y < -1e-4f) {
                float t = (planeY - pos.y) / forward.y; // forward.y < 0
                if (t >= 0.0f && t <= lookAhead) {
                    float timeFactor = 1.0f - (t / lookAhead);
                    float angleFactor = math.saturate(-forward.y);
                    float localDanger = timeFactor * angleFactor;

                    float3 normal = new float3(0.0f, 1.0f, 0.0f);
                    float3 bounceDir = math.reflect(forward, normal);
                    bounceDir = math.normalizesafe(bounceDir, normal);

                    if (localDanger > bestDanger) {
                        bestDanger = localDanger;
                        bestDir = bounceDir;
                    }
                }
            }
        }

        void EvaluateWallYMax(
            float3 pos,
            float3 forward,
            float lookAhead,
            float planeY,
            float margin,
            ref float bestDanger,
            ref float3 bestDir) {

            // Interior is y <= planeY, normal points -Y
            float distInterior = planeY - pos.y;

            if (distInterior < 0.0f) {
                float penetration = -distInterior;
                float penetrationFactor = math.saturate(penetration / math.max(margin, 0.01f));
                float localDanger = 0.5f + 0.5f * penetrationFactor;
                float3 localDir = new float3(0.0f, -1.0f, 0.0f);

                if (localDanger > bestDanger) {
                    bestDanger = localDanger;
                    bestDir = localDir;
                }
            } else if (forward.y > 1e-4f) {
                float t = (planeY - pos.y) / forward.y; // forward.y > 0
                if (t >= 0.0f && t <= lookAhead) {
                    float timeFactor = 1.0f - (t / lookAhead);
                    float angleFactor = math.saturate(forward.y);
                    float localDanger = timeFactor * angleFactor;

                    float3 normal = new float3(0.0f, -1.0f, 0.0f);
                    float3 bounceDir = math.reflect(forward, normal);
                    bounceDir = math.normalizesafe(bounceDir, normal);

                    if (localDanger > bestDanger) {
                        bestDanger = localDanger;
                        bestDir = bounceDir;
                    }
                }
            }
        }

        void EvaluateWallZMin(
            float3 pos,
            float3 forward,
            float lookAhead,
            float planeZ,
            float margin,
            ref float bestDanger,
            ref float3 bestDir) {

            // Interior is z >= planeZ, normal points +Z
            float distInterior = pos.z - planeZ;

            if (distInterior < 0.0f) {
                float penetration = -distInterior;
                float penetrationFactor = math.saturate(penetration / math.max(margin, 0.01f));
                float localDanger = 0.5f + 0.5f * penetrationFactor;
                float3 localDir = new float3(0.0f, 0.0f, 1.0f);

           

                if (localDanger > bestDanger) {
                    bestDanger = localDanger;
                    bestDir = localDir;
                }
            } else if (forward.z < -1e-4f) {
                float t = (planeZ - pos.z) / forward.z; // forward.z < 0
                if (t >= 0.0f && t <= lookAhead) {
                    float timeFactor = 1.0f - (t / lookAhead);
                    float angleFactor = math.saturate(-forward.z);
                    float localDanger = timeFactor * angleFactor;

                    float3 normal = new float3(0.0f, 0.0f, 1.0f);
                    float3 bounceDir = math.reflect(forward, normal);
                    bounceDir = math.normalizesafe(bounceDir, normal);

                    if (localDanger > bestDanger) {
                        bestDanger = localDanger;
                        bestDir = bounceDir;
                    }
                }
            }
        }

        void EvaluateWallZMax(
            float3 pos,
            float3 forward,
            float lookAhead,
            float planeZ,
            float margin,
            ref float bestDanger,
            ref float3 bestDir) {

            // Interior is z <= planeZ, normal points -Z
            float distInterior = planeZ - pos.z;

            if (distInterior < 0.0f) {
                float penetration = -distInterior;
                float penetrationFactor = math.saturate(penetration / math.max(margin, 0.01f));
                float localDanger = 0.5f + 0.5f * penetrationFactor;
                float3 localDir = new float3(0.0f, 0.0f, -1.0f);

                if (localDanger > bestDanger) {
                    bestDanger = localDanger;
                    bestDir = localDir;
                }
            } else if (forward.z > 1e-4f) {
                float t = (planeZ - pos.z) / forward.z; // forward.z > 0
                if (t >= 0.0f && t <= lookAhead) {
                    float timeFactor = 1.0f - (t / lookAhead);
                    float angleFactor = math.saturate(forward.z);
                    float localDanger = timeFactor * angleFactor;

                    float3 normal = new float3(0.0f, 0.0f, -1.0f);
                    float3 bounceDir = math.reflect(forward, normal);
                    bounceDir = math.normalizesafe(bounceDir, normal);

                    if (localDanger > bestDanger) {
                        bestDanger = localDanger;
                        bestDir = bounceDir;
                    }
                }
            }
        }
    }
}
