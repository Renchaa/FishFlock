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
                                                                      // REPLACE Execute IN FlockStepJob
        [ReadOnly] public bool UseAttraction;
        [ReadOnly] public float GlobalAttractionWeight;
        [ReadOnly] public NativeArray<float3> AttractionSteering;
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE Execute WITH THIS VERSION

        // ===== FlockStepJob.Execute (REPLACE WHOLE METHOD) =====
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

            int leaderNeighbourCount = 0;
            int separationCount = 0;
            float alignmentWeightSum = 0.0f;
            float cohesionWeightSum = 0.0f;

            // NEW: how “panicked” we are this frame because of Avoid neighbours
            float panicStrength = 0.0f;

            AccumulateNeighbourForces(
                index,
                behaviourIndex,
                friendlyMask,
                avoidMask,
                neutralMask,
                position,
                neighbourRadius,
                separationRadius,
                ref alignment,
                ref cohesion,
                ref separation,
                ref leaderNeighbourCount,
                ref separationCount,
                ref alignmentWeightSum,
                ref cohesionWeightSum,
                ref panicStrength);

            // clamp panic into [0,1] then turn it into 1..2 multiplier
            float separationPanicMultiplier = 1.0f + math.saturate(panicStrength);

            float3 flockSteering = ComputeSteering(
                position,
                velocity,
                alignment,
                cohesion,
                separation,
                leaderNeighbourCount,
                separationCount,
                alignmentWeight,
                cohesionWeight,
                separationWeight,
                desiredSpeed,
                alignmentWeightSum,
                cohesionWeightSum,
                separationPanicMultiplier);

            flockSteering *= influenceWeight;

            float3 steering = flockSteering;

            if (UseObstacleAvoidance && ObstacleSteering.IsCreated) {
                float3 obstacleAccel = ObstacleSteering[index];
                steering += obstacleAccel * ObstacleAvoidWeight;
            }

            if (UseAttraction && AttractionSteering.IsCreated) {
                float3 attract = AttractionSteering[index];
                steering += attract * GlobalAttractionWeight;
            }

            float3 propulsion = ComputePropulsion(
                velocity,
                desiredSpeed);

            steering += propulsion;

            steering = LimitVector(steering, maxAcceleration);

            velocity += steering * DeltaTime;
            velocity = LimitVector(velocity, maxSpeed);
            velocity = ApplyDamping(velocity, EnvironmentData.GlobalDamping, DeltaTime);
            velocity = ApplyBoundsSteering(position, velocity, EnvironmentData);

            Velocities[index] = velocity;
        }


        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE ONLY THIS METHOD
        // ===== FlockStepJob.AccumulateNeighbourForces (REPLACE WHOLE METHOD) =====
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
            ref float avoidDanger) {

            float neighbourRadiusSquared = neighbourRadius * neighbourRadius;
            float separationRadiusSquared = separationRadius * separationRadius;

            float myAvoidWeight = BehaviourAvoidanceWeight[myBehaviourIndex];
            float myNeutralWeight = BehaviourNeutralWeight[myBehaviourIndex];
            float myAvoidResponse = math.max(0f, BehaviourAvoidResponse[myBehaviourIndex]); // “panic” factor

            int3 baseCell = GetCell(agentPosition);

            // leadership state
            float maxLeaderWeight = -1.0f;
            const float epsilon = 1e-4f;

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

                            // hard collision separation (always on)
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

                            // === FRIENDLY: standard schooling + leadership ===
                            if (isFriendly) {
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

                            // === AVOID: prey (lower avoidance weight) runs from predator (higher avoidance weight) ===
                            if (isAvoid && myAvoidResponse > 0f) {
                                float neighbourAvoidWeight = BehaviourAvoidanceWeight[neighbourBehaviourIndex];

                                if (myAvoidWeight < neighbourAvoidWeight) {
                                    float weightDelta = neighbourAvoidWeight - myAvoidWeight;
                                    float normalised = weightDelta / math.max(neighbourAvoidWeight, 1e-3f);

                                    // local “danger” from this neighbour
                                    float localIntensity = t * normalised * myAvoidResponse;

                                    // push away from neighbour
                                    float3 repulse = -dir * localIntensity;
                                    separation += repulse;
                                    separationCount += 1;

                                    // track max danger so Execute can boost accel/speed
                                    if (localIntensity > avoidDanger) {
                                        avoidDanger = localIntensity;
                                    }
                                }
                            }

                            // === NEUTRAL: lower neutral weight gently avoids higher ones (don't clip through) ===
                            if (isNeutral) {
                                float neighbourNeutralWeight = BehaviourNeutralWeight[neighbourBehaviourIndex];

                                if (myNeutralWeight < neighbourNeutralWeight) {
                                    float weightDelta = neighbourNeutralWeight - myNeutralWeight;
                                    float normalised = weightDelta / math.max(neighbourNeutralWeight, 1e-3f);

                                    // softer than explicit "Avoid"
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

                // NEW: panic multiplier – when Avoid triggers, this rises towards 2x
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
