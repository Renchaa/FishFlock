// File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct FlockStepJob : IJobParallelFor {
        const int NeighbourCellRange = 1;

        [ReadOnly]
        public NativeArray<float3> Positions;

        // NEW: snapshot of last frame velocities used for neighbour logic
        [ReadOnly]
        public NativeArray<float3> PrevVelocities;

        // NEW: this frame's velocities (write target)
        [NativeDisableParallelForRestriction]
        public NativeArray<float3> Velocities;

        [ReadOnly] public NativeArray<int> BehaviourIds;

        [ReadOnly] public NativeArray<float> BehaviourMaxSpeed;
        [ReadOnly] public NativeArray<float> BehaviourMaxAcceleration;
        [ReadOnly] public NativeArray<float> BehaviourDesiredSpeed;
        [ReadOnly] public NativeArray<float> BehaviourNeighbourRadius;
        [ReadOnly] public NativeArray<float> BehaviourSeparationRadius;
        [ReadOnly] public NativeArray<float> BehaviourAlignmentWeight;
        [ReadOnly] public NativeArray<float> BehaviourCohesionWeight;
        [ReadOnly] public NativeArray<float> BehaviourSeparationWeight;
        [ReadOnly] public NativeArray<float> BehaviourInfluenceWeight;

        [ReadOnly] public NativeArray<float> BehaviourAvoidanceWeight;
        [ReadOnly] public NativeArray<float> BehaviourNeutralWeight;
        [ReadOnly] public NativeArray<uint> BehaviourAvoidMask;
        [ReadOnly] public NativeArray<uint> BehaviourNeutralMask;

        [ReadOnly] public NativeParallelMultiHashMap<int, int> CellToAgents;
        [ReadOnly] public float3 GridOrigin;
        [ReadOnly] public int3 GridResolution;
        [ReadOnly] public float CellSize;

        [ReadOnly] public FlockEnvironmentData EnvironmentData;
        [ReadOnly] public float DeltaTime;

        [ReadOnly] public bool UseObstacleAvoidance;
        [ReadOnly] public float ObstacleAvoidWeight;
        [ReadOnly] public NativeArray<float3> ObstacleSteering;

        [ReadOnly] public NativeArray<float> BehaviourLeadershipWeight;
        [ReadOnly] public NativeArray<uint> BehaviourGroupMask;

        [ReadOnly] public NativeArray<float> BehaviourAvoidResponse;  // NEW
        [ReadOnly] public NativeArray<float> BehaviourSplitPanicThreshold;
        [ReadOnly] public NativeArray<float> BehaviourSplitLateralWeight;
        [ReadOnly] public NativeArray<float> BehaviourSplitAccelBoost;

        // REPLACE Execute IN FlockStepJob
        // Grouping behaviour
        [ReadOnly] public NativeArray<int> BehaviourMinGroupSize;
        [ReadOnly] public NativeArray<int> BehaviourMaxGroupSize;
        [ReadOnly] public NativeArray<float> BehaviourGroupRadiusMultiplier;
        [ReadOnly] public NativeArray<float> BehaviourLonerRadiusMultiplier;
        [ReadOnly] public NativeArray<float> BehaviourLonerCohesionBoost;


        [ReadOnly] public bool UseAttraction;
        [ReadOnly] public float GlobalAttractionWeight;
        [ReadOnly] public NativeArray<float3> AttractionSteering;
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE Execute WITH THIS VERSION

        // ===== FlockStepJob.Execute (REPLACE WHOLE METHOD) =====
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE WHOLE Execute METHOD

        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE WHOLE Execute METHOD WITH THIS VERSION

        public void Execute(int index) {
            float3 position = Positions[index];
            float3 velocity = PrevVelocities[index];

            int behaviourIndex = BehaviourIds[index];
            if ((uint)behaviourIndex >= (uint)BehaviourMaxSpeed.Length) {
                Velocities[index] = velocity;
                return;
            }

            float maxSpeed = BehaviourMaxSpeed[behaviourIndex];
            float maxAcceleration = BehaviourMaxAcceleration[behaviourIndex];
            float desiredSpeed = BehaviourDesiredSpeed[behaviourIndex];
            float neighbourRadius = BehaviourNeighbourRadius[behaviourIndex];
            float separationRadius = BehaviourSeparationRadius[behaviourIndex];
            float alignmentWeight = BehaviourAlignmentWeight[behaviourIndex];
            float cohesionWeight = BehaviourCohesionWeight[behaviourIndex];
            float separationWeight = BehaviourSeparationWeight[behaviourIndex];
            float influenceWeight = BehaviourInfluenceWeight[behaviourIndex];

            uint friendlyMask = BehaviourGroupMask[behaviourIndex];
            uint avoidMask = BehaviourAvoidMask[behaviourIndex];
            uint neutralMask = BehaviourNeutralMask[behaviourIndex];

            float3 alignment = float3.zero;
            float3 cohesion = float3.zero;
            float3 separation = float3.zero;
            float3 avoidSeparation = float3.zero;   // repulse from Avoid neighbours only

            int leaderNeighbourCount = 0;
            int separationCount = 0;
            int friendlyNeighbourCount = 0;
            float alignmentWeightSum = 0.0f;
            float cohesionWeightSum = 0.0f;
            float avoidDanger = 0.0f;      // max panic from Avoid neighbours (0..1+)

            AccumulateNeighbourForces(
                agentIndex: index,
                myBehaviourIndex: behaviourIndex,
                friendlyMask: friendlyMask,
                avoidMask: avoidMask,
                neutralMask: neutralMask,
                agentPosition: position,
                neighbourRadius: neighbourRadius,
                separationRadius: separationRadius,
                alignment: ref alignment,
                cohesion: ref cohesion,
                separation: ref separation,
                leaderNeighbourCount: ref leaderNeighbourCount,
                separationCount: ref separationCount,
                alignmentWeightSum: ref alignmentWeightSum,
                cohesionWeightSum: ref cohesionWeightSum,
                avoidDanger: ref avoidDanger,
                friendlyNeighbourCount: ref friendlyNeighbourCount,
                avoidSeparation: ref avoidSeparation);

            // -------------------------------------------------------
            // NEW: group size logic (min / max, loner / overcrowded)
            // -------------------------------------------------------
            int groupSize = friendlyNeighbourCount + 1; // me + friendly neighbours

            bool hasGroupSettings =
                BehaviourMinGroupSize.IsCreated
                && BehaviourMaxGroupSize.IsCreated
                && BehaviourLonerCohesionBoost.IsCreated
                && BehaviourMinGroupSize.Length > behaviourIndex
                && BehaviourMaxGroupSize.Length > behaviourIndex
                && BehaviourLonerCohesionBoost.Length > behaviourIndex;

            int minGroupSize = 0;
            int maxGroupSize = 0;
            float lonerCohesionBoost = 0.0f;
            float groupRadiusMultiplier = 1.0f;
            float lonerRadiusMultiplier = 1.0f;

            if (hasGroupSettings) {
                minGroupSize = BehaviourMinGroupSize[behaviourIndex];
                maxGroupSize = BehaviourMaxGroupSize[behaviourIndex];
                lonerCohesionBoost = BehaviourLonerCohesionBoost[behaviourIndex];

                // radius multipliers are optional – treat missing arrays as 1.0
                if (BehaviourGroupRadiusMultiplier.IsCreated
                    && BehaviourGroupRadiusMultiplier.Length > behaviourIndex) {
                    groupRadiusMultiplier = BehaviourGroupRadiusMultiplier[behaviourIndex];
                }

                if (BehaviourLonerRadiusMultiplier.IsCreated
                    && BehaviourLonerRadiusMultiplier.Length > behaviourIndex) {
                    lonerRadiusMultiplier = BehaviourLonerRadiusMultiplier[behaviourIndex];
                }

                // sane clamps
                if (minGroupSize < 1) {
                    minGroupSize = 1;
                }

                if (maxGroupSize < minGroupSize) {
                    maxGroupSize = minGroupSize;
                }

                bool hasFriends = friendlyNeighbourCount > 0;
                bool isLoner = groupSize < minGroupSize;
                bool isOvercrowded = groupSize > maxGroupSize;

                // LONER: has some friends in view but group is smaller than we want.
                // → pull harder towards them (stronger cohesion), slightly scaled by loner multiplier.
                if (isLoner && hasFriends) {
                    float lonerFactor = math.max(0f, lonerCohesionBoost);
                    lonerFactor *= math.max(1f, lonerRadiusMultiplier); // more “desperate” search if multiplier > 1
                    cohesionWeight *= (1.0f + lonerFactor);
                }

                // OVERCROWDED: group too large.
                // → increase separation, reduce cohesion a bit so the ball can breathe.
                if (isOvercrowded && hasFriends) {
                    float crowdFactor = (groupSize - maxGroupSize) / math.max(1f, (float)maxGroupSize);
                    crowdFactor = math.saturate(crowdFactor);

                    // more sensitive to crowding if groupRadiusMultiplier > 1
                    crowdFactor *= math.max(1f, groupRadiusMultiplier);

                    separationWeight *= (1.0f + crowdFactor);
                    cohesionWeight *= (1.0f - 0.5f * crowdFactor); // up to −50% cohesion
                }
            }

            // Panic magnifies separation rule (like before)
            float separationPanicMultiplier = 1.0f + math.saturate(avoidDanger);

            float3 flockSteering = ComputeSteering(
                currentPosition: position,
                currentVelocity: velocity,
                alignment: alignment,
                cohesion: cohesion,
                separation: separation,
                neighbourCount: leaderNeighbourCount,
                separationCount: separationCount,
                alignmentWeight: alignmentWeight,
                cohesionWeight: cohesionWeight,
                separationWeight: separationWeight,
                desiredSpeed: desiredSpeed,
                alignmentWeightSum: alignmentWeightSum,
                cohesionWeightSum: cohesionWeightSum,
                separationPanicMultiplier: separationPanicMultiplier);

            flockSteering *= influenceWeight;

            float3 steering = flockSteering;

            // -------------------------------------------------------
            // Split behaviour (unchanged except using computed groupSize)
            // -------------------------------------------------------

            float localMaxAcceleration = maxAcceleration;
            float localMaxSpeed = maxSpeed;

            float splitPanicThreshold = BehaviourSplitPanicThreshold.IsCreated
                                        && BehaviourSplitPanicThreshold.Length > behaviourIndex
                ? BehaviourSplitPanicThreshold[behaviourIndex]
                : 0.0f;

            float splitLateralWeight = BehaviourSplitLateralWeight.IsCreated
                                       && BehaviourSplitLateralWeight.Length > behaviourIndex
                ? BehaviourSplitLateralWeight[behaviourIndex]
                : 0.0f;

            float splitAccelBoost = BehaviourSplitAccelBoost.IsCreated
                                    && BehaviourSplitAccelBoost.Length > behaviourIndex
                ? BehaviourSplitAccelBoost[behaviourIndex]
                : 0.0f;

            // Require at least 3 fish for proper split – or minGroupSize if that is higher.
            int minSplitGroup = 3;
            if (hasGroupSettings) {
                minSplitGroup = math.max(minSplitGroup, minGroupSize);
            }

            bool hasGroupForSplit = groupSize >= minSplitGroup;
            bool canSplit = splitPanicThreshold > 0.0f && splitLateralWeight > 0.0f && splitAccelBoost >= 0.0f;
            bool doSplit = hasGroupForSplit && canSplit && avoidDanger >= splitPanicThreshold;

            if (doSplit) {
                // Base flee direction: prefer Avoid-only separation, fallback to general separation, then velocity.
                float3 fleeSource = math.lengthsq(avoidSeparation) > 1e-6f
                    ? avoidSeparation
                    : (math.lengthsq(separation) > 1e-6f ? separation : velocity);

                float3 fleeDir = math.normalizesafe(
                    fleeSource,
                    new float3(0.0f, 0.0f, 1.0f));

                // Build lateral axis.
                float3 up = new float3(0.0f, 1.0f, 0.0f);
                float3 side = math.cross(fleeDir, up);
                if (math.lengthsq(side) < 1e-4f) {
                    up = new float3(0.0f, 0.0f, 1.0f);
                    side = math.cross(fleeDir, up);
                }
                side = math.normalizesafe(side, float3.zero);

                // Cheap hash from agent index → {0,1,2}
                uint hash = (uint)(index * 9781 + 1);
                hash ^= hash >> 11;
                hash *= 0x9E3779B1u;
                int branch = (int)(hash % 3u); // 0 = left, 1 = straight, 2 = right

                float sideSign = 0.0f;
                if (branch == 0) sideSign = -1.0f;
                else if (branch == 2) sideSign = 1.0f;

                float3 branchDir = fleeDir;
                if (sideSign != 0.0f) {
                    branchDir = math.normalizesafe(
                        fleeDir + side * sideSign * splitLateralWeight,
                        fleeDir);
                }

                float splitIntensity = math.saturate(avoidDanger); // 0..1-ish
                float3 splitForce = branchDir * separationWeight * splitIntensity;

                steering += splitForce;

                // Panic burst: accelerate and allow slightly higher top speed while splitting
                float boost = 1.0f + splitAccelBoost * splitIntensity;
                localMaxAcceleration *= boost;
                localMaxSpeed *= boost;
            }

            // Obstacles
            if (UseObstacleAvoidance && ObstacleSteering.IsCreated) {
                float3 obstacleAccel = ObstacleSteering[index];
                steering += obstacleAccel * ObstacleAvoidWeight;
            }

            // Attraction areas
            if (UseAttraction && AttractionSteering.IsCreated) {
                float3 attractionAccel = AttractionSteering[index];
                steering += attractionAccel * GlobalAttractionWeight;
            }

            // Self propulsion to maintain desiredSpeed
            float3 propulsion = ComputePropulsion(
                currentVelocity: velocity,
                desiredSpeed: desiredSpeed);

            steering += propulsion;

            steering = LimitVector(steering, localMaxAcceleration);

            velocity += steering * DeltaTime;
            velocity = LimitVector(velocity, localMaxSpeed);
            velocity = ApplyDamping(velocity, EnvironmentData.GlobalDamping, DeltaTime);
            velocity = ApplyBoundsSteering(position, velocity, EnvironmentData);

            Velocities[index] = velocity;
        }

        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE ONLY THIS METHOD
        // ===== FlockStepJob.AccumulateNeighbourForces (REPLACE WHOLE METHOD) =====
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE WHOLE METHOD

        void AccumulateNeighbourForces(
            int agentIndex,
            int myBehaviourIndex,
            uint friendlyMask,
            uint avoidMask,
            uint neutralMask,
            float3 agentPosition,
            float neighbourRadius,
            float separationRadius,
            ref float3 alignment,
            ref float3 cohesion,
            ref float3 separation,
            ref int leaderNeighbourCount,
            ref int separationCount,
            ref float alignmentWeightSum,
            ref float cohesionWeightSum,
            ref float avoidDanger,
            ref int friendlyNeighbourCount,
            ref float3 avoidSeparation) {

            float neighbourRadiusSquared = neighbourRadius * neighbourRadius;
            float separationRadiusSquared = separationRadius * separationRadius;

            float myAvoidWeight = BehaviourAvoidanceWeight[myBehaviourIndex];
            float myNeutralWeight = BehaviourNeutralWeight[myBehaviourIndex];
            float myAvoidResponse = math.max(0f, BehaviourAvoidResponse[myBehaviourIndex]); // “panic” factor

            int3 baseCell = GetCell(agentPosition);

            // leadership state
            float maxLeaderWeight = -1.0f;
            const float epsilon = 1e-4f;

            // initialise outputs
            avoidDanger = 0.0f;
            friendlyNeighbourCount = 0;
            avoidSeparation = float3.zero;

            for (int x = -NeighbourCellRange; x <= NeighbourCellRange; x += 1) {
                for (int y = -NeighbourCellRange; y <= NeighbourCellRange; y += 1) {
                    for (int z = -NeighbourCellRange; z <= NeighbourCellRange; z += 1) {
                        int3 neighbourCell = baseCell + new int3(x, y, z);

                        if (!IsCellInsideGrid(neighbourCell)) {
                            continue;
                        }

                        int cellId = GetCellId(neighbourCell);

                        NativeParallelMultiHashMap<int, int>.Enumerator enumerator =
                            CellToAgents.GetValuesForKey(cellId);

                        while (enumerator.MoveNext()) {
                            int neighbourIndex = enumerator.Current;

                            if (neighbourIndex == agentIndex) {
                                continue;
                            }

                            float3 neighbourPosition = Positions[neighbourIndex];
                            float3 offset = neighbourPosition - agentPosition;
                            float distanceSquared = math.lengthsq(offset);

                            if (distanceSquared < 1e-6f) {
                                continue;
                            }

                            // Hard collision separation (always on)
                            if (distanceSquared < separationRadiusSquared) {
                                separation -= offset / distanceSquared;
                                separationCount += 1;
                            }

                            if (distanceSquared > neighbourRadiusSquared) {
                                continue;
                            }

                            int neighbourBehaviourIndex = BehaviourIds[neighbourIndex];
                            if ((uint)neighbourBehaviourIndex >= (uint)BehaviourLeadershipWeight.Length) {
                                continue;
                            }

                            uint bit = neighbourBehaviourIndex < 32
                                ? (1u << neighbourBehaviourIndex)
                                : 0u;

                            bool isFriendly = bit != 0u && (friendlyMask & bit) != 0u;
                            bool isAvoid = bit != 0u && (avoidMask & bit) != 0u;
                            bool isNeutral = bit != 0u && (neutralMask & bit) != 0u;

                            // no declared relation (apart from hard separation above) – ignore
                            if (!isFriendly && !isAvoid && !isNeutral) {
                                continue;
                            }

                            float distance = math.sqrt(distanceSquared);
                            if (distance <= 0.0f || neighbourRadius <= 0.0f) {
                                continue;
                            }

                            float t = 1.0f - math.saturate(distance / neighbourRadius);
                            float3 dir = offset / distance; // from me towards neighbour

                            // === FRIENDLY: schooling + leadership ===
                            if (isFriendly) {
                                friendlyNeighbourCount += 1;

                                float neighbourLeaderWeight = BehaviourLeadershipWeight[neighbourBehaviourIndex];
                                float3 neighbourVelocity = PrevVelocities[neighbourIndex];

                                if (neighbourLeaderWeight > maxLeaderWeight + epsilon) {
                                    maxLeaderWeight = neighbourLeaderWeight;
                                    leaderNeighbourCount = 1;

                                    alignment = neighbourVelocity * t;
                                    cohesion = neighbourPosition * t;

                                    alignmentWeightSum = t;
                                    cohesionWeightSum = t;
                                } else if (math.abs(neighbourLeaderWeight - maxLeaderWeight) <= epsilon
                                           && maxLeaderWeight > -1.0f) {
                                    leaderNeighbourCount += 1;

                                    alignment += neighbourVelocity * t;
                                    cohesion += neighbourPosition * t;

                                    alignmentWeightSum += t;
                                    cohesionWeightSum += t;
                                }
                            }

                            // === AVOID: prey runs from predator ===
                            if (isAvoid && myAvoidResponse > 0f) {
                                float neighbourAvoidWeight = BehaviourAvoidanceWeight[neighbourBehaviourIndex];

                                if (myAvoidWeight < neighbourAvoidWeight) {
                                    float weightDelta = neighbourAvoidWeight - myAvoidWeight;
                                    float normalised = weightDelta / math.max(neighbourAvoidWeight, 1e-3f);

                                    float localIntensity = t * normalised * myAvoidResponse;

                                    float3 repulse = -dir * localIntensity; // away from predator
                                    separation += repulse;
                                    avoidSeparation += repulse;
                                    separationCount += 1;

                                    if (localIntensity > avoidDanger) {
                                        avoidDanger = localIntensity;
                                    }
                                }
                            }

                            // === NEUTRAL: soft avoid higher neutral weight (don't clip) ===
                            if (isNeutral) {
                                float neighbourNeutralWeight = BehaviourNeutralWeight[neighbourBehaviourIndex];

                                if (myNeutralWeight < neighbourNeutralWeight) {
                                    float weightDelta = neighbourNeutralWeight - myNeutralWeight;
                                    float normalised = weightDelta / math.max(neighbourNeutralWeight, 1e-3f);

                                    float3 softRepulse = -dir * (t * normalised * 0.5f);
                                    separation += softRepulse;
                                    separationCount += 1;
                                }
                            }
                        }
                    }
                }
            }
        }

        float3 ComputeSteering(
            float3 currentPosition,
            float3 currentVelocity,
            float3 alignment,
            float3 cohesion,
            float3 separation,
            int neighbourCount,
            int separationCount,
            float alignmentWeight,
            float cohesionWeight,
            float separationWeight,
            float desiredSpeed,
            float alignmentWeightSum,
            float cohesionWeightSum,
            float separationPanicMultiplier) {

            float3 steering = float3.zero;

            if (neighbourCount > 0) {
                float invAlign = alignmentWeightSum > 1e-6f
                    ? 1.0f / alignmentWeightSum
                    : 0.0f;

                float invCoh = cohesionWeightSum > 1e-6f
                    ? 1.0f / cohesionWeightSum
                    : 0.0f;

                float3 alignmentDir = alignment * invAlign;
                float3 alignmentNorm = math.normalizesafe(
                    alignmentDir,
                    currentVelocity);

                float3 desiredAlignVel = alignmentNorm * desiredSpeed;
                float3 alignmentForce = (desiredAlignVel - currentVelocity) * alignmentWeight;

                float3 cohesionCenter = cohesion * invCoh;
                float3 toCenter = cohesionCenter - currentPosition;
                float3 cohesionDir = math.normalizesafe(
                    toCenter,
                    float3.zero);

                float3 desiredCohesionVel = cohesionDir * desiredSpeed;
                float3 cohesionForce = (desiredCohesionVel - currentVelocity) * cohesionWeight;

                steering += alignmentForce;
                steering += cohesionForce;
            }

            if (separationCount > 0) {
                float invSep = 1.0f / separationCount;
                float3 separationDir = separation * invSep;
                separationDir = math.normalizesafe(
                    separationDir,
                    float3.zero);

                float3 separationForce = separationDir * separationWeight * separationPanicMultiplier;
                steering += separationForce;
            }

            return steering;
        }

        float3 ComputePropulsion(
            float3 currentVelocity,
            float desiredSpeed) {
            if (desiredSpeed <= 0.0f) {
                return float3.zero;
            }

            float currentSpeed = math.length(currentVelocity);
            float3 direction = math.normalizesafe(
                currentVelocity,
                new float3(0.0f, 0.0f, 1.0f));

            float speedError = desiredSpeed - currentSpeed;

            if (math.abs(speedError) < 1e-3f) {
                return float3.zero;
            }

            return direction * speedError;
        }

        float3 ApplyBoundsSteering(
            float3 position,
            float3 velocity,
            FlockEnvironmentData environment) {
            if (environment.BoundsType == FlockBoundsType.Box) {
                float3 min = environment.BoundsCenter - environment.BoundsExtents;
                float3 max = environment.BoundsCenter + environment.BoundsExtents;

                float3 correctedPosition = position;
                bool corrected = false;

                if (position.x < min.x) {
                    correctedPosition.x = min.x;
                    corrected = true;
                } else if (position.x > max.x) {
                    correctedPosition.x = max.x;
                    corrected = true;
                }

                if (position.y < min.y) {
                    correctedPosition.y = min.y;
                    corrected = true;
                } else if (position.y > max.y) {
                    correctedPosition.y = max.y;
                    corrected = true;
                }

                if (position.z < min.z) {
                    correctedPosition.z = min.z;
                    corrected = true;
                } else if (position.z > max.z) {
                    correctedPosition.z = max.z;
                    corrected = true;
                }

                if (corrected) {
                    float3 direction = math.normalize(correctedPosition - position);
                    velocity += direction;
                }
            } else if (environment.BoundsType == FlockBoundsType.Sphere) {
                float3 offset = position - environment.BoundsCenter;
                float distance = math.length(offset);

                if (distance > environment.BoundsRadius) {
                    float3 direction = -offset / math.max(distance, 0.0001f);
                    velocity += direction;
                }
            }

            return velocity;
        }

        int3 GetCell(float3 position) {
            float cellSize = math.max(CellSize, 0.0001f);
            float3 local = position - GridOrigin;
            float3 scaled = local / cellSize;

            int3 cell = (int3)math.floor(scaled);

            cell = math.clamp(
                cell,
                new int3(0, 0, 0),
                GridResolution - new int3(1, 1, 1));

            return cell;
        }

        bool IsCellInsideGrid(int3 cell) {
            if (cell.x < 0 || cell.y < 0 || cell.z < 0) {
                return false;
            }

            if (cell.x >= GridResolution.x
                || cell.y >= GridResolution.y
                || cell.z >= GridResolution.z) {
                return false;
            }

            return true;
        }

        int GetCellId(int3 cell) {
            int cellId = cell.x
                         + cell.y * GridResolution.x
                         + cell.z * GridResolution.x * GridResolution.y;

            return cellId;
        }

        static float3 LimitVector(float3 value, float maxLength) {
            float lengthSquared = math.lengthsq(value);

            if (lengthSquared == 0.0f) {
                return value;
            }

            float maxLengthSquared = maxLength * maxLength;

            if (lengthSquared <= maxLengthSquared) {
                return value;
            }

            float length = math.sqrt(lengthSquared);
            float scale = maxLength / math.max(length, 0.0001f);

            return value * scale;
        }

        static float3 ApplyDamping(
            float3 velocity,
            float damping,
            float deltaTime) {
            if (damping <= 0.0f) {
                return velocity;
            }

            float factor = math.saturate(1.0f - damping * deltaTime);
            return velocity * factor;
        }
    }
}
