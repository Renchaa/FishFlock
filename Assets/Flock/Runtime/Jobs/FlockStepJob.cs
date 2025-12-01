// File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using System;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct FlockStepJob : IJobParallelFor {
        const int NeighbourCellRange = 2;

        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> PrevVelocities;
        [NativeDisableParallelForRestriction] public NativeArray<float3> Velocities;

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

        [ReadOnly] public NativeArray<float> BehaviourAvoidResponse;
        [ReadOnly] public NativeArray<float> BehaviourSplitPanicThreshold;
        [ReadOnly] public NativeArray<float> BehaviourSplitLateralWeight;
        [ReadOnly] public NativeArray<float> BehaviourSplitAccelBoost;

        [ReadOnly] public NativeArray<int> BehaviourMinGroupSize;
        [ReadOnly] public NativeArray<int> BehaviourMaxGroupSize;
        [ReadOnly] public NativeArray<float> BehaviourGroupRadiusMultiplier;
        [ReadOnly] public NativeArray<float> BehaviourLonerRadiusMultiplier;
        [ReadOnly] public NativeArray<float> BehaviourLonerCohesionBoost;

        [ReadOnly] public NativeArray<byte> BehaviourUsePreferredDepth;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMin;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMax;
        [ReadOnly] public NativeArray<float> BehaviourDepthBiasStrength;
        [ReadOnly] public NativeArray<byte> BehaviourDepthWinsOverAttractor;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMinNorm;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMaxNorm;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthWeight;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthEdgeFraction;

        [ReadOnly] public bool UseAttraction;
        [ReadOnly] public float GlobalAttractionWeight;
        [ReadOnly] public NativeArray<float3> AttractionSteering;

        // === NEW: radius + zone arrays ===
        [ReadOnly] public NativeArray<float> BehaviourBaseRadius;
        [ReadOnly] public NativeArray<float> BehaviourDeadBandFraction;
        [ReadOnly] public NativeArray<float> BehaviourFriendlyInnerFraction;
        [ReadOnly] public NativeArray<float> BehaviourFriendDistanceFactor;
        [ReadOnly] public NativeArray<float> BehaviourAvoidDistanceFactor;
        [ReadOnly] public NativeArray<float> BehaviourNeutralDistanceFactor;
        [ReadOnly] public NativeArray<float> BehaviourInfluenceDistanceFactor;
        [ReadOnly] public NativeArray<float> BehaviourHardRepulsionGain;
        [ReadOnly] public NativeArray<float> BehaviourFriendlySoftGain;
        [ReadOnly] public NativeArray<float> BehaviourAvoidRadialGain;
        [ReadOnly] public NativeArray<float> BehaviourNeutralRadialGain;

        [ReadOnly] public NativeArray<float> BehaviourMaxTurnRateDeg;     // deg / second, 0 = unlimited
        [ReadOnly] public NativeArray<float> BehaviourTurnResponsiveness; // 0..1, how strongly we follow desired dir
        // ========================= CHANGED EXECUTE =========================
        public void Execute(int index) {
            // --- 1) Load per-agent + per-behaviour scalars ---
            if (!TryLoadBehaviourScalars(
                    index,
                    out float3 position,
                    out float3 velocity,
                    out int behaviourIndex,
                    out float maxSpeed,
                    out float maxAcceleration,
                    out float desiredSpeed,
                    out float neighbourRadius,
                    out float separationRadius,
                    out float alignmentWeight,
                    out float cohesionWeight,
                    out float separationWeight,
                    out float influenceWeight,
                    out uint friendlyMask,
                    out uint avoidMask,
                    out uint neutralMask)) {

                // Behaviour index out of range – keep previous velocity.
                Velocities[index] = PrevVelocities[index];
                return;
            }

            float maxTurnRateDeg = 0f;      // 0 = unlimited
            float turnResponsiveness = 1f;  // 1 = full allowed turn, 0 = almost frozen

            if (BehaviourMaxTurnRateDeg.IsCreated
                && (uint)behaviourIndex < (uint)BehaviourMaxTurnRateDeg.Length) {
                maxTurnRateDeg = BehaviourMaxTurnRateDeg[behaviourIndex];
            }

            if (BehaviourTurnResponsiveness.IsCreated
                && (uint)behaviourIndex < (uint)BehaviourTurnResponsiveness.Length) {
                turnResponsiveness = BehaviourTurnResponsiveness[behaviourIndex];
            }

            // --- 2) Neighbours: alignment / cohesion / separation / danger ---
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

            // --- 3) Group size logic (loners / overcrowding) ---
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

                if (BehaviourGroupRadiusMultiplier.IsCreated
                    && BehaviourGroupRadiusMultiplier.Length > behaviourIndex) {
                    groupRadiusMultiplier = BehaviourGroupRadiusMultiplier[behaviourIndex];
                }

                if (BehaviourLonerRadiusMultiplier.IsCreated
                    && BehaviourLonerRadiusMultiplier.Length > behaviourIndex) {
                    lonerRadiusMultiplier = BehaviourLonerRadiusMultiplier[behaviourIndex];
                }

                if (minGroupSize < 1) {
                    minGroupSize = 1;
                }

                if (maxGroupSize < minGroupSize) {
                    maxGroupSize = minGroupSize;
                }

                bool hasFriends = friendlyNeighbourCount > 0;
                bool isLoner = groupSize < minGroupSize;
                bool isOvercrowded = groupSize > maxGroupSize;

                if (isLoner && hasFriends) {
                    float lonerFactor = math.max(0f, lonerCohesionBoost);
                    lonerFactor *= math.max(1f, lonerRadiusMultiplier);
                    cohesionWeight *= (1.0f + lonerFactor);
                }

                if (isOvercrowded && hasFriends) {
                    float crowdFactor = (groupSize - maxGroupSize) / math.max(1f, (float)maxGroupSize);
                    crowdFactor = math.saturate(crowdFactor);

                    crowdFactor *= math.max(1f, groupRadiusMultiplier);

                    separationWeight *= (1.0f + crowdFactor);
                    cohesionWeight *= (1.0f - 0.5f * crowdFactor);
                }
            }

            // --- 4) Core flock steering (Boids rules) ---
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

            // --- 5) Split behaviour (panic-driven group splitting) ---
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

            int minSplitGroup = 3;
            if (hasGroupSettings) {
                minSplitGroup = math.max(minSplitGroup, minGroupSize);
            }

            bool hasGroupForSplit = groupSize >= minSplitGroup;
            bool canSplit = splitPanicThreshold > 0.0f && splitLateralWeight > 0.0f && splitAccelBoost >= 0.0f;
            bool doSplit = hasGroupForSplit && canSplit && avoidDanger >= splitPanicThreshold;

            if (doSplit) {
                float3 fleeSource = math.lengthsq(avoidSeparation) > 1e-6f
                    ? avoidSeparation
                    : (math.lengthsq(separation) > 1e-6f ? separation : velocity);

                float3 fleeDir = math.normalizesafe(
                    fleeSource,
                    new float3(0.0f, 0.0f, 1.0f));

                float3 up = new float3(0.0f, 1.0f, 0.0f);
                float3 side = math.cross(fleeDir, up);
                if (math.lengthsq(side) < 1e-4f) {
                    up = new float3(0.0f, 0.0f, 1.0f);
                    side = math.cross(fleeDir, up);
                }
                side = math.normalizesafe(side, float3.zero);

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

                float splitIntensity = math.saturate(avoidDanger);
                float3 splitForce = branchDir * separationWeight * splitIntensity;

                steering += splitForce;

                float boost = 1.0f + splitAccelBoost * splitIntensity;
                localMaxAcceleration *= boost;
                localMaxSpeed *= boost;
            }

            // --- 6) Obstacles ---
            if (UseObstacleAvoidance && ObstacleSteering.IsCreated) {
                float3 obstacleAccel = ObstacleSteering[index];
                steering += obstacleAccel * ObstacleAvoidWeight;
            }

            // --- 7) Attraction (no depth logic here) ---
            steering += ComputeAttraction(index);

            // --- 8) Self-propulsion (target speed) ---
            float3 propulsion = ComputePropulsion(
                currentVelocity: velocity,
                desiredSpeed: desiredSpeed);

            steering += propulsion;

            // --- 9) Integrate acceleration / velocity / damping ---
            steering = LimitVector(steering, localMaxAcceleration);

            velocity += steering * DeltaTime;
            velocity = LimitVector(velocity, localMaxSpeed);
            velocity = ApplyDamping(velocity, EnvironmentData.GlobalDamping, DeltaTime);

            // --- 10) Preferred depth controller (Y-only autopilot) ---
            velocity = ApplyPreferredDepth(
                behaviourIndex,
                position,
                velocity,
                localMaxSpeed);

            // Make sure vertical correction didn’t overshoot global speed
            velocity = LimitVector(velocity, localMaxSpeed);

            // --- 11) Bounds ---
            velocity = ApplyBoundsSteering(position, velocity, EnvironmentData);

            // --- 12) HARD PAIR-RADIUS CONSTRAINT (new) ---
            velocity = ApplyPairRadiusConstraint(
                agentIndex: index,
                myBehaviourIndex: behaviourIndex,
                position: position,
                velocity: velocity);


            velocity = ApplyTurnLimit(
                previousVelocity: PrevVelocities[index],
                newVelocity: velocity,
                maxTurnRateDeg: maxTurnRateDeg,
                responsiveness: turnResponsiveness,
                deltaTime: DeltaTime);

            velocity = ApplyAccelerationLimit(
                previousVelocity: PrevVelocities[index],
                newVelocity: velocity,
                maxAcceleration: localMaxAcceleration,
                deltaTime: DeltaTime);

            Velocities[index] = velocity;
        }

        float3 ApplyAccelerationLimit(
    float3 previousVelocity,
    float3 newVelocity,
    float maxAcceleration,
    float deltaTime) {

            // No limit configured → do nothing
            if (maxAcceleration <= 0f || deltaTime <= 0f) {
                return newVelocity;
            }

            // Delta-v this frame
            float3 dv = newVelocity - previousVelocity;
            float maxDelta = maxAcceleration * deltaTime;

            float dvLenSq = math.lengthsq(dv);
            float maxDeltaSq = maxDelta * maxDelta;

            // If inside allowed accel, keep it
            if (dvLenSq <= maxDeltaSq) {
                return newVelocity;
            }

            // Otherwise scale delta-v down to the cap
            float dvLen = math.sqrt(dvLenSq);
            float scale = maxDelta / math.max(dvLen, 1e-4f);

            return previousVelocity + dv * scale;
        }

        // 3) NEW HELPER METHOD – put it near other helpers (e.g. below ApplyPairRadiusConstraint / SafeRead)

        float3 ApplyTurnLimit(
            float3 previousVelocity,
            float3 newVelocity,
            float maxTurnRateDeg,
            float responsiveness,
            float deltaTime) {

            // No limit configured → do nothing.
            if (maxTurnRateDeg <= 0f) {
                return newVelocity;
            }

            float prevLenSq = math.lengthsq(previousVelocity);
            float newLenSq = math.lengthsq(newVelocity);

            // If any vector is almost zero – don't try to limit
            if (prevLenSq < 1e-8f || newLenSq < 1e-8f) {
                return newVelocity;
            }

            float prevLen = math.sqrt(prevLenSq);
            float newLen = math.sqrt(newLenSq);

            float3 prevDir = previousVelocity / prevLen;
            float3 newDir = newVelocity / newLen;

            float r = math.saturate(responsiveness);

            // Angle between directions
            float dot = math.clamp(math.dot(prevDir, newDir), -1f, 1f);
            float angle = math.acos(dot); // radians

            if (angle <= 1e-4f) {
                // Already almost same direction
                return newVelocity;
            }

            float maxAngleRad = maxTurnRateDeg * (math.PI / 180f) * deltaTime * r;

            // If desired change is smaller than allowed, let it pass
            if (angle <= maxAngleRad) {
                return newVelocity;
            }

            // Rotate previous direction towards desired direction by maxAngleRad
            float t = maxAngleRad / angle;
            t = math.saturate(t);

            float3 limitedDir = math.normalizesafe(
                math.lerp(prevDir, newDir, t),
                newDir);

            // Keep the new speed, only clamp heading change
            return limitedDir * newLen;
        }


        // ========================= NEW HELPER =========================
        // ================== REPLACE ApplyPairRadiusConstraint WITH THIS ==================
        float3 ApplyPairRadiusConstraint(
            int agentIndex,
            int myBehaviourIndex,
            float3 position,
            float3 velocity) {

            // If we don't have authored radii, skip.
            float myRadius = SafeRead(BehaviourBaseRadius, myBehaviourIndex, 0.0f);
            if (myRadius <= 0.0f || !CellToAgents.IsCreated) {
                return velocity;
            }

            int3 baseCell = GetCell(position);

            const float minDistSq = 1e-6f;
            const float minPenetration = 0.01f;  // ignore microscopic overlaps
            const float fullClampPenFrac = 0.25f;  // ≥25% inside -> fully kill inward speed
            const float softPushThresh = 0.05f;  // start outward push only if clearly inside
            const float maxTotalPush = 1.0f;   // safety clamp for accumulated correction

            float3 totalCorrection = float3.zero;

            for (int dx = -NeighbourCellRange; dx <= NeighbourCellRange; dx++) {
                for (int dy = -NeighbourCellRange; dy <= NeighbourCellRange; dy++) {
                    for (int dz = -NeighbourCellRange; dz <= NeighbourCellRange; dz++) {
                        int3 neighbourCell = baseCell + new int3(dx, dy, dz);

                        if (!IsCellInsideGrid(neighbourCell)) {
                            continue;
                        }

                        int cellId = GetCellId(neighbourCell);
                        var enumerator = CellToAgents.GetValuesForKey(cellId);

                        while (enumerator.MoveNext()) {
                            int neighbourIndex = enumerator.Current;

                            if (neighbourIndex == agentIndex) {
                                continue;
                            }

                            float3 neighbourPos = Positions[neighbourIndex];
                            float3 offset = neighbourPos - position;
                            float distSq = math.lengthsq(offset);
                            if (distSq < minDistSq) {
                                continue;
                            }

                            float dist = math.sqrt(distSq);

                            int neighbourBehaviourIndex = BehaviourIds[neighbourIndex];
                            float otherRadius = SafeRead(BehaviourBaseRadius, neighbourBehaviourIndex, 0.0f);

                            float pairRadius = myRadius + otherRadius;
                            if (pairRadius <= 0.0f || dist >= pairRadius) {
                                continue; // outside hard gap
                            }

                            // How deep inside the no-go zone we are (0..1).
                            float penetration = (pairRadius - dist) / pairRadius;
                            if (penetration < minPenetration) {
                                // tiny overlap → ignore to avoid micro jitter
                                continue;
                            }

                            float3 dirToNeighbour = offset / dist;

                            // Radial speed towards neighbour (positive = going in).
                            float radialSpeed = math.dot(velocity, dirToNeighbour);

                            // If we're only slightly inside AND already sliding outwards/tangentially,
                            // don't touch it – let flocking forces pull us out smoothly.
                            if (radialSpeed <= 0.0f && penetration < softPushThresh) {
                                continue;
                            }

                            // Smooth clamp: near boundary only reduce some of inward speed,
                            // deeper inside → kill all inward component.
                            float clampT = math.saturate(penetration / fullClampPenFrac); // 0..1
                            float inwardKill = math.max(0.0f, radialSpeed) * clampT;

                            if (inwardKill > 0.0f) {
                                // Collect correction instead of applying per neighbour directly,
                                // to avoid huge per-frame jumps with many neighbours.
                                totalCorrection -= dirToNeighbour * inwardKill;
                            }

                            // Small outward bias only when clearly inside, scaled by penetration²,
                            // so it’s soft near the boundary and stronger if we are really inside.
                            if (penetration > softPushThresh) {
                                float push = (penetration * penetration) * 0.3f; // tuned "skin" push
                                totalCorrection += (-dirToNeighbour) * push;
                            }
                        }
                    }
                }
            }

            // Safety clamp: don't let constraint itself create extreme acceleration.
            float corrLenSq = math.lengthsq(totalCorrection);
            if (corrLenSq > maxTotalPush * maxTotalPush) {
                float corrLen = math.sqrt(corrLenSq);
                totalCorrection *= (maxTotalPush / math.max(corrLen, 0.0001f));
            }

            velocity += totalCorrection;
            return velocity;
        }


        float3 ComputeAttraction(int agentIndex) {
            if (!UseAttraction || !AttractionSteering.IsCreated) {
                return float3.zero;
            }

            if ((uint)agentIndex >= (uint)AttractionSteering.Length) {
                return float3.zero;
            }

            return AttractionSteering[agentIndex] * GlobalAttractionWeight;
        }

        // =======================================================
        // 3) NEW HELPER – ApplyPreferredDepth (Y controller)
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // =======================================================
        float3 ApplyPreferredDepth(
           int behaviourIndex,
           float3 position,
           float3 velocity,
           float maxSpeed) {

            // --- Safety & enable check ---
            bool hasDepthArrays =
                BehaviourUsePreferredDepth.IsCreated
                && BehaviourPreferredDepthMinNorm.IsCreated
                && BehaviourPreferredDepthMaxNorm.IsCreated
                && BehaviourPreferredDepthWeight.IsCreated
                && BehaviourDepthBiasStrength.IsCreated
                && BehaviourUsePreferredDepth.Length > behaviourIndex
                && BehaviourPreferredDepthMinNorm.Length > behaviourIndex
                && BehaviourPreferredDepthMaxNorm.Length > behaviourIndex
                && BehaviourPreferredDepthWeight.Length > behaviourIndex
                && BehaviourDepthBiasStrength.Length > behaviourIndex;

            if (!hasDepthArrays) {
                return velocity;
            }

            if (BehaviourUsePreferredDepth[behaviourIndex] == 0) {
                return velocity;
            }

            float prefMin = BehaviourPreferredDepthMinNorm[behaviourIndex];
            float prefMax = BehaviourPreferredDepthMaxNorm[behaviourIndex];
            float weight = math.max(0f, BehaviourPreferredDepthWeight[behaviourIndex]);
            float biasStrength = math.max(0f, BehaviourDepthBiasStrength[behaviourIndex]);

            // If no strength configured, effectively disabled
            if (weight <= 0f || biasStrength <= 0f) {
                return velocity;
            }

            if (prefMax < prefMin) {
                float tmp = prefMin;
                prefMin = prefMax;
                prefMax = tmp;
            }

            float bandWidth = prefMax - prefMin;
            if (bandWidth <= 1e-4f) {
                // Degenerate band – treat as no-op
                return velocity;
            }

            // --- Normalised depth [0..1] inside environment bounds ---
            float envMinY = EnvironmentData.BoundsCenter.y - EnvironmentData.BoundsExtents.y;
            float envMaxY = EnvironmentData.BoundsCenter.y + EnvironmentData.BoundsExtents.y;
            float envHeight = math.max(envMaxY - envMinY, 0.0001f);

            float depthNorm = math.saturate((position.y - envMinY) / envHeight);

            float vy = velocity.y;

            // Combined strength scalar 0..1
            float strength = math.saturate(weight * biasStrength);

            // === ZONE A: Outside the band – strong push back in ===
            if (depthNorm < prefMin || depthNorm > prefMax) {
                float deltaNorm;
                float dir; // +1 = push up, -1 = push down

                if (depthNorm < prefMin) {
                    deltaNorm = (prefMin - depthNorm) / bandWidth;
                    dir = 1.0f;
                } else {
                    deltaNorm = (depthNorm - prefMax) / bandWidth;
                    dir = -1.0f;
                }

                deltaNorm = math.saturate(deltaNorm);               // 0..1
                float edgeT = deltaNorm * deltaNorm;                // smoother ramp
                float lerpFactor = math.saturate(strength * edgeT); // 0..1

                float targetVy = dir * maxSpeed;

                vy = math.lerp(vy, targetVy, lerpFactor);

                float damping = math.saturate(1.0f - strength * edgeT * DeltaTime);
                vy *= damping;
            }
            // === ZONE B / C: Inside the band ===
            else {
                // Explicit control: per-type edge buffer thickness [0..0.5] of band width
                float edgeFrac = 0.25f;
                if (BehaviourPreferredDepthEdgeFraction.IsCreated
                    && BehaviourPreferredDepthEdgeFraction.Length > behaviourIndex) {
                    edgeFrac = math.clamp(BehaviourPreferredDepthEdgeFraction[behaviourIndex], 0.01f, 0.49f);
                }

                float borderThickness = bandWidth * edgeFrac;

                float distToMin = depthNorm - prefMin;
                float distToMax = prefMax - depthNorm;
                float edgeDist = math.min(distToMin, distToMax);

                // ZONE B: inside band but close to edges – soft push + damping
                if (edgeDist < borderThickness) {
                    float t = 1.0f - edgeDist / math.max(borderThickness, 0.0001f); // 0 at inner edge of buffer, 1 at band edge
                    t = math.saturate(t);

                    bool nearBottom = distToMin < distToMax;
                    float dir = nearBottom ? 1.0f : -1.0f; // bottom → up, top → down

                    float innerStrength = strength * 0.5f; // weaker than outside band
                    float lerpFactor = math.saturate(innerStrength * t);

                    float targetVy = dir * maxSpeed * (innerStrength * t);

                    vy = math.lerp(vy, targetVy, lerpFactor);

                    float damping = math.saturate(1.0f - innerStrength * t * DeltaTime);
                    vy *= damping;
                }
                // ZONE C: inside central band – no extra vertical steering
                else {
                    // leave vy as is; depth controller is inactive in the inner band
                }
            }

            // Clamp vertical speed to something reasonable (<= maxSpeed)
            float maxVy = maxSpeed;
            vy = math.clamp(vy, -maxVy, maxVy);

            velocity.y = vy;
            return velocity;
        }

        // =====================================================================
        // NEW HELPER: common per-behaviour scalars
        // =====================================================================

        bool TryLoadBehaviourScalars(
            int agentIndex,
            out float3 position,
            out float3 velocity,
            out int behaviourIndex,
            out float maxSpeed,
            out float maxAcceleration,
            out float desiredSpeed,
            out float neighbourRadius,
            out float separationRadius,
            out float alignmentWeight,
            out float cohesionWeight,
            out float separationWeight,
            out float influenceWeight,
            out uint friendlyMask,
            out uint avoidMask,
            out uint neutralMask) {

            position = Positions[agentIndex];
            velocity = PrevVelocities[agentIndex];

            behaviourIndex = BehaviourIds[agentIndex];

            if ((uint)behaviourIndex >= (uint)BehaviourMaxSpeed.Length) {
                maxSpeed = maxAcceleration = desiredSpeed = 0f;
                neighbourRadius = separationRadius = 0f;
                alignmentWeight = cohesionWeight = separationWeight = 0f;
                influenceWeight = 0f;
                friendlyMask = avoidMask = neutralMask = 0u;
                return false;
            }

            maxSpeed = BehaviourMaxSpeed[behaviourIndex];
            maxAcceleration = BehaviourMaxAcceleration[behaviourIndex];
            desiredSpeed = BehaviourDesiredSpeed[behaviourIndex];
            neighbourRadius = BehaviourNeighbourRadius[behaviourIndex];
            separationRadius = BehaviourSeparationRadius[behaviourIndex];
            alignmentWeight = BehaviourAlignmentWeight[behaviourIndex];
            cohesionWeight = BehaviourCohesionWeight[behaviourIndex];
            separationWeight = BehaviourSeparationWeight[behaviourIndex];
            influenceWeight = BehaviourInfluenceWeight[behaviourIndex];

            friendlyMask = BehaviourGroupMask[behaviourIndex];
            avoidMask = BehaviourAvoidMask[behaviourIndex];
            neutralMask = BehaviourNeutralMask[behaviourIndex];

            return true;
        }

        void AccumulateNeighbourForces(
            int agentIndex,
            int myBehaviourIndex,
            uint friendlyMask,
            uint avoidMask,
            uint neutralMask,
            float3 agentPosition,
            float neighbourRadius,
            float separationRadius, // kept for fallback only
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

            // === Read my radius + zone parameters (fully driven from BehaviourProfile) ===
            float myRadius = SafeRead(BehaviourBaseRadius, myBehaviourIndex, 0.0f);
            float deadBandFraction = math.saturate(SafeRead(BehaviourDeadBandFraction, myBehaviourIndex, 0.10f));
            float friendlyInnerFraction = math.saturate(SafeRead(BehaviourFriendlyInnerFraction, myBehaviourIndex, 0.25f));

            float friendDistanceFactor = math.max(0.1f, SafeRead(BehaviourFriendDistanceFactor, myBehaviourIndex, 1.4f));
            float avoidDistanceFactor = math.max(0.1f, SafeRead(BehaviourAvoidDistanceFactor, myBehaviourIndex, 3.0f));
            float neutralDistanceFactor = math.max(0.1f, SafeRead(BehaviourNeutralDistanceFactor, myBehaviourIndex, 2.0f));
            float influenceDistanceFactor = math.max(0.1f, SafeRead(BehaviourInfluenceDistanceFactor, myBehaviourIndex, 4.0f));

            float hardRepulsionGain = math.max(0f, SafeRead(BehaviourHardRepulsionGain, myBehaviourIndex, 4.0f));
            float friendlySoftGain = math.max(0f, SafeRead(BehaviourFriendlySoftGain, myBehaviourIndex, 1.0f));
            float avoidRadialGain = math.max(0f, SafeRead(BehaviourAvoidRadialGain, myBehaviourIndex, 2.0f));
            float neutralRadialGain = math.max(0f, SafeRead(BehaviourNeutralRadialGain, myBehaviourIndex, 0.75f));

            float myAvoidWeight = BehaviourAvoidanceWeight[myBehaviourIndex];
            float myNeutralWeight = BehaviourNeutralWeight[myBehaviourIndex];
            float myAvoidResponse = math.max(0f, BehaviourAvoidResponse[myBehaviourIndex]);

            int3 baseCell = GetCell(agentPosition);

            float maxLeaderWeight = -1.0f;
            const float epsilon = 1e-4f;

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

                            if (!isFriendly && !isAvoid && !isNeutral) {
                                continue;
                            }

                            float distance = math.sqrt(distanceSquared);
                            if (distance <= 0.0f) {
                                continue;
                            }

                            float3 dir = offset / distance;

                            // === Pair radius & zones ===
                            float otherRadius = SafeRead(BehaviourBaseRadius, neighbourBehaviourIndex, 0.0f);
                            float pairRadius = myRadius + otherRadius;

                            if (pairRadius <= 0.0f) {
                                // fallback if nothing was authored
                                pairRadius = math.max(1e-3f, separationRadius);
                            }

                            float contact = pairRadius;
                            float deadBandEnd = contact * (1.0f + deadBandFraction);
                            float friendlyInnerEnd = contact * (1.0f + friendlyInnerFraction);

                            float friendTarget = pairRadius * friendDistanceFactor;
                            float avoidMin = pairRadius * avoidDistanceFactor;
                            float neutralMin = pairRadius * neutralDistanceFactor;
                            float maxInfluence = pairRadius * influenceDistanceFactor;
                            float maxInfluenceSq = maxInfluence * maxInfluence;

                            if (distanceSquared > maxInfluenceSq) {
                                continue;
                            }

                            // === Leadership + tangential schooling ===
                            float tProximity = 1.0f - math.saturate(distance / neighbourRadius);

                            if (isFriendly) {
                                friendlyNeighbourCount += 1;

                                float neighbourLeaderWeight = BehaviourLeadershipWeight[neighbourBehaviourIndex];
                                float3 neighbourVelocity = PrevVelocities[neighbourIndex];

                                if (neighbourLeaderWeight > maxLeaderWeight + epsilon) {
                                    maxLeaderWeight = neighbourLeaderWeight;
                                    leaderNeighbourCount = 1;

                                    alignment = neighbourVelocity * tProximity;
                                    cohesion = neighbourPosition * tProximity;

                                    alignmentWeightSum = tProximity;
                                    cohesionWeightSum = tProximity;
                                } else if (math.abs(neighbourLeaderWeight - maxLeaderWeight) <= epsilon
                                           && maxLeaderWeight > -1.0f) {
                                    leaderNeighbourCount += 1;

                                    alignment += neighbourVelocity * tProximity;
                                    cohesion += neighbourPosition * tProximity;

                                    alignmentWeightSum += tProximity;
                                    cohesionWeightSum += tProximity;
                                }
                            }

                            // === Radial repulsion from unified radius ===
                            float repulseFactor = 0.0f;

                            // 1) Inside body – hard push
                            if (distance < contact) {
                                float penetration = (contact - distance) / contact;
                                repulseFactor += hardRepulsionGain * penetration;
                            }

                            // 2) Dead band after contact – kill jitter but keep tiny push
                            if (distance >= contact && distance < deadBandEnd) {
                                float span = math.max(deadBandEnd - contact, 1e-4f);
                                float tDead = (deadBandEnd - distance) / span; // 1 at contact, 0 at deadBandEnd
                                repulseFactor += friendlySoftGain * tDead * 0.25f;
                            }

                            // 3) Friendly inner shell
                            if (isFriendly && distance < friendlyInnerEnd) {
                                float innerStart = contact;
                                float innerEnd = friendlyInnerEnd;
                                float spanInner = math.max(innerEnd - innerStart, 1e-4f);
                                float tInner = (innerEnd - distance) / spanInner;
                                repulseFactor += friendlySoftGain * tInner;
                            }

                            // 4) Avoid: prey from predators
                            if (isAvoid && distance < avoidMin) {
                                float deficit = (avoidMin - distance) / avoidMin;
                                float gain = avoidRadialGain * (1.0f + myAvoidWeight);
                                repulseFactor += gain * deficit;

                                float localIntensity = deficit * myAvoidResponse;
                                if (localIntensity > avoidDanger) {
                                    avoidDanger = localIntensity;
                                }
                            }

                            // 5) Neutral: soft spacing
                            if (isNeutral && distance < neutralMin) {
                                float deficit = (neutralMin - distance) / neutralMin;
                                float gain = neutralRadialGain * (1.0f + myNeutralWeight * 0.5f);
                                repulseFactor += gain * deficit;
                            }

                            if (repulseFactor > 0.0f) {
                                float3 repulse = -dir * repulseFactor;

                                // ==== NEW: shape separation so it doesn't hard-flip direction ====
                                // Use previous velocity to bias separation into more "side-step" than pure 180° turn,
                                // which removes the glitchy back-and-forth when packed.
                                repulse = ShapeSeparationResponse(
                                    repulse,
                                    PrevVelocities[agentIndex]);

                                separation += repulse;
                                separationCount += 1;

                                if (isAvoid) {
                                    avoidSeparation += repulse;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Add this helper somewhere below inside FlockStepJob (alongside SafeRead, LimitVector, etc.)

        float3 ShapeSeparationResponse(
            float3 repulse,
            float3 currentVelocity) {

            float repulseLenSq = math.lengthsq(repulse);
            float velLenSq = math.lengthsq(currentVelocity);

            // No shaping if one of the vectors is almost zero
            if (repulseLenSq < 1e-8f || velLenSq < 1e-8f) {
                return repulse;
            }

            float3 forward = currentVelocity / math.sqrt(velLenSq);
            float3 repulseDir = repulse / math.sqrt(repulseLenSq);

            // dotForward:
            //  +1 = same direction as forward
            //   0 = perpendicular
            //  -1 = fully opposite (full brake / flip)
            float dotForward = math.dot(repulseDir, forward);

            // Only reshape when we would strongly push backwards.
            if (dotForward < -0.25f) {
                // Build a sideways axis relative to our current forward.
                float3 side = math.cross(forward, new float3(0f, 1f, 0f));
                if (math.lengthsq(side) < 1e-4f) {
                    side = math.cross(forward, new float3(0f, 0f, 1f));
                }
                side = math.normalizesafe(side, repulseDir);

                float t = math.saturate((-dotForward - 0.25f) / 0.75f); // 0 at -0.25, 1 at -1
                float magnitude = math.sqrt(repulseLenSq);

                float3 sideways = side * magnitude;
                float3 backward = -forward * magnitude;

                // Prefer sliding around neighbour (sideways) instead of full reverse.
                float3 target = sideways * 0.7f + backward * 0.3f;

                // Blend between original pure radial repulse and shaped side-step.
                return math.lerp(repulse, target, t);
            }

            return repulse;
        }


        float SafeRead(NativeArray<float> array, int index, float fallback) {
            if (!array.IsCreated) return fallback;
            if ((uint)index >= (uint)array.Length) return fallback;
            return array[index];
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