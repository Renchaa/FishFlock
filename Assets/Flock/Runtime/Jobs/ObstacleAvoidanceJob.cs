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
                ObstacleSteering[index] = float3.zero;
                return;
            }

            float invSpeed = math.rsqrt(speedSq);
            float3 forward = vel * invSpeed; // normalized
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
            float speedFactor = (maxSpeed > 1e-6f) ? math.saturate(speed / maxSpeed) : 1.0f;
            float lookAhead = baseLookAhead + separationRadius * 3.0f * speedFactor;

            float3 bestDir = float3.zero;
            float bestDanger = 0.0f;

            float cellSize = math.max(CellSize, 0.0001f);
            int3 res = GridResolution;

            int3 cell = GetCell(pos, GridOrigin, res, cellSize);
            int cellId = GetCellId(cell, res);

            // Search range depends only on how far ahead we care this frame (no global obstacle radius).
            int cellRange = (int)math.ceil(lookAhead / cellSize) + 1;
            cellRange = math.clamp(cellRange, 1, math.cmax(res));

            int layerSize = res.x * res.y;

            for (int dz = -cellRange; dz <= cellRange; dz += 1) {
                int z = cell.z + dz;
                if ((uint)z >= (uint)res.z) {
                    continue;
                }

                for (int dy = -cellRange; dy <= cellRange; dy += 1) {
                    int y = cell.y + dy;
                    if ((uint)y >= (uint)res.y) {
                        continue;
                    }

                    int rowBase = y * res.x + z * layerSize;

                    for (int dx = -cellRange; dx <= cellRange; dx += 1) {
                        int x = cell.x + dx;
                        if ((uint)x >= (uint)res.x) {
                            continue;
                        }

                        int neighbourCellId = x + rowBase;

                        NativeParallelMultiHashMapIterator<int> it;
                        int obstacleIndex;

                        if (!CellToObstacles.TryGetFirstValue(neighbourCellId, out obstacleIndex, out it)) {
                            continue;
                        }

                        do {
                            FlockObstacleData obstacle = Obstacles[obstacleIndex];

                            float broadR = math.max(0.0f, obstacle.Radius) + separationRadius;
                            float maxRange = lookAhead + broadR;

                            float3 toObstacle = obstacle.Position - pos;
                            float distCenterSq = math.lengthsq(toObstacle);

                            if (distCenterSq > (maxRange * maxRange)) {
                                continue;
                            }

                            if (obstacle.Shape == FlockObstacleShape.Sphere) {
                                EvaluateSphere(
                                    pos,
                                    forward,
                                    lookAhead,
                                    separationRadius,
                                    obstacle.Position,
                                    math.max(0.0f, obstacle.Radius),
                                    ref bestDanger,
                                    ref bestDir);
                            } else {
                                EvaluateBox(
                                    pos,
                                    forward,
                                    lookAhead,
                                    separationRadius,
                                    obstacle.Position,
                                    obstacle.BoxHalfExtents,
                                    obstacle.BoxRotation,
                                    ref bestDanger,
                                    ref bestDir);
                            }

                        } while (CellToObstacles.TryGetNextValue(out obstacleIndex, ref it));
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

        static void EvaluateSphere(
            float3 pos,
            float3 forward,
            float lookAhead,
            float separationRadius,
            float3 sphereCenter,
            float sphereRadius,
            ref float bestDanger,
            ref float3 bestDir) {

            float expandedRadius = sphereRadius + separationRadius;
            float expandedRadiusSq = expandedRadius * expandedRadius;

            float3 toCenter = sphereCenter - pos;
            float distCenterSq = math.lengthsq(toCenter);

            // Inside safety sphere: push out strongly
            if (distCenterSq < expandedRadiusSq) {
                float distCenter = math.sqrt(math.max(distCenterSq, 1e-6f));
                float penetration = expandedRadius - distCenter;
                if (penetration > 0.0f) {
                    float penetrationFactor = math.saturate(penetration / math.max(expandedRadius, 1e-3f));
                    float dangerInside = 0.5f + 0.5f * penetrationFactor;

                    float3 exitDir = math.normalizesafe(
                        pos - sphereCenter,
                        -forward);

                    if (dangerInside > bestDanger) {
                        bestDanger = dangerInside;
                        bestDir = exitDir;
                    }
                }

                return;
            }

            // Path-based intersection against expanded sphere
            float forwardDist = math.dot(toCenter, forward);
            if (forwardDist < 0.0f || forwardDist > lookAhead) {
                return;
            }

            float3 projected = forward * forwardDist;
            float3 lateral = toCenter - projected;
            float lateralSq = math.lengthsq(lateral);

            if (lateralSq > expandedRadiusSq) {
                return;
            }

            float lateralDist = math.sqrt(math.max(lateralSq, 1e-6f));
            float penetrationLateral = expandedRadius - lateralDist;
            if (penetrationLateral <= 0.0f) {
                return;
            }

            float timeFactor = 1.0f - (forwardDist / math.max(lookAhead, 1e-3f));
            float penetrationFactorLateral = penetrationLateral / math.max(expandedRadius, 1e-3f);

            float danger = timeFactor * penetrationFactorLateral;

            float3 lateralDir = math.normalizesafe(
                -lateral,
                -forward);

            if (danger > bestDanger) {
                bestDanger = danger;
                bestDir = lateralDir;
            }
        }

        static void EvaluateBox(
            float3 pos,
            float3 forward,
            float lookAhead,
            float separationRadius,
            float3 boxCenter,
            float3 boxHalfExtents,
            quaternion boxRot,
            ref float bestDanger,
            ref float3 bestDir) {

            float3 he = new float3(
                math.max(0.0f, boxHalfExtents.x) + separationRadius,
                math.max(0.0f, boxHalfExtents.y) + separationRadius,
                math.max(0.0f, boxHalfExtents.z) + separationRadius);

            // Degenerate box → ignore
            if (he.x <= 0f || he.y <= 0f || he.z <= 0f) {
                return;
            }

            quaternion invRot = math.inverse(boxRot);

            float3 o = math.mul(invRot, pos - boxCenter);   // ray origin in box local
            float3 d = math.mul(invRot, forward);           // ray dir in box local (normalized)

            float tHit;
            float3 nLocal;
            bool startsInside;

            if (!RayAabbSegmentHit(o, d, he, lookAhead, out tHit, out nLocal, out startsInside)) {
                return;
            }

            float danger;
            if (startsInside) {
                // Inside: stronger response, scaled by how deep we are (approx).
                float3 absO = math.abs(o);
                float dx = he.x - absO.x;
                float dy = he.y - absO.y;
                float dz = he.z - absO.z;

                float minFaceDist = math.min(dx, math.min(dy, dz));
                float minHe = math.max(math.cmin(he), 1e-3f);

                float penetrationFactor = 1.0f - math.saturate(minFaceDist / minHe);
                danger = 0.5f + 0.5f * penetrationFactor;
            } else {
                float timeFactor = 1.0f - (tHit / math.max(lookAhead, 1e-3f));
                danger = math.saturate(timeFactor);
            }

            if (danger <= bestDanger) {
                return;
            }

            float3 nWorld = math.mul(boxRot, nLocal);
            if (math.lengthsq(nWorld) < 1e-8f) {
                return;
            }

            bestDanger = danger;
            bestDir = nWorld;
        }

        static bool RayAabbSegmentHit(
            float3 o,
            float3 d,
            float3 he,
            float maxT,
            out float tHit,
            out float3 normalLocal,
            out bool startsInside) {

            tHit = 0f;
            normalLocal = float3.zero;

            startsInside =
                math.abs(o.x) <= he.x &&
                math.abs(o.y) <= he.y &&
                math.abs(o.z) <= he.z;

            if (startsInside) {
                // Choose outward normal of nearest face; if ambiguous, bias toward travel direction.
                float3 absO = math.abs(o);
                float dx = he.x - absO.x;
                float dy = he.y - absO.y;
                float dz = he.z - absO.z;

                int axis = 0;
                float minD = dx;

                if (dy < minD) { minD = dy; axis = 1; }
                if (dz < minD) { minD = dz; axis = 2; }

                // If we're near the center (ties), bias by direction.
                float3 absD = math.abs(d);
                if (minD > math.cmin(he) * 0.999f) {
                    axis = absD.x >= absD.y
                        ? (absD.x >= absD.z ? 0 : 2)
                        : (absD.y >= absD.z ? 1 : 2);
                }

                if (axis == 0) {
                    float s = (o.x != 0f) ? math.sign(o.x) : (d.x >= 0f ? 1f : -1f);
                    normalLocal = new float3(s, 0f, 0f);
                } else if (axis == 1) {
                    float s = (o.y != 0f) ? math.sign(o.y) : (d.y >= 0f ? 1f : -1f);
                    normalLocal = new float3(0f, s, 0f);
                } else {
                    float s = (o.z != 0f) ? math.sign(o.z) : (d.z >= 0f ? 1f : -1f);
                    normalLocal = new float3(0f, 0f, s);
                }

                tHit = 0f;
                return true;
            }

            // Slab intersection against segment [0, maxT]
            float tmin = 0f;
            float tmax = maxT;

            float3 n = float3.zero;
            const float eps = 1e-8f;

            // X
            if (math.abs(d.x) < eps) {
                if (o.x < -he.x || o.x > he.x) return false;
            } else {
                float inv = 1.0f / d.x;
                float t1 = (-he.x - o.x) * inv;
                float t2 = (he.x - o.x) * inv;

                float tNear = math.min(t1, t2);
                float tFar = math.max(t1, t2);
                float3 nNear = (t1 < t2) ? new float3(-1f, 0f, 0f) : new float3(1f, 0f, 0f);

                if (tNear > tmin) { tmin = tNear; n = nNear; }
                tmax = math.min(tmax, tFar);
                if (tmin > tmax) return false;
            }

            // Y
            if (math.abs(d.y) < eps) {
                if (o.y < -he.y || o.y > he.y) return false;
            } else {
                float inv = 1.0f / d.y;
                float t1 = (-he.y - o.y) * inv;
                float t2 = (he.y - o.y) * inv;

                float tNear = math.min(t1, t2);
                float tFar = math.max(t1, t2);
                float3 nNear = (t1 < t2) ? new float3(0f, -1f, 0f) : new float3(0f, 1f, 0f);

                if (tNear > tmin) { tmin = tNear; n = nNear; }
                tmax = math.min(tmax, tFar);
                if (tmin > tmax) return false;
            }

            // Z
            if (math.abs(d.z) < eps) {
                if (o.z < -he.z || o.z > he.z) return false;
            } else {
                float inv = 1.0f / d.z;
                float t1 = (-he.z - o.z) * inv;
                float t2 = (he.z - o.z) * inv;

                float tNear = math.min(t1, t2);
                float tFar = math.max(t1, t2);
                float3 nNear = (t1 < t2) ? new float3(0f, 0f, -1f) : new float3(0f, 0f, 1f);

                if (tNear > tmin) { tmin = tNear; n = nNear; }
                tmax = math.min(tmax, tFar);
                if (tmin > tmax) return false;
            }

            if (tmax < 0f) return false;
            if (tmin > maxT) return false;

            tHit = math.max(tmin, 0f);
            normalLocal = n;

            return math.lengthsq(normalLocal) > 1e-6f;
        }

        static int3 GetCell(float3 position, float3 origin, int3 res, float cellSize) {
            float3 scaled = (position - origin) / cellSize;
            int3 cell = (int3)math.floor(scaled);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                res - new int3(1, 1, 1));
        }

        static int GetCellId(int3 cell, int3 res) {
            return cell.x + cell.y * res.x + cell.z * res.x * res.y;
        }
    }
}
