// File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct FlockStepJob : IJobParallelFor {

        // File: Assets/Flock/Runtime/Jobs/FlockStepJob.cs
        // REPLACE ALL FIELDS ABOVE Hash32(...) WITH THIS SET:

        // Bounds probe outputs (per-agent)
        [ReadOnly] public NativeArray<float3> WallDirections;
        [ReadOnly] public NativeArray<float> WallDangers;

        // Core agent data
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> PrevVelocities;
        [NativeDisableParallelForRestriction] public NativeArray<float3> Velocities;

        // Behaviour
        [ReadOnly] public NativeArray<int> BehaviourIds;

        // Phase 1: per-behaviour parameter block (single source of truth)
        [ReadOnly] public NativeArray<FlockBehaviourSettings> BehaviourSettings;

        // Derived per-behaviour runtime param (kept separate on purpose)
        [ReadOnly] public NativeArray<int> BehaviourCellSearchRadius;

        // Spatial grid
        [ReadOnly] public NativeArray<int> CellAgentStarts;
        [ReadOnly] public NativeArray<int> CellAgentCounts;
        [ReadOnly] public NativeArray<CellAgentPair> CellAgentPairs;

        [ReadOnly] public float3 GridOrigin;
        [ReadOnly] public int3 GridResolution;
        [ReadOnly] public float CellSize;

        // Environment + timestep
        [ReadOnly] public FlockEnvironmentData EnvironmentData;
        [ReadOnly] public float DeltaTime;

        // Obstacles
        [ReadOnly] public bool UseObstacleAvoidance;
        [ReadOnly] public float ObstacleAvoidWeight;
        [ReadOnly] public NativeArray<float3> ObstacleSteering;

        // Attraction
        [ReadOnly] public bool UseAttraction;
        [ReadOnly] public float GlobalAttractionWeight;
        [ReadOnly] public NativeArray<float3> AttractionSteering;

        // Noise + pattern
        [ReadOnly] public NativeArray<float3> PatternSteering;
        [ReadOnly] public NativeArray<float3> CellGroupNoise;

        [ReadOnly] public float NoiseTime;
        [ReadOnly] public float GlobalWanderMultiplier;
        [ReadOnly] public float GlobalGroupNoiseMultiplier;
        [ReadOnly] public float GlobalPatternMultiplier;

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
            if (!TryLoadBehaviour(
                    index,
                    out float3 position,
                    out float3 velocity,
                    out int behaviourIndex,
                    out FlockBehaviourSettings b)) {

                // Behaviour index out of range – keep previous velocity.
                Velocities[index] = PrevVelocities[index];
                return;
            }

            float maxSpeed = b.MaxSpeed;
            float maxAcceleration = b.MaxAcceleration;

            float desiredSpeed = b.DesiredSpeed;
            float neighbourRadius = b.NeighbourRadius;
            float separationRadius = b.SeparationRadius;

            float alignmentWeight = b.AlignmentWeight;
            float cohesionWeight = b.CohesionWeight;
            float separationWeight = b.SeparationWeight;

            float influenceWeight = b.InfluenceWeight;
            float groupFlowWeight = b.GroupFlowWeight;

            uint friendlyMask = b.GroupMask;
            uint avoidMask = b.AvoidMask;
            uint neutralMask = b.NeutralMask;

            float boundsWeight = math.max(0f, b.BoundsWeight);
            float boundsTangentialDamping = math.max(0f, b.BoundsTangentialDamping);
            float boundsInfluenceSuppression = math.max(0f, b.BoundsInfluenceSuppression);

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

            // Predictive damping accumulator
            float3 radialDamping = float3.zero;

            // Per-agent dedup state
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

            // --- 3) Group size logic (loners / overcrowding) ---
            int groupSize = friendlyNeighbourCount + 1; // me + friendly neighbours

            int minGroupSize = math.max(1, b.MinGroupSize);
            int maxGroupSize = b.MaxGroupSize;
            if (maxGroupSize < minGroupSize) {
                maxGroupSize = minGroupSize;
            }

            float lonerCohesionBoost = b.LonerCohesionBoost;
            float groupRadiusMultiplier = math.max(1f, b.GroupRadiusMultiplier);
            float lonerRadiusMultiplier = math.max(1f, b.LonerRadiusMultiplier);

            float minGroupWeight = math.max(0f, b.MinGroupSizeWeight);
            float maxGroupWeight = math.max(0f, b.MaxGroupSizeWeight);

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

            // Predictive radial braking
            steering += radialDamping;

            // --- Group-flow steering ---
            float localGroupFlowWeight = groupFlowWeight;

            if (localGroupFlowWeight > 0f
                && leaderNeighbourCount > 0
                && alignmentWeightSum > 1e-6f) {

                float3 groupDirRaw = alignment / alignmentWeightSum;
                float3 groupDir = math.normalizesafe(groupDirRaw, velocity);

                float currentSpeed = math.length(velocity);
                float targetSpeed = desiredSpeed > 0f ? desiredSpeed : currentSpeed;

                if (targetSpeed > 1e-4f) {
                    float3 desiredGroupVel = groupDir * targetSpeed;
                    float3 flowAccel = (desiredGroupVel - velocity) * localGroupFlowWeight;
                    steering += flowAccel;
                }
            }

            // --- 5) Split behaviour ---
            float localMaxAcceleration = maxAcceleration;
            float localMaxSpeed = maxSpeed;

            float splitPanicThreshold = b.SplitPanicThreshold;
            float splitLateralWeight = b.SplitLateralWeight;
            float splitAccelBoost = b.SplitAccelBoost;

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

            // --- 7) Attraction ---
            steering += ComputeAttraction(index);

            // --- 8) Self-propulsion (target speed) ---
            float3 propulsion = ComputePropulsion(
                currentVelocity: velocity,
                desiredSpeed: desiredSpeed);

            steering += propulsion;

            // --- 8.1) Micro wander / group noise / pattern ---
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
            float3 dir,
            out float collisionDist,
            out float targetDist,
            out float deadZoneUpper,
            out float farDist) {

            collisionDist = 0f;
            targetDist = 0f;
            deadZoneUpper = 0f;
            farDist = 0f;

            if ((uint)myBehaviourIndex >= (uint)BehaviourSettings.Length
                || (uint)neighbourBehaviourIndex >= (uint)BehaviourSettings.Length) {
                return float3.zero;
            }

            FlockBehaviourSettings a = BehaviourSettings[myBehaviourIndex];
            FlockBehaviourSettings b = BehaviourSettings[neighbourBehaviourIndex];

            float rA = a.BodyRadius;
            float rB = b.BodyRadius;

            if (rA <= 0f && rB <= 0f) {
                return float3.zero;
            }

            float spacing = math.max(0.5f, 0.5f * (a.SchoolingSpacingFactor + b.SchoolingSpacingFactor));
            float outer = math.max(1f, 0.5f * (a.SchoolingOuterFactor + b.SchoolingOuterFactor));
            float strength = math.max(0f, 0.5f * (a.SchoolingStrength + b.SchoolingStrength));
            if (strength <= 0f) {
                return float3.zero;
            }

            float softness = math.clamp(0.5f * (a.SchoolingInnerSoftness + b.SchoolingInnerSoftness), 0f, 1f);
            float deadFrac = math.clamp(0.5f * (a.SchoolingDeadzoneFraction + b.SchoolingDeadzoneFraction), 0f, 0.5f);

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
                force = t;
            } else if (distance < targetDist) {
                float innerSpan = math.max(targetDist - collisionDist, 1e-3f);
                float t = (targetDist - distance) / innerSpan;

                float t2 = t * t;
                float shaped;

                if (softness <= 0f) {
                    shaped = t;
                } else {
                    float t3 = t2 * t;
                    if (softness <= 0.5f) {
                        float u = softness * 2f;
                        shaped = math.lerp(t, t2, u);
                    } else {
                        float u = (softness - 0.5f) * 2f;
                        shaped = math.lerp(t2, t3, u);
                    }
                }

                force = shaped;
            } else {
                if (distance >= deadLower && distance <= deadZoneUpper) {
                    return float3.zero;
                }

                float attractStart = deadZoneUpper;
                float attractSpan = math.max(farDist - attractStart, 1e-3f);
                float t = (distance - attractStart) / attractSpan;

                float falloff = 1f - t;
                force = -falloff;
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

            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                return float3.zero;
            }

            float strength = BehaviourSettings[behaviourIndex].WanderStrength * GlobalWanderMultiplier;
            if (strength <= 0f || maxAcceleration <= 0f) {
                return float3.zero;
            }

            float frequency = math.max(0f, BehaviourSettings[behaviourIndex].WanderFrequency);
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

            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                return float3.zero;
            }

            FlockBehaviourSettings b = BehaviourSettings[behaviourIndex];

            float baseStrength = b.GroupNoiseStrength * GlobalGroupNoiseMultiplier;
            if (baseStrength <= 0f || maxAcceleration <= 0f) {
                return float3.zero;
            }

            float directionRate = math.max(0f, b.GroupNoiseDirectionRate);
            float speedWeight = math.saturate(b.GroupNoiseSpeedWeight);

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

            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                return float3.zero;
            }

            float weight = BehaviourSettings[behaviourIndex].PatternWeight * GlobalPatternMultiplier;
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

            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                return velocity;
            }

            FlockBehaviourSettings b = BehaviourSettings[behaviourIndex];

            if (b.UsePreferredDepth == 0) {
                return velocity;
            }

            float prefMin = b.PreferredDepthMinNorm;
            float prefMax = b.PreferredDepthMaxNorm;
            float weight = math.max(0f, b.PreferredDepthWeight);
            float biasStrength = math.max(0f, b.DepthBiasStrength);

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
                return velocity;
            }

            float envMinY = EnvironmentData.BoundsCenter.y - EnvironmentData.BoundsExtents.y;
            float envMaxY = EnvironmentData.BoundsCenter.y + EnvironmentData.BoundsExtents.y;
            float envHeight = math.max(envMaxY - envMinY, 0.0001f);

            float depthNorm = math.saturate((position.y - envMinY) / envHeight);

            float vy = velocity.y;

            float strength = math.saturate(weight * biasStrength);

            if (depthNorm < prefMin || depthNorm > prefMax) {
                float deltaNorm;
                float dir;

                if (depthNorm < prefMin) {
                    deltaNorm = (prefMin - depthNorm) / bandWidth;
                    dir = 1.0f;
                } else {
                    deltaNorm = (depthNorm - prefMax) / bandWidth;
                    dir = -1.0f;
                }

                deltaNorm = math.saturate(deltaNorm);
                float edgeT = deltaNorm * deltaNorm;
                float lerpFactor = math.saturate(strength * edgeT);

                float targetVy = dir * maxSpeed;

                vy = math.lerp(vy, targetVy, lerpFactor);

                float damping = math.saturate(1.0f - strength * edgeT * DeltaTime);
                vy *= damping;
            } else {
                float edgeFrac = math.clamp(b.PreferredDepthEdgeFraction, 0.01f, 0.49f);

                float borderThickness = bandWidth * edgeFrac;

                float distToMin = depthNorm - prefMin;
                float distToMax = prefMax - depthNorm;
                float edgeDist = math.min(distToMin, distToMax);

                if (edgeDist < borderThickness) {
                    float t = 1.0f - edgeDist / math.max(borderThickness, 0.0001f);
                    t = math.saturate(t);

                    bool nearBottom = distToMin < distToMax;
                    float dir = nearBottom ? 1.0f : -1.0f;

                    float innerStrength = strength * 0.5f;
                    float lerpFactor = math.saturate(innerStrength * t);

                    float targetVy = dir * maxSpeed * (innerStrength * t);

                    vy = math.lerp(vy, targetVy, lerpFactor);

                    float damping = math.saturate(1.0f - innerStrength * t * DeltaTime);
                    vy *= damping;
                }
            }

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

        bool TryLoadBehaviour(
            int agentIndex,
            out float3 position,
            out float3 velocity,
            out int behaviourIndex,
            out FlockBehaviourSettings b) {

            position = Positions[agentIndex];
            velocity = PrevVelocities[agentIndex];
            behaviourIndex = BehaviourIds[agentIndex];

            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                b = default;
                return false;
            }

            b = BehaviourSettings[behaviourIndex];
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

            // Reset per-call accumulators that this method owns.
            avoidDanger = 0.0f;
            friendlyNeighbourCount = 0;
            avoidSeparation = float3.zero;

            // Defensive: behaviour index must be valid to read settings.
            if ((uint)myBehaviourIndex >= (uint)BehaviourSettings.Length) {
                return;
            }

            FlockBehaviourSettings my = BehaviourSettings[myBehaviourIndex];

            float myAvoidWeight = my.AvoidanceWeight;
            float myNeutralWeight = my.NeutralWeight;
            float myAvoidResponse = math.max(0f, my.AvoidResponse);

            // Per-type radial damping strength for this agent
            float myRadialDamping = my.SchoolingRadialDamping;
            float3 selfPrevVel = PrevVelocities[agentIndex];

            int3 baseCell = GetCell(agentPosition);

            // Leadership state
            float maxLeaderWeight = -1.0f;
            const float epsilon = 1e-4f;

            // Per-type cell search radius (derived array kept separate)
            int cellRange = 1;
            if (BehaviourCellSearchRadius.IsCreated && (uint)myBehaviourIndex < (uint)BehaviourCellSearchRadius.Length) {
                cellRange = math.max(BehaviourCellSearchRadius[myBehaviourIndex], 1);
            }

            // Caps (0 = unlimited)
            int maxChecks = math.max(0, my.MaxNeighbourChecks);
            int maxFriendlySamples = math.max(0, my.MaxFriendlySamples);
            int maxSeparationSamples = math.max(0, my.MaxSeparationSamples);

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

                        if (!IsCellInsideGrid(neighbourCell)) {
                            continue;
                        }

                        int cellId = GetCellId(neighbourCell);

                        int countInCell = CellAgentCounts[cellId];
                        if (countInCell <= 0) {
                            continue;
                        }

                        int startInCell = CellAgentStarts[cellId];
                        for (int p = 0; p < countInCell; p += 1) {
                            int neighbourIndex = CellAgentPairs[startInCell + p].AgentIndex;
                            if (neighbourIndex == agentIndex) {
                                continue;
                            }

                            // Dedup neighbour if it appears in multiple cells
                            if (IsNeighbourVisitedOrMark(neighbourIndex, ref visited, ref seen0, ref seen1, ref seen2, ref seen3)) {
                                continue;
                            }

                            // Max unique neighbour checks per frame
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
                            if ((uint)neighbourBehaviourIndex >= (uint)BehaviourSettings.Length) {
                                continue;
                            }

                            FlockBehaviourSettings nb = BehaviourSettings[neighbourBehaviourIndex];

                            // --- HARD GEOMETRIC SEPARATION radius^2 (no sqrt yet) ---
                            float hardRadiusSq = separationRadiusSquared;

                            float myBody = my.BodyRadius;
                            float nbBody = nb.BodyRadius;

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
                            float distance = distanceSquared * invDist; // == sqrt(distanceSquared)
                            float3 dirUnit = offset * invDist;          // normalized

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
                                float neighbourLeaderWeight = nb.LeadershipWeight;
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

                            if (isAvoid && myAvoidResponse > 0f) {
                                float neighbourAvoidWeight = nb.AvoidanceWeight;

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

                            if (isNeutral) {
                                float neighbourNeutralWeight = nb.NeutralWeight;

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
