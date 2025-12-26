// File: Assets/Flock/Runtime/Jobs/SteeringIntegrateJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct SteeringIntegrateJob : IJobParallelFor {
        // Phase 2 input: per-agent neighbour aggregates
        [ReadOnly] public NativeArray<NeighbourAggregate> NeighbourAggregates;

        // Bounds probe outputs (per-agent)
        [ReadOnly] public NativeArray<float3> WallDirections;
        [ReadOnly] public NativeArray<float> WallDangers;

        // Core agent data
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> PrevVelocities;
        [NativeDisableParallelForRestriction] public NativeArray<float3> Velocities;

        // Behaviour
        [ReadOnly] public NativeArray<int> BehaviourIds;
        [ReadOnly] public NativeArray<FlockBehaviourSettings> BehaviourSettings;

        // Grid params (still needed for group-noise cell lookup)
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

        public void Execute(int index) {
            if (!TryLoadBehaviour(
                    index,
                    out float3 position,
                    out float3 velocity,
                    out int behaviourIndex,
                    out FlockBehaviourSettings b)) {

                Velocities[index] = PrevVelocities[index];
                return;
            }

            // --- Load neighbour aggregates (Phase 2) ---
            NeighbourAggregate agg = default;
            if (NeighbourAggregates.IsCreated && (uint)index < (uint)NeighbourAggregates.Length) {
                agg = NeighbourAggregates[index];
            }

            float maxSpeed = b.MaxSpeed;
            float maxAcceleration = b.MaxAcceleration;

            float desiredSpeed = b.DesiredSpeed;
            float alignmentWeight = b.AlignmentWeight;
            float cohesionWeight = b.CohesionWeight;
            float separationWeight = b.SeparationWeight;

            float influenceWeight = b.InfluenceWeight;
            float groupFlowWeight = b.GroupFlowWeight;

            float boundsWeight = math.max(0f, b.BoundsWeight);
            float boundsTangentialDamping = math.max(0f, b.BoundsTangentialDamping);
            float boundsInfluenceSuppression = math.max(0f, b.BoundsInfluenceSuppression);

            // --- Aggregates unpack ---
            float3 alignment = agg.AlignmentSum;
            float3 cohesion = agg.CohesionSum;
            float3 separation = agg.SeparationSum;
            float3 avoidSeparation = agg.AvoidSeparationSum;
            float3 radialDamping = agg.RadialDamping;

            int leaderNeighbourCount = agg.LeaderNeighbourCount;
            int separationCount = agg.SeparationCount;
            int friendlyNeighbourCount = agg.FriendlyNeighbourCount;
            float alignmentWeightSum = agg.AlignmentWeightSum;
            float cohesionWeightSum = agg.CohesionWeightSum;
            float avoidDanger = agg.AvoidDanger;

            // --- 3) Group size logic (loners / overcrowding) ---
            int groupSize = friendlyNeighbourCount + 1;

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
                int branch = (int)(hash % 3u);

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

            // --- 8.5) Bounds steering gate ---
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

            // --- 10) Preferred depth controller ---
            velocity = ApplyPreferredDepth(
                behaviourIndex,
                position,
                velocity,
                localMaxSpeed);

            velocity = LimitVector(velocity, localMaxSpeed);

            // --- 11) Bounds final velocity correction ---
            velocity = ApplyBoundsVelocity(
                agentIndex: index,
                velocity: velocity,
                boundsTangentialDamping: boundsTangentialDamping);

            velocity = LimitVector(velocity, localMaxSpeed);

            Velocities[index] = velocity;
        }

        // =========================
        // Helpers (copied from your current FlockStepJob)
        // =========================
        static float Hash01(uint seed) {
            seed ^= seed >> 17;
            seed *= 0xED5AD4BBu;
            seed ^= seed >> 11;
            seed *= 0xAC4C1B51u;
            seed ^= seed >> 15;
            seed *= 0x31848BABu;
            seed ^= seed >> 14;
            return (seed >> 8) * (1.0f / 16777216.0f);
        }

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

            float currentSpeed = math.length(currentVelocity);
            float targetSpeed = desiredSpeed > 0f ? desiredSpeed : currentSpeed;

            if (neighbourCount > 0 && targetSpeed > 1e-4f) {
                if (alignmentWeightSum > 1e-6f) {
                    float invAlign = 1.0f / alignmentWeightSum;

                    float3 alignmentDir = alignment * invAlign;
                    float3 alignmentNorm = math.normalizesafe(alignmentDir, currentVelocity);

                    float3 desiredAlignVel = alignmentNorm * targetSpeed;
                    float3 alignmentForce = (desiredAlignVel - currentVelocity) * alignmentWeight;

                    steering += alignmentForce;
                }

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

        int GetCellId(int3 cell) {
            return cell.x
                   + cell.y * GridResolution.x
                   + cell.z * GridResolution.x * GridResolution.y;
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
