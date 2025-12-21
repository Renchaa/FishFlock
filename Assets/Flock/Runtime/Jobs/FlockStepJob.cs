// File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct FlockStepJob : IJobParallelFor {

        // Bounds probe outputs
        [ReadOnly] public NativeArray<float3> WallDirections;
        [ReadOnly] public NativeArray<float> WallDangers;

        // Per-behaviour bounds response (always allocated; disable via weights)
        [ReadOnly] public NativeArray<float> BehaviourBoundsWeight;              // radial push strength
        [ReadOnly] public NativeArray<float> BehaviourBoundsTangentialDamping;   // kill sliding
        [ReadOnly] public NativeArray<float> BehaviourBoundsInfluenceSuppression; // how much to mute flocking near walls

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

        [ReadOnly] public NativeArray<int> CellAgentStarts;
        [ReadOnly] public NativeArray<int> CellAgentCounts;
        [ReadOnly] public NativeArray<Flock.Runtime.Jobs.CellAgentPair> CellAgentPairs;
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
        // NEW: how strongly min/max group constraints are enforced
        [ReadOnly] public NativeArray<float> BehaviourMinGroupSizeWeight;
        [ReadOnly] public NativeArray<float> BehaviourMaxGroupSizeWeight;

        // NEW: schooling band params per type
        [ReadOnly] public NativeArray<float> BehaviourSchoolSpacingFactor;
        [ReadOnly] public NativeArray<float> BehaviourSchoolOuterFactor;
        [ReadOnly] public NativeArray<float> BehaviourSchoolStrength;
        [ReadOnly] public NativeArray<float> BehaviourSchoolInnerSoftness;
        [ReadOnly] public NativeArray<float> BehaviourSchoolDeadzoneFraction;

        [ReadOnly] public NativeArray<byte> BehaviourUsePreferredDepth;
        [ReadOnly] public NativeArray<float> BehaviourDepthBiasStrength;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMinNorm;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthMaxNorm;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthWeight;
        [ReadOnly] public NativeArray<float> BehaviourSchoolRadialDamping;

        [ReadOnly] public NativeArray<float> BehaviourWanderStrength;
        [ReadOnly] public NativeArray<float> BehaviourWanderFrequency;
        [ReadOnly] public NativeArray<float> BehaviourGroupNoiseStrength;
        [ReadOnly] public NativeArray<float> BehaviourPatternWeight;

        [ReadOnly] public NativeArray<float3> PatternSteering;
        [ReadOnly] public NativeArray<float3> CellGroupNoise;

        [ReadOnly] public float NoiseTime;
        [ReadOnly] public float GlobalWanderMultiplier;
        [ReadOnly] public float GlobalGroupNoiseMultiplier;
        [ReadOnly] public float GlobalPatternMultiplier;

        [ReadOnly] public bool UseAttraction;
        [ReadOnly] public float GlobalAttractionWeight;
        [ReadOnly] public NativeArray<float3> AttractionSteering;
        [ReadOnly] public NativeArray<float> BehaviourPreferredDepthEdgeFraction;

        // NEW: how fast dir changes, how much it hits speed
        [ReadOnly] public NativeArray<float> BehaviourGroupNoiseDirectionRate;
        [ReadOnly] public NativeArray<float> BehaviourGroupNoiseSpeedWeight;

        [ReadOnly] public NativeArray<int> BehaviourMaxNeighbourChecks;
        [ReadOnly] public NativeArray<int> BehaviourMaxFriendlySamples;
        [ReadOnly] public NativeArray<int> BehaviourMaxSeparationSamples;

        static uint Hash32(uint x) {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

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

            float boundsWeight = math.max(0f, BehaviourBoundsWeight[behaviourIndex]);
            float boundsTangentialDamping = math.max(0f, BehaviourBoundsTangentialDamping[behaviourIndex]);
            float boundsInfluenceSuppression = math.max(0f, BehaviourBoundsInfluenceSuppression[behaviourIndex]);

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

            // Per-agent (per Execute) dedup state: prevents double-processing big fish
            FixedList512Bytes<int> visited = default;
            ulong seen0 = 0ul, seen1 = 0ul, seen2 = 0ul, seen3 = 0ul;

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
                radialDamping: ref radialDamping,
                visited: ref visited,
                seen0: ref seen0,
                seen1: ref seen1,
                seen2: ref seen2,
                seen3: ref seen3);
            // NEW

            // --- 3) Group size logic (loners / overcrowding) ---
            // Arrays are always allocated & sized per-behaviour; no IsCreated/Length guards here.
            int groupSize = friendlyNeighbourCount + 1; // me + friendly neighbours

            int minGroupSize = math.max(1, BehaviourMinGroupSize[behaviourIndex]);
            int maxGroupSize = BehaviourMaxGroupSize[behaviourIndex];
            if (maxGroupSize < minGroupSize) {
                maxGroupSize = minGroupSize;
            }

            float lonerCohesionBoost = BehaviourLonerCohesionBoost[behaviourIndex];
            float groupRadiusMultiplier = math.max(1f, BehaviourGroupRadiusMultiplier[behaviourIndex]);
            float lonerRadiusMultiplier = math.max(1f, BehaviourLonerRadiusMultiplier[behaviourIndex]);

            float minGroupWeight = math.max(0f, BehaviourMinGroupSizeWeight[behaviourIndex]);
            float maxGroupWeight = math.max(0f, BehaviourMaxGroupSizeWeight[behaviourIndex]);

            bool hasFriends = friendlyNeighbourCount > 0;
            bool isLoner = groupSize < minGroupSize;
            bool isOvercrowded = maxGroupSize > 0 && groupSize > maxGroupSize;

            if (isLoner && hasFriends) {
                float lonerFactor = math.max(0f, lonerCohesionBoost);
                lonerFactor *= lonerRadiusMultiplier;
                lonerFactor *= minGroupWeight;
                cohesionWeight *= (1.0f + lonerFactor);
            }

            if (isOvercrowded && hasFriends) {
                float crowdFactor = (groupSize - maxGroupSize) / math.max(1f, (float)maxGroupSize);
                crowdFactor = math.saturate(crowdFactor);

                crowdFactor *= groupRadiusMultiplier;
                crowdFactor *= maxGroupWeight;

                separationWeight *= (1.0f + crowdFactor);
                cohesionWeight *= (1.0f - 0.5f * crowdFactor);
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

            float splitPanicThreshold = BehaviourSplitPanicThreshold[behaviourIndex];
            float splitLateralWeight = BehaviourSplitLateralWeight[behaviourIndex];
            float splitAccelBoost = BehaviourSplitAccelBoost[behaviourIndex];

            int minSplitGroup = math.max(3, minGroupSize);

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
            if (UseObstacleAvoidance) {
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

            // NEW: 8.1) Micro wander (per-fish), group noise, pattern
            steering += ComputeWanderNoise(
                agentIndex: index,
                behaviourIndex: behaviourIndex,
                currentVelocity: velocity,
                maxAcceleration: localMaxAcceleration,
                time: NoiseTime);

            steering += ComputeGroupNoise(
                agentIndex: index,
                behaviourIndex: behaviourIndex,
                position: position,
                currentVelocity: velocity,
                maxAcceleration: localMaxAcceleration);

            steering += ComputePatternSteering(
                index,
                behaviourIndex,
                localMaxAcceleration);

            // --- 8.5) Bounds: gate non-wall steering + add radial push ---
            steering = ApplyBoundsSteering(
                agentIndex: index,
                steering: steering,
                maxAcceleration: localMaxAcceleration,
                boundsWeight: boundsWeight,
                boundsInfluenceSuppression: boundsInfluenceSuppression);

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
                agentIndex: index,
                velocity: velocity,
                boundsTangentialDamping: boundsTangentialDamping);

            velocity = LimitVector(velocity, localMaxSpeed);

            Velocities[index] = velocity;
        }

        static bool IsNeighbourVisitedOrMark(
            int neighbourIndex,
            ref FixedList512Bytes<int> visited,
            ref ulong seen0,
            ref ulong seen1,
            ref ulong seen2,
            ref ulong seen3) {
            uint h = Hash32((uint)neighbourIndex);
            int bitIndex = (int)(h & 255u);     // 0..255
            int word = bitIndex >> 6;           // 0..3
            ulong mask = 1ul << (bitIndex & 63);

            bool maybeSeen;
            if (word == 0) maybeSeen = (seen0 & mask) != 0;
            else if (word == 1) maybeSeen = (seen1 & mask) != 0;
            else if (word == 2) maybeSeen = (seen2 & mask) != 0;
            else maybeSeen = (seen3 & mask) != 0;

            // Fast path: definitely not seen (filter says no)
            if (!maybeSeen) {
                if (word == 0) seen0 |= mask;
                else if (word == 1) seen1 |= mask;
                else if (word == 2) seen2 |= mask;
                else seen3 |= mask;

                if (visited.Length < visited.Capacity) {
                    visited.Add(neighbourIndex);
                }
                // If overflow: fail-open (may process duplicates, but never miss neighbours)
                return false;
            }

            // Confirm in exact list (prevents hash collision causing "false visited")
            for (int i = 0; i < visited.Length; i++) {
                if (visited[i] == neighbourIndex) {
                    return true;
                }
            }

            // Hash collision (or not recorded earlier) -> treat as new
            if (visited.Length < visited.Capacity) {
                visited.Add(neighbourIndex);
            }

            return false;
        }

        // ======================================================
        // 5) FlockStepJob – updated ComputeSchoolingBandForce
        //      now also exposes thresholds for zone gating
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // ======================================================

        // REPLACE METHOD: ComputeSchoolingBandForce (full)

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

            float rA = BehaviourBodyRadius[myBehaviourIndex];
            float rB = BehaviourBodyRadius[neighbourBehaviourIndex];

            if (rA <= 0f && rB <= 0f) {
                return float3.zero;
            }

            float spacingA = BehaviourSchoolSpacingFactor[myBehaviourIndex];
            float spacingB = BehaviourSchoolSpacingFactor[neighbourBehaviourIndex];

            float outerA = BehaviourSchoolOuterFactor[myBehaviourIndex];
            float outerB = BehaviourSchoolOuterFactor[neighbourBehaviourIndex];

            float strA = BehaviourSchoolStrength[myBehaviourIndex];
            float strB = BehaviourSchoolStrength[neighbourBehaviourIndex];

            float softnessA = BehaviourSchoolInnerSoftness[myBehaviourIndex];
            float softnessB = BehaviourSchoolInnerSoftness[neighbourBehaviourIndex];

            float deadA = BehaviourSchoolDeadzoneFraction[myBehaviourIndex];
            float deadB = BehaviourSchoolDeadzoneFraction[neighbourBehaviourIndex];

            float spacing = math.max(0.5f, 0.5f * (spacingA + spacingB));
            float outer = math.max(1f, 0.5f * (outerA + outerB));
            float strength = math.max(0f, 0.5f * (strA + strB));
            if (strength <= 0f) {
                return float3.zero;
            }

            float softness = math.clamp(0.5f * (softnessA + softnessB), 0f, 1f);
            float deadFrac = math.clamp(0.5f * (deadA + deadB), 0f, 0.5f);

            collisionDist = rA + rB;
            targetDist = collisionDist * spacing;
            farDist = targetDist * outer;

            if (distance <= 0f || distance >= farDist) {
                return float3.zero;
            }

            float deadRadius = deadFrac * targetDist;
            float deadLower = targetDist;
            deadZoneUpper = targetDist + deadRadius;

            float force;

            if (distance < collisionDist) {
                float t = (collisionDist - distance) / math.max(collisionDist, 1e-3f);
                force = t; // repulsive
            } else if (distance < targetDist) {
                float innerSpan = math.max(targetDist - collisionDist, 1e-3f);
                float t = (targetDist - distance) / innerSpan; // 0..1

                // ---- NO POW: blend between t, t^2, t^3 based on softness (0..1) ----
                float t2 = t * t;
                float shaped;

                if (softness <= 0f) {
                    shaped = t;
                } else {
                    float t3 = t2 * t;
                    if (softness <= 0.5f) {
                        float u = softness * 2f;           // 0..1
                        shaped = math.lerp(t, t2, u);      // t -> t^2
                    } else {
                        float u = (softness - 0.5f) * 2f;  // 0..1
                        shaped = math.lerp(t2, t3, u);     // t^2 -> t^3
                    }
                }

                force = shaped; // repulsive
            } else {
                if (distance >= deadLower && distance <= deadZoneUpper) {
                    return float3.zero;
                }

                float attractStart = deadZoneUpper;
                float attractSpan = math.max(farDist - attractStart, 1e-3f);
                float t = (distance - attractStart) / attractSpan; // 0..1

                float falloff = 1f - t;
                force = -falloff; // attractive
            }

            if (math.abs(force) <= 1e-5f) {
                return float3.zero;
            }

            return -dir * (force * strength);
        }

        // =======================================================
        // 10) FlockStepJob – HELPERS: hash + noise layers
        // Place these near your other helpers (e.g. under ComputePropulsion)
        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // =======================================================

        static float Hash01(uint seed) {
            seed ^= seed >> 17;
            seed *= 0xED5AD4BBu;
            seed ^= seed >> 11;
            seed *= 0xAC4C1B51u;
            seed ^= seed >> 15;
            seed *= 0x31848BABu;
            seed ^= seed >> 14;
            // 24-bit mantissa to [0,1)
            return (seed >> 8) * (1.0f / 16777216.0f);
        }

        // --------- Micro wander (per-fish, low amplitude) ----------
        float3 ComputeWanderNoise(
            int agentIndex,
            int behaviourIndex,
            float3 currentVelocity,
            float maxAcceleration,
            float time) {

            float strength = BehaviourWanderStrength[behaviourIndex] * GlobalWanderMultiplier;
            if (strength <= 0f || maxAcceleration <= 0f) {
                return float3.zero;
            }

            float frequency = math.max(0f, BehaviourWanderFrequency[behaviourIndex]);
            float t = time * frequency;

            uint baseSeed = (uint)(agentIndex * 0x9E3779B1u + 0x85EBCA6Bu);
            float phaseX = Hash01(baseSeed ^ 0xA2C2A1EDu) * 6.2831853f;
            float phaseY = Hash01(baseSeed ^ 0x27D4EB2Fu) * 6.2831853f;
            float phaseZ = Hash01(baseSeed ^ 0x165667B1u) * 6.2831853f;

            float3 dir = new float3(
                math.sin(t + phaseX),
                math.sin(t * 1.37f + phaseY),
                math.sin(t * 1.79f + phaseZ));

            dir = math.normalizesafe(dir, float3.zero);
            if (math.lengthsq(dir) < 1e-6f) {
                return float3.zero;
            }

            float3 forward = math.normalizesafe(currentVelocity, dir);
            float3 side = math.normalizesafe(math.cross(forward, new float3(0, 1, 0)), dir);
            float3 up = math.cross(forward, side);

            float3 wanderDir = math.normalizesafe(
                forward * 0.7f + side * 0.2f + up * 0.1f,
                forward);

            float maxWanderAccel = maxAcceleration * strength;
            return wanderDir * maxWanderAccel;
        }
    
        // --------- Group noise (per-cell, shared by neighbours) ----------
        float3 ComputeGroupNoise(
            int agentIndex,
            int behaviourIndex,
            float3 position,
            float3 currentVelocity,
            float maxAcceleration) {

            float baseStrength = BehaviourGroupNoiseStrength[behaviourIndex] * GlobalGroupNoiseMultiplier;
            if (baseStrength <= 0f || maxAcceleration <= 0f) {
                return float3.zero;
            }

            float directionRate = math.max(0f, BehaviourGroupNoiseDirectionRate[behaviourIndex]);
            float speedWeight = math.saturate(BehaviourGroupNoiseSpeedWeight[behaviourIndex]);

            int3 cell = GetCell(position);
            int cellId = GetCellId(cell);
            if ((uint)cellId >= (uint)CellGroupNoise.Length) {
                return float3.zero;
            }

            float3 noiseDir = CellGroupNoise[cellId];
            if (math.lengthsq(noiseDir) < 1e-6f) {
                return float3.zero;
            }

            noiseDir = math.normalizesafe(noiseDir, float3.zero);
            if (math.lengthsq(noiseDir) < 1e-6f) {
                return float3.zero;
            }

            float3 forward = math.normalizesafe(currentVelocity, noiseDir);

            float proj = math.dot(noiseDir, forward);
            float3 along = forward * proj;
            float3 lateral = noiseDir - along;

            float lateralLenSq = math.lengthsq(lateral);
            float3 lateralDir = lateralLenSq > 1e-8f
                ? lateral * math.rsqrt(lateralLenSq)
                : float3.zero;

            float strength = baseStrength * directionRate;
            if (strength <= 0f) {
                return float3.zero;
            }

            float maxNoiseAccel = maxAcceleration * strength;

            float wSpeed = speedWeight;
            float lateralAccelMag = maxNoiseAccel * (1f - wSpeed);
            float speedAccelMag = maxNoiseAccel * wSpeed;

            float3 result = float3.zero;

            if (math.lengthsq(lateralDir) > 1e-8f && lateralAccelMag > 0f) {
                result += lateralDir * lateralAccelMag;
            }

            if (speedAccelMag > 0f && math.lengthsq(forward) > 1e-8f) {
                result += forward * (speedAccelMag * proj);
            }

            return result;
        }

        // --------- Pattern driver (external steering field) ----------
        float3 ComputePatternSteering(
            int agentIndex,
            int behaviourIndex,
            float maxAcceleration) {

            float weight = BehaviourPatternWeight[behaviourIndex] * GlobalPatternMultiplier;
            if (weight <= 0f || maxAcceleration <= 0f) {
                return float3.zero;
            }

            float3 pattern = PatternSteering[agentIndex];
            if (math.lengthsq(pattern) < 1e-8f) {
                return float3.zero;
            }

            float3 dir = math.normalizesafe(pattern, float3.zero);
            if (math.lengthsq(dir) < 1e-8f) {
                return float3.zero;
            }

            return dir * maxAcceleration * weight;
        }

        float3 ComputeAttraction(int agentIndex) {
            if (!UseAttraction) {
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
            // Arrays are always allocated per behaviour. Disable via UsePreferredDepth/weights.
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
                float edgeFrac = math.clamp(BehaviourPreferredDepthEdgeFraction[behaviourIndex], 0.01f, 0.49f);

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
            float3 steering,
            float maxAcceleration,
            float boundsWeight,
            float boundsInfluenceSuppression) {

            float danger = WallDangers[agentIndex];
            if (danger <= 0f) {
                return steering;
            }

            float3 wallDir = WallDirections[agentIndex];
            if (math.lengthsq(wallDir) < 1e-8f) {
                return steering;
            }

            float gate = 1f - danger * boundsInfluenceSuppression;
            gate = math.saturate(gate);
            steering *= gate;

            float3 n = math.normalizesafe(wallDir, float3.zero);
            float radialAccel = danger * boundsWeight * maxAcceleration;
            steering += n * radialAccel;

            return steering;
        }

        float3 ApplyBoundsVelocity(
           int agentIndex,
           float3 velocity,
           float boundsTangentialDamping) {

            float danger = WallDangers[agentIndex];
            if (danger <= 0f || boundsTangentialDamping <= 0f) {
                return velocity;
            }

            float3 wallDir = WallDirections[agentIndex];
            if (math.lengthsq(wallDir) < 1e-8f) {
                return velocity;
            }

            float3 n = math.normalizesafe(wallDir, float3.zero);

            float vRadial = math.dot(velocity, n);
            float3 vRad = n * vRadial;
            float3 vTan = velocity - vRad;

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

            groupFlowWeight = BehaviourGroupFlowWeight[behaviourIndex];

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
            ref float3 radialDamping,
            ref FixedList512Bytes<int> visited,
            ref ulong seen0,
            ref ulong seen1,
            ref ulong seen2,
            ref ulong seen3) {

            float neighbourRadiusSquared = neighbourRadius * neighbourRadius;
            float separationRadiusSquared = separationRadius * separationRadius;

            float myAvoidWeight = BehaviourAvoidanceWeight[myBehaviourIndex];
            float myNeutralWeight = BehaviourNeutralWeight[myBehaviourIndex];
            float myAvoidResponse = math.max(0f, BehaviourAvoidResponse[myBehaviourIndex]);

            // Per-type radial damping strength for this agent
            float myRadialDamping = BehaviourSchoolRadialDamping[myBehaviourIndex];
            float3 selfPrevVel = PrevVelocities[agentIndex];

            int3 baseCell = GetCell(agentPosition);

            // Leadership state
            float maxLeaderWeight = -1.0f;
            const float epsilon = 1e-4f;

            avoidDanger = 0.0f;
            friendlyNeighbourCount = 0;
            avoidSeparation = float3.zero;

            // Per-type cell search radius
            int cellRange = math.max(BehaviourCellSearchRadius[myBehaviourIndex], 1);

            // -------------------------
            // NEW: caps (0 = unlimited)
            // -------------------------
            int maxChecks = math.max(0, BehaviourMaxNeighbourChecks[myBehaviourIndex]);
            int maxFriendlySamples = math.max(0, BehaviourMaxFriendlySamples[myBehaviourIndex]);
            int maxSeparationSamples = math.max(0, BehaviourMaxSeparationSamples[myBehaviourIndex]);

            int neighbourChecks = 0;

            // Counts only the number of “tie samples” taken at current maxLeaderWeight.
            // If a higher leader is found, this resets to 1 (the new leader sample).
            int leaderTieSamples = 0;

            bool stopAll = false;

            for (int x = -cellRange; x <= cellRange; x++) {
                if (stopAll) break;

                for (int y = -cellRange; y <= cellRange; y++) {
                    if (stopAll) break;

                    for (int z = -cellRange; z <= cellRange; z++) {
                        if (stopAll) break;

                        int3 neighbourCell = baseCell + new int3(x, y, z);

                        if (!IsCellInsideGrid(neighbourCell))
                            continue;

                        int cellId = GetCellId(neighbourCell);

                        int countInCell = CellAgentCounts[cellId];
                        if (countInCell <= 0) {
                            continue;
                        }

                        int startInCell = CellAgentStarts[cellId];
                        for (int p = 0; p < countInCell; p += 1) {
                            int neighbourIndex = CellAgentPairs[startInCell + p].AgentIndex;
                            if (neighbourIndex == agentIndex)
                                continue;

                            // Dedup neighbour if it appears in multiple cells
                            if (IsNeighbourVisitedOrMark(neighbourIndex, ref visited, ref seen0, ref seen1, ref seen2, ref seen3))
                                continue;

                            // NEW: max unique neighbour checks per frame
                            if (maxChecks > 0 && neighbourChecks >= maxChecks) {
                                stopAll = true;
                                break;
                            }
                            neighbourChecks += 1;

                            float3 neighbourPosition = Positions[neighbourIndex];
                            float3 offset = neighbourPosition - agentPosition;
                            float distanceSquared = math.lengthsq(offset);
                            if (distanceSquared < 1e-6f) {
                                continue;
                            }

                            int neighbourBehaviourIndex = BehaviourIds[neighbourIndex];
                            if ((uint)neighbourBehaviourIndex >= (uint)BehaviourMaxSpeed.Length) {
                                continue;
                            }

                            // --- HARD GEOMETRIC SEPARATION radius^2 (no sqrt yet) ---
                            float hardRadiusSq = separationRadiusSquared;

                            float myBody = BehaviourBodyRadius[myBehaviourIndex];
                            float nbBody = BehaviourBodyRadius[neighbourBehaviourIndex];

                            float collisionDistBody = myBody + nbBody;
                            if (collisionDistBody > 0f) {
                                float collisionDistSq = collisionDistBody * collisionDistBody;
                                if (collisionDistSq > hardRadiusSq) {
                                    hardRadiusSq = collisionDistSq;
                                }
                            }

                            bool withinHard = hardRadiusSq > 0f && distanceSquared < hardRadiusSq;
                            bool withinNeighbour = distanceSquared <= neighbourRadiusSquared;

                            // If neither rule applies, skip without ever computing sqrt/dir
                            if (!withinHard && !withinNeighbour) {
                                continue;
                            }

                            // Compute direction + distance using rsqrt pipeline
                            float invDist = math.rsqrt(distanceSquared);
                            float distance = distanceSquared * invDist;   // == sqrt(distanceSquared)
                            float3 dirUnit = offset * invDist;            // normalized

                            // --- HARD separation (only if budget allows) ---
                            if (withinHard) {
                                bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                if (canAddSep) {
                                    float hardInv = math.rsqrt(math.max(hardRadiusSq, 1e-12f));
                                    float hardRadius = hardRadiusSq * hardInv;

                                    float penetration = hardRadius - distance;
                                    float strengthPen = penetration / math.max(hardRadius, 1e-3f);

                                    separation -= dirUnit * (1f + strengthPen);
                                    separationCount += 1;
                                }
                            }

                            // Beyond neighbour radius → no flock rules (but hard separation above may have applied)
                            if (!withinNeighbour || neighbourRadius <= 1e-6f) {
                                continue;
                            }

                            uint bit = neighbourBehaviourIndex < 32 ? (1u << neighbourBehaviourIndex) : 0u;

                            bool isFriendly = bit != 0u && (friendlyMask & bit) != 0u;
                            bool isAvoid = bit != 0u && (avoidMask & bit) != 0u;
                            bool isNeutral = bit != 0u && (neutralMask & bit) != 0u;

                            if (!isFriendly && !isAvoid && !isNeutral) {
                                continue;
                            }

                            float invNeighbourRadius = 1.0f / neighbourRadius;
                            float t = 1.0f - math.saturate(distance * invNeighbourRadius);

                            // === FRIENDLY: schooling distance band + zone-gated cohesion ===
                            if (isFriendly) {
                                friendlyNeighbourCount += 1;

                                // Decide if we should consider this neighbour for leader alignment/cohesion.
                                float neighbourLeaderWeight = BehaviourLeadershipWeight[neighbourBehaviourIndex];
                                bool isLeaderUpgrade = neighbourLeaderWeight > maxLeaderWeight + epsilon;

                                bool isLeaderTie =
                                    math.abs(neighbourLeaderWeight - maxLeaderWeight) <= epsilon
                                    && maxLeaderWeight > -1.0f;

                                bool canTakeTieSample =
                                    isLeaderTie && (maxFriendlySamples == 0 || leaderTieSamples < maxFriendlySamples);

                                bool considerLeadership = isLeaderUpgrade || canTakeTieSample;

                                // We need band thresholds if we want:
                                // - band separation (if sep budget allows)
                                // - radial damping
                                // - cohesion gating for leadership samples
                                bool needBand =
                                    myRadialDamping > 0f
                                    || considerLeadership
                                    || (maxSeparationSamples == 0 || separationCount < maxSeparationSamples);

                                float collisionDistBand = 0f;
                                float targetDistBand = 0f;
                                float deadUpperBand = 0f;
                                float farDistBand = 0f;

                                float3 bandForce = float3.zero;

                                if (needBand) {
                                    bandForce = ComputeSchoolingBandForce(
                                        myBehaviourIndex,
                                        neighbourBehaviourIndex,
                                        distance,
                                        dirUnit,
                                        out collisionDistBand,
                                        out targetDistBand,
                                        out deadUpperBand,
                                        out farDistBand);

                                    // Apply band as separation only if separation budget allows
                                    if (math.lengthsq(bandForce) > 0f) {
                                        bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                        if (canAddSep) {
                                            separation += bandForce;
                                            separationCount += 1;
                                        }
                                    }
                                }

                                bool haveBand =
                                    farDistBand > 0f &&
                                    collisionDistBand > 0f &&
                                    targetDistBand > collisionDistBand;

                                bool isFarForCohesion =
                                    haveBand &&
                                    (deadUpperBand > 0f
                                        ? distance > deadUpperBand
                                        : distance > targetDistBand) &&
                                    distance < farDistBand;

                                // --- Predictive radial damping (inside inner band) ---
                                if (myRadialDamping > 0f && haveBand && distance < targetDistBand) {
                                    float3 otherPrevVel = PrevVelocities[neighbourIndex];
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

                                // --- Leadership-weighted alignment / centroid (capped) ---
                                if (considerLeadership) {
                                    float3 neighbourVelocity = PrevVelocities[neighbourIndex];

                                    if (isLeaderUpgrade) {
                                        maxLeaderWeight = neighbourLeaderWeight;
                                        leaderNeighbourCount = 1;

                                        alignment = neighbourVelocity * t;
                                        alignmentWeightSum = t;

                                        if (isFarForCohesion) {
                                            cohesion = neighbourPosition * t;
                                            cohesionWeightSum = t;
                                        } else {
                                            cohesion = float3.zero;
                                            cohesionWeightSum = 0f;
                                        }

                                        // Reset tie sample count for the new leader group
                                        leaderTieSamples = 1;
                                    } else if (canTakeTieSample) {
                                        leaderNeighbourCount += 1;

                                        alignment += neighbourVelocity * t;
                                        alignmentWeightSum += t;

                                        if (isFarForCohesion) {
                                            cohesion += neighbourPosition * t;
                                            cohesionWeightSum += t;
                                        }

                                        leaderTieSamples += 1;
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

                                    // Always track danger even if separation budget is exhausted
                                    if (localIntensity > avoidDanger) {
                                        avoidDanger = localIntensity;
                                    }

                                    bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                    if (canAddSep) {
                                        float3 repulse = -dirUnit * localIntensity;
                                        separation += repulse;
                                        avoidSeparation += repulse;
                                        separationCount += 1;
                                    }
                                }
                            }

                            // === NEUTRAL: soft give-way to higher neutral weight ===
                            if (isNeutral) {
                                float neighbourNeutralWeight = BehaviourNeutralWeight[neighbourBehaviourIndex];

                                if (myNeutralWeight < neighbourNeutralWeight) {
                                    float weightDelta = neighbourNeutralWeight - myNeutralWeight;
                                    float normalised = weightDelta / math.max(neighbourNeutralWeight, 1e-3f);

                                    bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                    if (canAddSep) {
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

            // Match your group-flow logic: if desiredSpeed is not configured, preserve current speed.
            float currentSpeed = math.length(currentVelocity);
            float targetSpeed = desiredSpeed > 0f ? desiredSpeed : currentSpeed;

            if (neighbourCount > 0 && targetSpeed > 1e-4f) {

                // Alignment ONLY if we actually accumulated something
                if (alignmentWeightSum > 1e-6f) {
                    float invAlign = 1.0f / alignmentWeightSum;

                    float3 alignmentDir = alignment * invAlign;
                    float3 alignmentNorm = math.normalizesafe(alignmentDir, currentVelocity);

                    float3 desiredAlignVel = alignmentNorm * targetSpeed;
                    float3 alignmentForce = (desiredAlignVel - currentVelocity) * alignmentWeight;

                    steering += alignmentForce;
                }

                // Cohesion ONLY if we have a valid centroid sum
                if (cohesionWeightSum > 1e-6f) {
                    float invCoh = 1.0f / cohesionWeightSum;

                    float3 cohesionCenter = cohesion * invCoh;
                    float3 toCenter = cohesionCenter - currentPosition;

                    float3 cohesionDir = math.normalizesafe(toCenter, float3.zero);

                    float3 desiredCohesionVel = cohesionDir * targetSpeed;
                    float3 cohesionForce = (desiredCohesionVel - currentVelocity) * cohesionWeight;

                    steering += cohesionForce;
                }
            }

            if (separationCount > 0) {
                float invSep = 1.0f / separationCount;

                // This is "average separation". Stable. If you want crowding to scale harder with neighbour count,
                // remove invSep and use raw sum (it will still be clamped by maxAcceleration later).
                float3 separationAvg = separation * invSep;

                float3 separationForce = separationAvg * separationWeight * separationPanicMultiplier;
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

            float speedSq = math.lengthsq(currentVelocity);
            if (speedSq < 1e-6f) {
                return float3.zero;
            }

            float invSpeed = math.rsqrt(speedSq);
            float currentSpeed = speedSq * invSpeed;
            float3 direction = currentVelocity * invSpeed;

            float speedError = desiredSpeed - currentSpeed;
            if (math.abs(speedError) < 1e-3f) {
                return float3.zero;
            }

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
            if (maxLength <= 0f) {
                return float3.zero;
            }

            float lengthSquared = math.lengthsq(value);
            if (lengthSquared <= 0.0f) {
                return value;
            }

            float maxLengthSquared = maxLength * maxLength;
            if (lengthSquared <= maxLengthSquared) {
                return value;
            }

            // scale = maxLength / sqrt(lenSq) => maxLength * rsqrt(lenSq)
            float invLen = math.rsqrt(lengthSquared);
            return value * (maxLength * invLen);
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
