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

        // Bounds probe outputs
        [ReadOnly] public NativeArray<float3> WallDirections;
        [ReadOnly] public NativeArray<float> WallDangers;

        // Per-behaviour bounds response (all optional / IsCreated-guarded)
        [ReadOnly] public NativeArray<float> BehaviourBoundsWeight;              // radial push strength
        [ReadOnly] public NativeArray<float> BehaviourBoundsTangentialDamping;   // kill sliding
        [ReadOnly] public NativeArray<float> BehaviourBoundsInfluenceSuppression; // how much to mute flocking near walls

        [NativeDisableParallelForRestriction]
        public NativeArray<int> NeighbourVisitStamp;

        [ReadOnly]
        public NativeArray<float3> Positions;

        // NEW: snapshot of last frame velocities used for neighbour logic
        [ReadOnly]
        public NativeArray<float3> PrevVelocities;

        // NEW: this frame's velocities (write target)
        [NativeDisableParallelForRestriction]
        public NativeArray<float3> Velocities;

        [ReadOnly] public NativeArray<int> BehaviourIds;
        [ReadOnly] public NativeArray<int> BehaviourCellSearchRadius;
        [ReadOnly] public NativeArray<float> BehaviourBodyRadius;

        [ReadOnly] public NativeArray<float> BehaviourMaxSpeed;
        [ReadOnly] public NativeArray<float> BehaviourMaxAcceleration;
        [ReadOnly] public NativeArray<float> BehaviourDesiredSpeed;
        [ReadOnly] public NativeArray<float> BehaviourNeighbourRadius;
        [ReadOnly] public NativeArray<float> BehaviourSeparationRadius;
        [ReadOnly] public NativeArray<float> BehaviourAlignmentWeight;
        [ReadOnly] public NativeArray<float> BehaviourCohesionWeight;
        [ReadOnly] public NativeArray<float> BehaviourSeparationWeight;
        [ReadOnly] public NativeArray<float> BehaviourInfluenceWeight;
        [ReadOnly] public NativeArray<float> BehaviourGroupFlowWeight;

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

        // NEW: schooling band params per type
        [ReadOnly] public NativeArray<float> BehaviourSchoolSpacingFactor;
        [ReadOnly] public NativeArray<float> BehaviourSchoolOuterFactor;
        [ReadOnly] public NativeArray<float> BehaviourSchoolStrength;
        [ReadOnly] public NativeArray<float> BehaviourSchoolInnerSoftness;
        [ReadOnly] public NativeArray<float> BehaviourSchoolDeadzoneFraction;

        [ReadOnly] public NativeArray<byte> BehaviourUsePreferredDepth;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMin;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMax;
        [ReadOnly] public NativeArray<float> BehaviourDepthBiasStrength;
        [ReadOnly] public NativeArray<byte> BehaviourDepthWinsOverAttractor;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMinNorm;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMaxNorm;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthWeight;
        [ReadOnly] public NativeArray<float> BehaviourSchoolRadialDamping;

        [ReadOnly] public bool UseAttraction;
        [ReadOnly] public float GlobalAttractionWeight;
        [ReadOnly] public NativeArray<float3> AttractionSteering;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthEdgeFraction;

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
                    out float groupFlowWeight,
                    out uint friendlyMask,
                    out uint avoidMask,
                    out uint neutralMask)) {

                // Behaviour index out of range – keep previous velocity.
                Velocities[index] = PrevVelocities[index];
                return;
            }

            float boundsWeight = 1f;
            float boundsTangentialDamping = 0.0f;
            float boundsInfluenceSuppression = 1.0f;

            if (BehaviourBoundsWeight.IsCreated &&
                (uint)behaviourIndex < (uint)BehaviourBoundsWeight.Length) {
                boundsWeight = math.max(0f, BehaviourBoundsWeight[behaviourIndex]);
            }

            if (BehaviourBoundsTangentialDamping.IsCreated &&
                (uint)behaviourIndex < (uint)BehaviourBoundsTangentialDamping.Length) {
                boundsTangentialDamping = math.max(0f, BehaviourBoundsTangentialDamping[behaviourIndex]);
            }

            if (BehaviourBoundsInfluenceSuppression.IsCreated &&
                (uint)behaviourIndex < (uint)BehaviourBoundsInfluenceSuppression.Length) {
                boundsInfluenceSuppression = math.max(0f, BehaviourBoundsInfluenceSuppression[behaviourIndex]);
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

            // NEW: accumulates radial “brake” based on predictive damping
            float3 radialDamping = float3.zero;

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
                avoidSeparation: ref avoidSeparation,
                radialDamping: ref radialDamping);   // NEW

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
                bool isOvercrowded = maxGroupSize > 0 && groupSize > maxGroupSize;

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

            // NEW: apply predictive radial braking as additional steering
            steering += radialDamping;

            // --- Group-flow steering: push velocity toward local flock heading ---
            // With suppression near walls so corner-huggers don't drag the world.
            float localGroupFlowWeight = groupFlowWeight;

            if (localGroupFlowWeight > 0f
                && leaderNeighbourCount > 0
                && alignmentWeightSum > 1e-6f) {

                // Average neighbour heading we already collected for alignment
                float3 groupDirRaw = alignment / alignmentWeightSum;
                float3 groupDir = math.normalizesafe(groupDirRaw, velocity);

                // Target speed: use desiredSpeed if set, otherwise keep current magnitude
                float currentSpeed = math.length(velocity);
                float targetSpeed = desiredSpeed > 0f ? desiredSpeed : currentSpeed;

                if (targetSpeed > 1e-4f) {
                    float3 desiredGroupVel = groupDir * targetSpeed;

                    // Extra accel that tries to pull us into the group flow
                    float3 flowAccel = (desiredGroupVel - velocity) * localGroupFlowWeight;

                    steering += flowAccel;
                }
            }

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

            // --- 8.5) Bounds: gate non-wall steering + add radial push ---
            steering = ApplyBoundsSteering(
                index,
                behaviourIndex,
                velocity,
                steering,
                maxAcceleration,
                boundsWeight,
                boundsInfluenceSuppression);

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

            // --- 11) Bounds final velocity correction (kill sliding along wall) ---
            velocity = ApplyBoundsVelocity(
                index,
                behaviourIndex,
                velocity,
                boundsTangentialDamping);

            velocity = LimitVector(velocity, localMaxSpeed);

            Velocities[index] = velocity;
        }

        // ======================================================
        // 5) FlockStepJob – updated ComputeSchoolingBandForce
        //      now also exposes thresholds for zone gating
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // ======================================================

        float3 ComputeSchoolingBandForce(
            int myBehaviourIndex,
            int neighbourBehaviourIndex,
            float distance,
            float3 dir,                  // dir: from me toward neighbour, normalized
            out float collisionDist,     // touch distance = rA + rB
            out float targetDist,        // preferred spacing distance
            out float deadZoneUpper,     // upper bound of dead/comfort zone
            out float farDist)           // max distance where band acts
        {
            collisionDist = 0f;
            targetDist = 0f;
            deadZoneUpper = 0f;
            farDist = 0f;

            // Basic safety: arrays must exist and indices be valid.
            if (!BehaviourBodyRadius.IsCreated
                || !BehaviourSchoolSpacingFactor.IsCreated
                || !BehaviourSchoolOuterFactor.IsCreated
                || !BehaviourSchoolStrength.IsCreated) {
                return float3.zero;
            }

            if ((uint)myBehaviourIndex >= (uint)BehaviourBodyRadius.Length ||
                (uint)neighbourBehaviourIndex >= (uint)BehaviourBodyRadius.Length) {
                return float3.zero;
            }

            float rA = BehaviourBodyRadius[myBehaviourIndex];
            float rB = BehaviourBodyRadius[neighbourBehaviourIndex];

            // If both radii are zero, there's no meaningful band.
            if (rA <= 0f && rB <= 0f)
                return float3.zero;

            // Per-type spacing / outer range / strength
            float spacingA = BehaviourSchoolSpacingFactor[myBehaviourIndex];
            float spacingB = BehaviourSchoolSpacingFactor[neighbourBehaviourIndex];

            float outerA = BehaviourSchoolOuterFactor[myBehaviourIndex];
            float outerB = BehaviourSchoolOuterFactor[neighbourBehaviourIndex];

            float strA = BehaviourSchoolStrength[myBehaviourIndex];
            float strB = BehaviourSchoolStrength[neighbourBehaviourIndex];

            // Optional inner softness and dead zone params
            float softnessA = 1f;
            float softnessB = 1f;
            if (BehaviourSchoolInnerSoftness.IsCreated) {
                if (BehaviourSchoolInnerSoftness.Length > myBehaviourIndex)
                    softnessA = BehaviourSchoolInnerSoftness[myBehaviourIndex];
                if (BehaviourSchoolInnerSoftness.Length > neighbourBehaviourIndex)
                    softnessB = BehaviourSchoolInnerSoftness[neighbourBehaviourIndex];
            }

            float deadA = 0f;
            float deadB = 0f;
            if (BehaviourSchoolDeadzoneFraction.IsCreated) {
                if (BehaviourSchoolDeadzoneFraction.Length > myBehaviourIndex)
                    deadA = BehaviourSchoolDeadzoneFraction[myBehaviourIndex];
                if (BehaviourSchoolDeadzoneFraction.Length > neighbourBehaviourIndex)
                    deadB = BehaviourSchoolDeadzoneFraction[neighbourBehaviourIndex];
            }

            // Pair-wise parameters (simple average, then clamped)
            float spacing = math.max(0.5f, 0.5f * (spacingA + spacingB));
            float outer = math.max(1f, 0.5f * (outerA + outerB));
            float strength = math.max(0f, 0.5f * (strA + strB));
            if (strength <= 0f)
                return float3.zero;

            float softness = math.clamp(0.5f * (softnessA + softnessB), 0f, 1f);
            float deadFrac = math.clamp(0.5f * (deadA + deadB), 0f, 0.5f);

            collisionDist = rA + rB;          // touch distance
            targetDist = collisionDist * spacing;
            farDist = targetDist * outer;

            if (distance <= 0f || distance >= farDist)
                return float3.zero;

            float deadRadius = deadFrac * targetDist;
            float deadLower = targetDist;
            deadZoneUpper = targetDist + deadRadius;

            float force = 0f;

            // Zone 1: deep collision – strong repulsion
            if (distance < collisionDist) {
                float t = (collisionDist - distance) / math.max(collisionDist, 1e-3f); // 0..1
                force = t; // positive → repulsive
            }
            // Zone 2: between collisionDist and targetDist – soft repulsion
            else if (distance < targetDist) {
                float innerSpan = math.max(targetDist - collisionDist, 1e-3f);
                float t = (targetDist - distance) / innerSpan; // 0..1

                // shaped curve: higher softness pushes more near collision, smoother near target
                float shaped = (softness > 0f)
                    ? math.pow(t, 1f + softness * 2f)
                    : t;

                force = shaped; // still repulsive, but weaker than deep collision
            } else {
                // Dead zone ABOVE target distance: small band with near-zero force
                if (distance >= deadLower && distance <= deadZoneUpper) {
                    return float3.zero;
                }

                // Zone 4: outer attraction between deadZoneUpper and farDist
                if (distance < farDist) {
                    float attractStart = deadZoneUpper;
                    float attractSpan = math.max(farDist - attractStart, 1e-3f);
                    float t = (distance - attractStart) / attractSpan; // 0..1

                    float falloff = 1f - t; // strongest just outside dead zone
                    force = -falloff;       // negative → attraction (towards neighbour)
                } else {
                    return float3.zero;
                }
            }

            if (math.abs(force) <= 1e-5f)
                return float3.zero;

            float signedStrength = force * strength;
            return -dir * signedStrength;
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

        // =======================================================
        // Bounds helpers – steering + velocity correction
        // =======================================================

        float3 ApplyBoundsSteering(
            int agentIndex,
            int behaviourIndex,
            float3 currentVelocity,
            float3 steering,
            float maxAcceleration,
            float boundsWeight,
            float boundsInfluenceSuppression) {

            if (!WallDangers.IsCreated || !WallDirections.IsCreated) {
                return steering;
            }

            if ((uint)agentIndex >= (uint)WallDangers.Length ||
                (uint)agentIndex >= (uint)WallDirections.Length) {
                return steering;
            }

            float danger = WallDangers[agentIndex];
            if (danger <= 0f) {
                return steering;
            }

            float3 wallDir = WallDirections[agentIndex];
            if (math.lengthsq(wallDir) < 1e-8f) {
                return steering;
            }

            // 1) Gate non-wall steering: closer to wall → flock/attraction have less authority
            float gate = 1f - danger * boundsInfluenceSuppression;
            gate = math.saturate(gate);
            steering *= gate;

            // 2) Add radial push back into volume
            float3 n = math.normalizesafe(wallDir, float3.zero);
            float radialAccel = danger * boundsWeight * maxAcceleration;
            steering += n * radialAccel;

            return steering;
        }

        float3 ApplyBoundsVelocity(
            int agentIndex,
            int behaviourIndex,
            float3 velocity,
            float boundsTangentialDamping) {

            if (!WallDangers.IsCreated || !WallDirections.IsCreated) {
                return velocity;
            }

            if ((uint)agentIndex >= (uint)WallDangers.Length ||
                (uint)agentIndex >= (uint)WallDirections.Length) {
                return velocity;
            }

            float danger = WallDangers[agentIndex];
            if (danger <= 0f || boundsTangentialDamping <= 0f) {
                return velocity;
            }

            float3 wallDir = WallDirections[agentIndex];
            if (math.lengthsq(wallDir) < 1e-8f) {
                return velocity;
            }

            float3 n = math.normalizesafe(wallDir, float3.zero);

            // Decompose velocity into radial + tangential components
            float vRadial = math.dot(velocity, n);
            float3 vRad = n * vRadial;
            float3 vTan = velocity - vRad;

            // Kill tangential component proportionally to danger
            float kill = danger * boundsTangentialDamping * DeltaTime;
            kill = math.saturate(kill);

            vTan *= (1f - kill);

            return vRad + vTan;
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
            out float groupFlowWeight,
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
                groupFlowWeight = 0f;  // NEW
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

            groupFlowWeight = 0f;
            if (BehaviourGroupFlowWeight.IsCreated
                && (uint)behaviourIndex < (uint)BehaviourGroupFlowWeight.Length) {
                groupFlowWeight = BehaviourGroupFlowWeight[behaviourIndex];
            }

            friendlyMask = BehaviourGroupMask[behaviourIndex];
            avoidMask = BehaviourAvoidMask[behaviourIndex];
            neutralMask = BehaviourNeutralMask[behaviourIndex];

            return true;
        }

        // UPDATED: hard BodyRadius-based separation + per-type cellRange + dedup
        // ======================================================
        // 6) FlockStepJob – updated AccumulateNeighbourForces
        //      zone-gated cohesion + radial damping, single neighbour loop
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // ======================================================

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
            ref float3 avoidSeparation,
            ref float3 radialDamping) // NEW: accumulates predictive braking
        {
            float neighbourRadiusSquared = neighbourRadius * neighbourRadius;
            float separationRadiusSquared = separationRadius * separationRadius;

            float myAvoidWeight = BehaviourAvoidanceWeight[myBehaviourIndex];
            float myNeutralWeight = BehaviourNeutralWeight[myBehaviourIndex];
            float myAvoidResponse = math.max(0f, BehaviourAvoidResponse[myBehaviourIndex]);

            // Per-type radial damping strength for this agent
            float myRadialDamping = 0f;
            if (BehaviourSchoolRadialDamping.IsCreated &&
                (uint)myBehaviourIndex < (uint)BehaviourSchoolRadialDamping.Length) {
                myRadialDamping = BehaviourSchoolRadialDamping[myBehaviourIndex];
            }

            int3 baseCell = GetCell(agentPosition);

            // Leadership state
            float maxLeaderWeight = -1.0f;
            const float epsilon = 1e-4f;

            avoidDanger = 0.0f;
            friendlyNeighbourCount = 0;
            avoidSeparation = float3.zero;

            // Per-type cell search radius
            int cellRange = 1;
            if (BehaviourCellSearchRadius.IsCreated &&
                (uint)myBehaviourIndex < (uint)BehaviourCellSearchRadius.Length) {
                cellRange = math.max(BehaviourCellSearchRadius[myBehaviourIndex], 1);
            }

            // Per-agent dedup stamp: same neighbour in many cells is processed once
            int stampValue = agentIndex + 1;

            for (int x = -cellRange; x <= cellRange; x++) {
                for (int y = -cellRange; y <= cellRange; y++) {
                    for (int z = -cellRange; z <= cellRange; z++) {
                        int3 neighbourCell = baseCell + new int3(x, y, z);

                        if (!IsCellInsideGrid(neighbourCell))
                            continue;

                        int cellId = GetCellId(neighbourCell);

                        var enumerator = CellToAgents.GetValuesForKey(cellId);
                        while (enumerator.MoveNext()) {
                            int neighbourIndex = enumerator.Current;
                            if (neighbourIndex == agentIndex)
                                continue;

                            // Dedup neighbour if it appears in multiple cells
                            if (NeighbourVisitStamp.IsCreated) {
                                int prev = NeighbourVisitStamp[neighbourIndex];
                                if (prev == stampValue)
                                    continue;

                                NeighbourVisitStamp[neighbourIndex] = stampValue;
                            }

                            float3 neighbourPosition = Positions[neighbourIndex];
                            float3 offset = neighbourPosition - agentPosition;
                            float distanceSquared = math.lengthsq(offset);
                            if (distanceSquared < 1e-6f)
                                continue;

                            int neighbourBehaviourIndex = BehaviourIds[neighbourIndex];
                            if ((uint)neighbourBehaviourIndex >= (uint)BehaviourLeadershipWeight.Length)
                                continue;

                            float distance = math.sqrt(distanceSquared);
                            float3 dirUnit = offset / distance; // from me → neighbour

                            // --- HARD GEOMETRIC SEPARATION USING BODY RADIUS + separationRadius ---
                            float hardRadiusSq = separationRadiusSquared;

                            if (BehaviourBodyRadius.IsCreated &&
                                (uint)myBehaviourIndex < (uint)BehaviourBodyRadius.Length &&
                                (uint)neighbourBehaviourIndex < (uint)BehaviourBodyRadius.Length) {
                                float myBody = BehaviourBodyRadius[myBehaviourIndex];
                                float nbBody = BehaviourBodyRadius[neighbourBehaviourIndex];

                                float collisionDistBody = myBody + nbBody;
                                if (collisionDistBody > 0f) {
                                    float collisionDistSq = collisionDistBody * collisionDistBody;
                                    if (collisionDistSq > hardRadiusSq)
                                        hardRadiusSq = collisionDistSq;
                                }
                            }

                            if (hardRadiusSq > 0f && distanceSquared < hardRadiusSq) {
                                float hardRadius = math.sqrt(hardRadiusSq);

                                float penetration = hardRadius - distance; // how deep inside
                                float strength = penetration / math.max(hardRadius, 1e-3f);

                                // Stronger repulsion the deeper we are inside
                                separation -= dirUnit * (1f + strength);
                                separationCount += 1;
                            }

                            // Beyond neighbour radius → no flock rules (but hard separation still applied)
                            if (distanceSquared > neighbourRadiusSquared)
                                continue;

                            uint bit = neighbourBehaviourIndex < 32
                                ? (1u << neighbourBehaviourIndex)
                                : 0u;

                            bool isFriendly = bit != 0u && (friendlyMask & bit) != 0u;
                            bool isAvoid = bit != 0u && (avoidMask & bit) != 0u;
                            bool isNeutral = bit != 0u && (neutralMask & bit) != 0u;

                            // No declared relation (apart from hard separation above) – ignore
                            if (!isFriendly && !isAvoid && !isNeutral)
                                continue;

                            if (distance <= 0.0f || neighbourRadius <= 0.0f)
                                continue;

                            float t = 1.0f - math.saturate(distance / neighbourRadius);

                            // === FRIENDLY: schooling distance band + zone-gated cohesion ===
                            if (isFriendly) {
                                friendlyNeighbourCount += 1;

                                // Size-aware distance band force (repulsion + attraction)
                                float collisionDistBand;
                                float targetDistBand;
                                float deadUpperBand;
                                float farDistBand;

                                float3 bandForce = ComputeSchoolingBandForce(
                                    myBehaviourIndex,
                                    neighbourBehaviourIndex,
                                    distance,
                                    dirUnit,
                                    out collisionDistBand,
                                    out targetDistBand,
                                    out deadUpperBand,
                                    out farDistBand);

                                if (math.lengthsq(bandForce) > 0f) {
                                    separation += bandForce;
                                    separationCount += 1;
                                }

                                bool haveBand =
                                    farDistBand > 0f &&
                                    collisionDistBand > 0f &&
                                    targetDistBand > collisionDistBand;

                                // Zone classification for this pair
                                bool isTooClose = haveBand && distance < targetDistBand;
                                bool isInComfortOrJustOutside =
                                    haveBand &&
                                    distance >= targetDistBand &&
                                    (deadUpperBand <= 0f || distance <= deadUpperBand);

                                bool isFarForCohesion =
                                    haveBand &&
                                    (deadUpperBand > 0f
                                        ? distance > deadUpperBand
                                        : distance > targetDistBand) &&
                                    distance < farDistBand;

                                // --- Predictive radial damping (inside inner band) ---
                                if (myRadialDamping > 0f && haveBand && distance < targetDistBand) {
                                    float3 selfPrevVel = PrevVelocities[agentIndex];
                                    float3 otherPrevVel = PrevVelocities[neighbourIndex];

                                    // Relative radial velocity along line of centres: >0 = closing
                                    float vRel = math.dot(selfPrevVel - otherPrevVel, dirUnit);
                                    if (vRel > 0f) {
                                        float innerSpan = math.max(targetDistBand - collisionDistBand, 1e-3f);
                                        float proximity = math.saturate((targetDistBand - distance) / innerSpan); // 0..1
                                        float dampingStrength = myRadialDamping * proximity;

                                        float damping = vRel * dampingStrength;

                                        // Push against approach direction (acts like an extra steering term)
                                        radialDamping -= dirUnit * damping;
                                    }
                                }

                                // --- Leadership-weighted alignment / centroid ---
                                float neighbourLeaderWeight = BehaviourLeadershipWeight[neighbourBehaviourIndex];
                                float3 neighbourVelocity = PrevVelocities[neighbourIndex];

                                if (neighbourLeaderWeight > maxLeaderWeight + epsilon) {
                                    maxLeaderWeight = neighbourLeaderWeight;
                                    leaderNeighbourCount = 1;

                                    // Alignment always allowed (we still want heading coherence)
                                    alignment = neighbourVelocity * t;
                                    alignmentWeightSum = t;

                                    // Cohesion only allowed when neighbour is meaningfully "far"
                                    if (isFarForCohesion) {
                                        cohesion = neighbourPosition * t;
                                        cohesionWeightSum = t;
                                    } else {
                                        cohesion = float3.zero;
                                        cohesionWeightSum = 0f;
                                    }
                                } else if (math.abs(neighbourLeaderWeight - maxLeaderWeight) <= epsilon &&
                                           maxLeaderWeight > -1.0f) {
                                    leaderNeighbourCount += 1;

                                    alignment += neighbourVelocity * t;
                                    alignmentWeightSum += t;

                                    if (isFarForCohesion) {
                                        cohesion += neighbourPosition * t;
                                        cohesionWeightSum += t;
                                    }
                                }
                            }

                            // === AVOID: predator behaviour ===
                            if (isAvoid && myAvoidResponse > 0f) {
                                float neighbourAvoidWeight = BehaviourAvoidanceWeight[neighbourBehaviourIndex];

                                if (myAvoidWeight < neighbourAvoidWeight) {
                                    float weightDelta = neighbourAvoidWeight - myAvoidWeight;
                                    float normalised = weightDelta / math.max(neighbourAvoidWeight, 1e-3f);

                                    float localIntensity = t * normalised * myAvoidResponse;

                                    float3 repulse = -dirUnit * localIntensity;
                                    separation += repulse;
                                    avoidSeparation += repulse;
                                    separationCount += 1;

                                    if (localIntensity > avoidDanger) {
                                        avoidDanger = localIntensity;
                                    }
                                }
                            }

                            // === NEUTRAL: soft give-way to higher neutral weight ===
                            if (isNeutral) {
                                float neighbourNeutralWeight = BehaviourNeutralWeight[neighbourBehaviourIndex];

                                if (myNeutralWeight < neighbourNeutralWeight) {
                                    float weightDelta = neighbourNeutralWeight - myNeutralWeight;
                                    float normalised = weightDelta / math.max(neighbourNeutralWeight, 1e-3f);

                                    float3 softRepulse = -dirUnit * (t * normalised * 0.5f);
                                    separation += softRepulse;
                                    separationCount += 1;
                                }
                            }
                        }
                    }
                }
            }
        }

        // UPDATED: keep separation magnitude instead of normalizing away penetration depth
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

                // Keep magnitude → deeper overlaps / more neighbours = stronger push
                float3 separationAvg = separation * invSep;

                float3 separationForce = separationAvg * separationWeight * separationPanicMultiplier;
                steering += separationForce;
            }

            return steering;
        }

        float3 ComputePropulsion(
            float3 currentVelocity,
            float desiredSpeed) {

            // No target speed → no self-propulsion.
            if (desiredSpeed <= 0.0f) {
                return float3.zero;
            }

            float speedSq = math.lengthsq(currentVelocity);

            // If we don't really have a heading, don't invent a global one (like +Z).
            // Let flocking / attraction / walls decide where to go instead.
            if (speedSq < 1e-6f) {
                return float3.zero;
            }

            float currentSpeed = math.sqrt(speedSq);
            float3 direction = currentVelocity / currentSpeed;

            float speedError = desiredSpeed - currentSpeed;
            if (math.abs(speedError) < 1e-3f) {
                return float3.zero;
            }

            // Accelerate strictly along current heading.
            return direction * speedError;
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
