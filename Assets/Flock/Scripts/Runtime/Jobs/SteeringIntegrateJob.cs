using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    /**
    * <summary>
    * Integrates per-agent steering inputs into final velocity, combining neighbour aggregates, environmental influences,
    * noise, and behavioural constraints.
    * </summary>
    */
    [BurstCompile]
    public struct SteeringIntegrateJob : IJobParallelFor {
        [ReadOnly]
        public NativeArray<NeighbourAggregate> NeighbourAggregates;

        [ReadOnly]
        public NativeArray<float3> WallDirections;

        [ReadOnly]
        public NativeArray<float> WallDangers;

        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<float3> PrevVelocities;

        [NativeDisableParallelForRestriction]
        public NativeArray<float3> Velocities;

        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public NativeArray<FlockBehaviourSettings> BehaviourSettings;

        [ReadOnly]
        public float3 GridOrigin;

        [ReadOnly]
        public int3 GridResolution;

        [ReadOnly]
        public float CellSize;

        [ReadOnly]
        public FlockEnvironmentData EnvironmentData;

        [ReadOnly]
        public float DeltaTime;

        [ReadOnly]
        public bool UseObstacleAvoidance;

        [ReadOnly]
        public float ObstacleAvoidWeight;

        [ReadOnly]
        public NativeArray<float3> ObstacleSteering;

        [ReadOnly]
        public bool UseAttraction;

        [ReadOnly]
        public float GlobalAttractionWeight;

        [ReadOnly]
        public NativeArray<float3> AttractionSteering;

        [ReadOnly]
        public NativeArray<float3> PatternSteering;

        [ReadOnly]
        public NativeArray<float3> CellGroupNoise;

        [ReadOnly]
        public float NoiseTime;

        [ReadOnly]
        public float GlobalWanderMultiplier;

        [ReadOnly]
        public float GlobalGroupNoiseMultiplier;

        [ReadOnly]
        public float GlobalPatternMultiplier;

        public void Execute(int agentIndex) {
            if (!TryLoadBehaviourContext(agentIndex, out BehaviourContext behaviourContext)) {
                Velocities[agentIndex] = PrevVelocities[agentIndex];
                return;
            }

            Velocities[agentIndex] = ComputeFinalVelocity(agentIndex, behaviourContext);
        }

        private float3 ComputeFinalVelocity(int agentIndex, BehaviourContext behaviourContext) {
            NeighbourAggregate neighbourAggregate = LoadNeighbourAggregate(agentIndex);

            float maximumSpeed = behaviourContext.BehaviourSettings.MaxSpeed;
            float maximumAcceleration = behaviourContext.BehaviourSettings.MaxAcceleration;

            float3 steeringAcceleration = ComputeSteeringAcceleration(agentIndex, behaviourContext, neighbourAggregate, ref maximumSpeed, ref maximumAcceleration);
            float3 integratedVelocity = IntegrateVelocity(behaviourContext.Velocity, steeringAcceleration, maximumSpeed, maximumAcceleration);

            float3 depthAdjustedVelocity = ApplyPreferredDepth(behaviourContext.BehaviourIndex, behaviourContext.Position, integratedVelocity, maximumSpeed);
            float boundsTangentialDamping = math.max(0f, behaviourContext.BehaviourSettings.BoundsTangentialDamping);

            float3 boundsAdjustedVelocity = ApplyBoundsVelocity(agentIndex, depthAdjustedVelocity, boundsTangentialDamping);
            return LimitVector(boundsAdjustedVelocity, maximumSpeed);
        }

        private float3 ComputeSteeringAcceleration(int agentIndex, BehaviourContext behaviourContext, NeighbourAggregate neighbourAggregate, ref float maximumSpeed, ref float maximumAcceleration) {
            FlockBehaviourSettings behaviourSettings = behaviourContext.BehaviourSettings;

            GetAdjustedFlockWeights(behaviourSettings, neighbourAggregate.FriendlyNeighbourCount, out float alignmentWeight, out float cohesionWeight, out float separationWeight);

            float3 steeringAcceleration = ComputeCoreSteering(behaviourContext, neighbourAggregate, behaviourSettings, alignmentWeight, cohesionWeight, separationWeight);
            steeringAcceleration = AddGroupFlowSteering(steeringAcceleration, behaviourContext.Velocity, neighbourAggregate, behaviourSettings);
            steeringAcceleration = ApplySplitBehaviour(agentIndex, steeringAcceleration, behaviourContext.Velocity, neighbourAggregate, behaviourSettings, ref maximumSpeed, ref maximumAcceleration, separationWeight);

            steeringAcceleration = AddObstacleAvoidanceSteering(agentIndex, steeringAcceleration);
            steeringAcceleration += ComputeAttractionSteering(agentIndex);
            steeringAcceleration += ComputePropulsionAcceleration(behaviourContext.Velocity, behaviourSettings.DesiredSpeed);

            steeringAcceleration += ComputeNoiseAndPatternSteering(agentIndex, behaviourContext.BehaviourIndex, behaviourContext.Position, behaviourContext.Velocity, maximumAcceleration);
            steeringAcceleration = ApplyBoundsSteering(agentIndex, steeringAcceleration, maximumAcceleration, behaviourSettings);

            return LimitVector(steeringAcceleration, maximumAcceleration);
        }

        private static void GetAdjustedFlockWeights(FlockBehaviourSettings behaviourSettings, int friendlyNeighbourCount, out float alignmentWeight, out float cohesionWeight, out float separationWeight) {
            alignmentWeight = behaviourSettings.AlignmentWeight;
            cohesionWeight = behaviourSettings.CohesionWeight;
            separationWeight = behaviourSettings.SeparationWeight;

            if (friendlyNeighbourCount <= 0) {
                return;
            }

            int groupSize = friendlyNeighbourCount + 1;
            AdjustWeightsForLoner(behaviourSettings, groupSize, ref cohesionWeight);
            AdjustWeightsForOvercrowding(behaviourSettings, groupSize, ref cohesionWeight, ref separationWeight);
        }

        private static void AdjustWeightsForLoner(FlockBehaviourSettings behaviourSettings, int groupSize, ref float cohesionWeight) {
            int minimumGroupSize = math.max(1, behaviourSettings.MinGroupSize);
            if (groupSize >= minimumGroupSize) {
                return;
            }

            float lonerFactor = math.max(0f, behaviourSettings.LonerCohesionBoost);
            lonerFactor *= math.max(1f, behaviourSettings.LonerRadiusMultiplier);
            lonerFactor *= math.max(0f, behaviourSettings.MinGroupSizeWeight);

            cohesionWeight *= 1f + lonerFactor;
        }

        private static void AdjustWeightsForOvercrowding(FlockBehaviourSettings behaviourSettings, int groupSize, ref float cohesionWeight, ref float separationWeight) {
            int minimumGroupSize = math.max(1, behaviourSettings.MinGroupSize);
            int maximumGroupSize = behaviourSettings.MaxGroupSize;

            if (maximumGroupSize < minimumGroupSize) {
                maximumGroupSize = minimumGroupSize;
            }

            if (maximumGroupSize <= 0 || groupSize <= maximumGroupSize) {
                return;
            }

            float overcrowdingFraction = (groupSize - maximumGroupSize) / math.max(1f, (float)maximumGroupSize);
            overcrowdingFraction = math.saturate(overcrowdingFraction);

            overcrowdingFraction *= math.max(1f, behaviourSettings.GroupRadiusMultiplier);
            overcrowdingFraction *= math.max(0f, behaviourSettings.MaxGroupSizeWeight);

            separationWeight *= 1f + overcrowdingFraction;
            cohesionWeight *= 1f - 0.5f * overcrowdingFraction;
        }

        private float3 ComputeCoreSteering(BehaviourContext behaviourContext, NeighbourAggregate neighbourAggregate, FlockBehaviourSettings behaviourSettings, float alignmentWeight, float cohesionWeight, float separationWeight) {
            float separationPanicMultiplier = 1f + math.saturate(neighbourAggregate.AvoidDanger);

            float3 flockSteeringAcceleration = ComputeFlockSteering(
                behaviourContext.Position,
                behaviourContext.Velocity,
                neighbourAggregate,
                alignmentWeight,
                cohesionWeight,
                separationWeight,
                behaviourSettings.DesiredSpeed,
                separationPanicMultiplier);

            flockSteeringAcceleration *= behaviourSettings.InfluenceWeight;
            return flockSteeringAcceleration + neighbourAggregate.RadialDamping;
        }

        private float3 ComputeFlockSteering(float3 currentPosition, float3 currentVelocity, NeighbourAggregate neighbourAggregate, float alignmentWeight, float cohesionWeight, float separationWeight, float desiredSpeed, float separationPanicMultiplier) {
            float currentSpeed = math.length(currentVelocity);
            float targetSpeed = desiredSpeed > 0f ? desiredSpeed : currentSpeed;

            float3 steeringAcceleration = float3.zero;
            steeringAcceleration += ComputeAlignmentSteering(currentVelocity, neighbourAggregate, alignmentWeight, targetSpeed);
            steeringAcceleration += ComputeCohesionSteering(currentPosition, currentVelocity, neighbourAggregate, cohesionWeight, targetSpeed);
            steeringAcceleration += ComputeSeparationSteering(neighbourAggregate, separationWeight, separationPanicMultiplier);

            return steeringAcceleration;
        }

        private static float3 ComputeAlignmentSteering(float3 currentVelocity, NeighbourAggregate neighbourAggregate, float alignmentWeight, float targetSpeed) {
            if (neighbourAggregate.LeaderNeighbourCount <= 0 || targetSpeed <= 1e-4f || neighbourAggregate.AlignmentWeightSum <= 1e-6f) {
                return float3.zero;
            }

            float inverseAlignmentWeightSum = 1f / neighbourAggregate.AlignmentWeightSum;
            float3 alignmentDirection = neighbourAggregate.AlignmentSum * inverseAlignmentWeightSum;
            float3 alignmentDirectionNormalized = math.normalizesafe(alignmentDirection, currentVelocity);

            float3 desiredAlignmentVelocity = alignmentDirectionNormalized * targetSpeed;
            return (desiredAlignmentVelocity - currentVelocity) * alignmentWeight;
        }

        private static float3 ComputeCohesionSteering(float3 currentPosition, float3 currentVelocity, NeighbourAggregate neighbourAggregate, float cohesionWeight, float targetSpeed) {
            if (neighbourAggregate.LeaderNeighbourCount <= 0 || targetSpeed <= 1e-4f || neighbourAggregate.CohesionWeightSum <= 1e-6f) {
                return float3.zero;
            }

            float inverseCohesionWeightSum = 1f / neighbourAggregate.CohesionWeightSum;
            float3 cohesionCenter = neighbourAggregate.CohesionSum * inverseCohesionWeightSum;
            float3 directionToCenter = cohesionCenter - currentPosition;

            float3 cohesionDirection = math.normalizesafe(directionToCenter, float3.zero);
            float3 desiredCohesionVelocity = cohesionDirection * targetSpeed;

            return (desiredCohesionVelocity - currentVelocity) * cohesionWeight;
        }

        private static float3 ComputeSeparationSteering(NeighbourAggregate neighbourAggregate, float separationWeight, float separationPanicMultiplier) {
            if (neighbourAggregate.SeparationCount <= 0) {
                return float3.zero;
            }

            float inverseSeparationCount = 1f / neighbourAggregate.SeparationCount;
            float3 separationAverage = neighbourAggregate.SeparationSum * inverseSeparationCount;

            return separationAverage * separationWeight * separationPanicMultiplier;
        }

        private static float3 AddGroupFlowSteering(float3 steeringAcceleration, float3 currentVelocity, NeighbourAggregate neighbourAggregate, FlockBehaviourSettings behaviourSettings) {
            if (behaviourSettings.GroupFlowWeight <= 0f || neighbourAggregate.LeaderNeighbourCount <= 0 || neighbourAggregate.AlignmentWeightSum <= 1e-6f) {
                return steeringAcceleration;
            }

            float3 groupDirectionRaw = neighbourAggregate.AlignmentSum / neighbourAggregate.AlignmentWeightSum;
            float3 groupDirection = math.normalizesafe(groupDirectionRaw, currentVelocity);

            float currentSpeed = math.length(currentVelocity);
            float targetSpeed = behaviourSettings.DesiredSpeed > 0f ? behaviourSettings.DesiredSpeed : currentSpeed;

            if (targetSpeed <= 1e-4f) {
                return steeringAcceleration;
            }

            float3 desiredGroupVelocity = groupDirection * targetSpeed;
            float3 flowAcceleration = (desiredGroupVelocity - currentVelocity) * behaviourSettings.GroupFlowWeight;

            return steeringAcceleration + flowAcceleration;
        }

        private float3 ApplySplitBehaviour(int agentIndex, float3 steeringAcceleration, float3 currentVelocity, NeighbourAggregate neighbourAggregate, FlockBehaviourSettings behaviourSettings, ref float maximumSpeed, ref float maximumAcceleration, float separationWeight) {
            if (!ShouldSplit(neighbourAggregate, behaviourSettings)) {
                return steeringAcceleration;
            }

            float splitIntensity = math.saturate(neighbourAggregate.AvoidDanger);
            float3 splitDirection = ComputeSplitDirection(agentIndex, currentVelocity, neighbourAggregate, behaviourSettings.SplitLateralWeight);

            steeringAcceleration += splitDirection * separationWeight * splitIntensity;

            float boostMultiplier = 1f + behaviourSettings.SplitAccelBoost * splitIntensity;
            maximumSpeed *= boostMultiplier;
            maximumAcceleration *= boostMultiplier;

            return steeringAcceleration;
        }

        private static bool ShouldSplit(NeighbourAggregate neighbourAggregate, FlockBehaviourSettings behaviourSettings) {
            int groupSize = neighbourAggregate.FriendlyNeighbourCount + 1;
            int minimumGroupSizeForSplit = math.max(3, math.max(1, behaviourSettings.MinGroupSize));

            bool hasSufficientGroupSize = groupSize >= minimumGroupSizeForSplit;
            bool hasValidSplitParameters = behaviourSettings.SplitPanicThreshold > 0f && behaviourSettings.SplitLateralWeight > 0f && behaviourSettings.SplitAccelBoost >= 0f;

            return hasSufficientGroupSize && hasValidSplitParameters && neighbourAggregate.AvoidDanger >= behaviourSettings.SplitPanicThreshold;
        }

        private static float3 ComputeSplitDirection(int agentIndex, float3 currentVelocity, NeighbourAggregate neighbourAggregate, float splitLateralWeight) {
            float3 fleeDirection = ComputeSplitFleeDirection(currentVelocity, neighbourAggregate);
            float3 sideDirection = ComputeSplitSideDirection(fleeDirection);

            int branchIndex = ComputeSplitBranch(agentIndex);
            float sideSign = branchIndex == 0 ? -1f : (branchIndex == 2 ? 1f : 0f);

            if (sideSign == 0f) {
                return fleeDirection;
            }

            float3 combinedDirection = fleeDirection + sideDirection * sideSign * splitLateralWeight;
            return math.normalizesafe(combinedDirection, fleeDirection);
        }

        private static float3 ComputeSplitFleeDirection(float3 currentVelocity, NeighbourAggregate neighbourAggregate) {
            float3 fleeSource = neighbourAggregate.AvoidSeparationSum;

            if (math.lengthsq(fleeSource) <= 1e-6f) {
                fleeSource = math.lengthsq(neighbourAggregate.SeparationSum) > 1e-6f ? neighbourAggregate.SeparationSum : currentVelocity;
            }

            return math.normalizesafe(fleeSource, new float3(0f, 0f, 1f));
        }

        private static float3 ComputeSplitSideDirection(float3 fleeDirection) {
            float3 upDirection = new float3(0f, 1f, 0f);
            float3 sideDirection = math.cross(fleeDirection, upDirection);

            if (math.lengthsq(sideDirection) < 1e-4f) {
                upDirection = new float3(0f, 0f, 1f);
                sideDirection = math.cross(fleeDirection, upDirection);
            }

            return math.normalizesafe(sideDirection, float3.zero);
        }

        private static int ComputeSplitBranch(int agentIndex) {
            uint hashValue = (uint)(agentIndex * 9781 + 1);
            hashValue ^= hashValue >> 11;
            hashValue *= 0x9E3779B1u;

            return (int)(hashValue % 3u);
        }

        private float3 AddObstacleAvoidanceSteering(int agentIndex, float3 steeringAcceleration) {
            if (!UseObstacleAvoidance || !ObstacleSteering.IsCreated || (uint)agentIndex >= (uint)ObstacleSteering.Length) {
                return steeringAcceleration;
            }

            float3 obstacleAcceleration = ObstacleSteering[agentIndex];
            return steeringAcceleration + obstacleAcceleration * ObstacleAvoidWeight;
        }

        private float3 ComputeAttractionSteering(int agentIndex) {
            if (!UseAttraction || !AttractionSteering.IsCreated || (uint)agentIndex >= (uint)AttractionSteering.Length) {
                return float3.zero;
            }

            return AttractionSteering[agentIndex] * GlobalAttractionWeight;
        }

        private static float3 ComputePropulsionAcceleration(float3 currentVelocity, float desiredSpeed) {
            if (desiredSpeed <= 0f) {
                return float3.zero;
            }

            float currentSpeedSquared = math.lengthsq(currentVelocity);
            if (currentSpeedSquared < 1e-6f) {
                return float3.zero;
            }

            float inverseCurrentSpeed = math.rsqrt(currentSpeedSquared);
            float currentSpeed = currentSpeedSquared * inverseCurrentSpeed;

            float speedError = desiredSpeed - currentSpeed;
            if (math.abs(speedError) < 1e-3f) {
                return float3.zero;
            }

            float3 direction = currentVelocity * inverseCurrentSpeed;
            return direction * speedError;
        }

        private float3 ComputeNoiseAndPatternSteering(int agentIndex, int behaviourIndex, float3 position, float3 currentVelocity, float maximumAcceleration) {
            float3 wanderNoise = ComputeWanderNoise(agentIndex, behaviourIndex, currentVelocity, maximumAcceleration, NoiseTime);
            float3 groupNoise = ComputeGroupNoise(agentIndex, behaviourIndex, position, currentVelocity, maximumAcceleration);
            float3 patternNoise = ComputePatternSteering(agentIndex, behaviourIndex, maximumAcceleration);

            return wanderNoise + groupNoise + patternNoise;
        }

        private float3 ApplyBoundsSteering(int agentIndex, float3 steeringAcceleration, float maximumAcceleration, FlockBehaviourSettings behaviourSettings) {
            float boundsWeight = math.max(0f, behaviourSettings.BoundsWeight);
            float boundsInfluenceSuppression = math.max(0f, behaviourSettings.BoundsInfluenceSuppression);

            if (boundsWeight <= 0f && boundsInfluenceSuppression <= 0f) {
                return steeringAcceleration;
            }

            float danger = GetWallDanger(agentIndex);
            if (danger <= 0f) {
                return steeringAcceleration;
            }

            float3 wallDirection = GetWallDirection(agentIndex);
            if (math.lengthsq(wallDirection) < 1e-8f) {
                return steeringAcceleration;
            }

            return ApplyBoundsSteeringInternal(steeringAcceleration, wallDirection, danger, maximumAcceleration, boundsWeight, boundsInfluenceSuppression);
        }

        private static float3 ApplyBoundsSteeringInternal(float3 steeringAcceleration, float3 wallDirection, float danger, float maximumAcceleration, float boundsWeight, float boundsInfluenceSuppression) {
            float gate = 1f - danger * boundsInfluenceSuppression;
            steeringAcceleration *= math.saturate(gate);

            float3 wallNormal = math.normalizesafe(wallDirection, float3.zero);
            float radialAcceleration = danger * boundsWeight * maximumAcceleration;

            return steeringAcceleration + wallNormal * radialAcceleration;
        }

        private float GetWallDanger(int agentIndex) {
            if (!WallDangers.IsCreated || (uint)agentIndex >= (uint)WallDangers.Length) {
                return 0f;
            }

            return WallDangers[agentIndex];
        }

        private float3 GetWallDirection(int agentIndex) {
            if (!WallDirections.IsCreated || (uint)agentIndex >= (uint)WallDirections.Length) {
                return float3.zero;
            }

            return WallDirections[agentIndex];
        }

        private float3 IntegrateVelocity(float3 currentVelocity, float3 steeringAcceleration, float maximumSpeed, float maximumAcceleration) {
            float3 limitedAcceleration = LimitVector(steeringAcceleration, maximumAcceleration);
            float3 integratedVelocity = currentVelocity + limitedAcceleration * DeltaTime;

            integratedVelocity = LimitVector(integratedVelocity, maximumSpeed);
            integratedVelocity = ApplyDamping(integratedVelocity, EnvironmentData.GlobalDamping, DeltaTime);

            return integratedVelocity;
        }

        private float3 ComputeWanderNoise(int agentIndex, int behaviourIndex, float3 currentVelocity, float maximumAcceleration, float time) {
            if (!TryGetBehaviourSettings(behaviourIndex, out FlockBehaviourSettings behaviourSettings)) {
                return float3.zero;
            }

            float wanderStrength = behaviourSettings.WanderStrength * GlobalWanderMultiplier;
            if (wanderStrength <= 0f || maximumAcceleration <= 0f) {
                return float3.zero;
            }

            float wanderFrequency = math.max(0f, behaviourSettings.WanderFrequency);
            float3 wanderDirection = ComputeWanderDirection(agentIndex, time, wanderFrequency);

            return ComputeWanderAcceleration(currentVelocity, wanderDirection, maximumAcceleration * wanderStrength);
        }

        private static float3 ComputeWanderDirection(int agentIndex, float time, float frequency) {
            float timeScaled = time * frequency;
            uint baseSeed = (uint)(agentIndex * 0x9E3779B1u + 0x85EBCA6Bu);

            float phaseX = ComputeHash01(baseSeed ^ 0xA2C2A1EDu) * 6.2831853f;
            float phaseY = ComputeHash01(baseSeed ^ 0x27D4EB2Fu) * 6.2831853f;
            float phaseZ = ComputeHash01(baseSeed ^ 0x165667B1u) * 6.2831853f;

            float3 direction = new float3(
                math.sin(timeScaled + phaseX),
                math.sin(timeScaled * 1.37f + phaseY),
                math.sin(timeScaled * 1.79f + phaseZ));

            return math.normalizesafe(direction, float3.zero);
        }

        private static float3 ComputeWanderAcceleration(float3 currentVelocity, float3 wanderDirection, float maximumWanderAcceleration) {
            if (math.lengthsq(wanderDirection) < 1e-6f || maximumWanderAcceleration <= 0f) {
                return float3.zero;
            }

            float3 forward = math.normalizesafe(currentVelocity, wanderDirection);
            float3 side = math.normalizesafe(math.cross(forward, new float3(0f, 1f, 0f)), wanderDirection);
            float3 up = math.cross(forward, side);

            float3 combinedDirection = forward * 0.7f + side * 0.2f + up * 0.1f;
            float3 finalDirection = math.normalizesafe(combinedDirection, forward);

            return finalDirection * maximumWanderAcceleration;
        }

        private float3 ComputeGroupNoise(int agentIndex, int behaviourIndex, float3 position, float3 currentVelocity, float maximumAcceleration) {
            if (!TryGetBehaviourSettings(behaviourIndex, out FlockBehaviourSettings behaviourSettings)) {
                return float3.zero;
            }

            float groupNoiseStrength = behaviourSettings.GroupNoiseStrength * GlobalGroupNoiseMultiplier;
            if (groupNoiseStrength <= 0f || maximumAcceleration <= 0f) {
                return float3.zero;
            }

            if (!TryGetCellNoiseDirection(position, out float3 noiseDirection)) {
                return float3.zero;
            }

            float directionRate = math.max(0f, behaviourSettings.GroupNoiseDirectionRate);
            float speedWeight = math.saturate(behaviourSettings.GroupNoiseSpeedWeight);

            return ComputeGroupNoiseAcceleration(currentVelocity, noiseDirection, maximumAcceleration, groupNoiseStrength, directionRate, speedWeight);
        }

        private bool TryGetCellNoiseDirection(float3 position, out float3 noiseDirection) {
            noiseDirection = float3.zero;

            int3 cell = GetCell(position);
            int cellId = GetCellId(cell);

            if (!CellGroupNoise.IsCreated || (uint)cellId >= (uint)CellGroupNoise.Length) {
                return false;
            }

            noiseDirection = CellGroupNoise[cellId];
            if (math.lengthsq(noiseDirection) < 1e-6f) {
                return false;
            }

            noiseDirection = math.normalizesafe(noiseDirection, float3.zero);
            return math.lengthsq(noiseDirection) >= 1e-6f;
        }

        private static float3 ComputeGroupNoiseAcceleration(float3 currentVelocity, float3 noiseDirection, float maximumAcceleration, float baseStrength, float directionRate, float speedWeight) {
            float strength = baseStrength * directionRate;
            if (strength <= 0f) {
                return float3.zero;
            }

            float3 forward = math.normalizesafe(currentVelocity, noiseDirection);
            float projection = math.dot(noiseDirection, forward);

            float3 lateralDirection = ComputeLateralDirection(noiseDirection, forward, projection);
            float maximumNoiseAcceleration = maximumAcceleration * strength;

            return ComposeNoiseAcceleration(forward, lateralDirection, projection, maximumNoiseAcceleration, speedWeight);
        }

        private static float3 ComputeLateralDirection(float3 noiseDirection, float3 forward, float projection) {
            float3 componentAlongForward = forward * projection;
            float3 lateralComponent = noiseDirection - componentAlongForward;

            float lateralLengthSquared = math.lengthsq(lateralComponent);
            if (lateralLengthSquared <= 1e-8f) {
                return float3.zero;
            }

            return lateralComponent * math.rsqrt(lateralLengthSquared);
        }

        private static float3 ComposeNoiseAcceleration(float3 forward, float3 lateralDirection, float projection, float maximumNoiseAcceleration, float speedWeight) {
            float lateralAccelerationMagnitude = maximumNoiseAcceleration * (1f - speedWeight);
            float speedAccelerationMagnitude = maximumNoiseAcceleration * speedWeight;

            float3 result = float3.zero;

            if (math.lengthsq(lateralDirection) > 1e-8f && lateralAccelerationMagnitude > 0f) {
                result += lateralDirection * lateralAccelerationMagnitude;
            }

            if (math.lengthsq(forward) > 1e-8f && speedAccelerationMagnitude > 0f) {
                result += forward * (speedAccelerationMagnitude * projection);
            }

            return result;
        }

        private float3 ComputePatternSteering(int agentIndex, int behaviourIndex, float maximumAcceleration) {
            if (!TryGetBehaviourSettings(behaviourIndex, out FlockBehaviourSettings behaviourSettings)) {
                return float3.zero;
            }

            float patternWeight = behaviourSettings.PatternWeight * GlobalPatternMultiplier;
            if (patternWeight <= 0f || maximumAcceleration <= 0f) {
                return float3.zero;
            }

            if (!PatternSteering.IsCreated || (uint)agentIndex >= (uint)PatternSteering.Length) {
                return float3.zero;
            }

            float3 pattern = PatternSteering[agentIndex];
            float3 direction = math.normalizesafe(pattern, float3.zero);

            return math.lengthsq(direction) < 1e-8f ? float3.zero : direction * maximumAcceleration * patternWeight;
        }

        private float3 ApplyPreferredDepth(int behaviourIndex, float3 position, float3 velocity, float maximumSpeed) {
            if (!TryGetBehaviourSettings(behaviourIndex, out FlockBehaviourSettings behaviourSettings)) {
                return velocity;
            }

            if (behaviourSettings.UsePreferredDepth == 0) {
                return velocity;
            }

            if (!TryGetPreferredDepthParameters(behaviourSettings, out PreferredDepthParameters preferredDepthParameters)) {
                return velocity;
            }

            float normalizedDepth = ComputeNormalizedDepth(position.y);
            float updatedVerticalVelocity = ComputePreferredDepthVerticalVelocity(velocity.y, normalizedDepth, maximumSpeed, preferredDepthParameters);

            velocity.y = math.clamp(updatedVerticalVelocity, -maximumSpeed, maximumSpeed);
            return velocity;
        }

        private static bool TryGetPreferredDepthParameters(FlockBehaviourSettings behaviourSettings, out PreferredDepthParameters preferredDepthParameters) {
            preferredDepthParameters = default;

            float preferredDepthMinimumNormalized = behaviourSettings.PreferredDepthMinNorm;
            float preferredDepthMaximumNormalized = behaviourSettings.PreferredDepthMaxNorm;

            if (preferredDepthMaximumNormalized < preferredDepthMinimumNormalized) {
                float preferredDepthTemporary = preferredDepthMinimumNormalized;
                preferredDepthMinimumNormalized = preferredDepthMaximumNormalized;
                preferredDepthMaximumNormalized = preferredDepthTemporary;
            }

            float bandWidth = preferredDepthMaximumNormalized - preferredDepthMinimumNormalized;
            float weight = math.max(0f, behaviourSettings.PreferredDepthWeight);
            float biasStrength = math.max(0f, behaviourSettings.DepthBiasStrength);

            if (bandWidth <= 1e-4f || weight <= 0f || biasStrength <= 0f) {
                return false;
            }

            preferredDepthParameters = new PreferredDepthParameters(preferredDepthMinimumNormalized, preferredDepthMaximumNormalized, bandWidth, weight, biasStrength, behaviourSettings.PreferredDepthEdgeFraction);
            return true;
        }

        private float ComputeNormalizedDepth(float positionY) {
            float environmentMinimumY = EnvironmentData.BoundsCenter.y - EnvironmentData.BoundsExtents.y;
            float environmentMaximumY = EnvironmentData.BoundsCenter.y + EnvironmentData.BoundsExtents.y;

            float environmentHeight = math.max(environmentMaximumY - environmentMinimumY, 0.0001f);
            return math.saturate((positionY - environmentMinimumY) / environmentHeight);
        }

        private float ComputePreferredDepthVerticalVelocity(float verticalVelocity, float normalizedDepth, float maximumSpeed, PreferredDepthParameters preferredDepthParameters) {
            float strength = math.saturate(preferredDepthParameters.Weight * preferredDepthParameters.BiasStrength);

            if (normalizedDepth < preferredDepthParameters.MinimumNormalized || normalizedDepth > preferredDepthParameters.MaximumNormalized) {
                return ApplyPreferredDepthOutsideBand(verticalVelocity, normalizedDepth, maximumSpeed, preferredDepthParameters, strength);
            }

            return ApplyPreferredDepthInsideBand(verticalVelocity, normalizedDepth, maximumSpeed, preferredDepthParameters, strength);
        }

        private float ApplyPreferredDepthOutsideBand(float verticalVelocity, float normalizedDepth, float maximumSpeed, PreferredDepthParameters preferredDepthParameters, float strength) {
            float deltaNormalized = normalizedDepth < preferredDepthParameters.MinimumNormalized
                ? (preferredDepthParameters.MinimumNormalized - normalizedDepth) / preferredDepthParameters.BandWidth
                : (normalizedDepth - preferredDepthParameters.MaximumNormalized) / preferredDepthParameters.BandWidth;

            float directionSign = normalizedDepth < preferredDepthParameters.MinimumNormalized ? 1f : -1f;

            float edgeFactor = math.saturate(deltaNormalized);
            float edgeCurve = edgeFactor * edgeFactor;

            float lerpFactor = math.saturate(strength * edgeCurve);
            float targetVerticalVelocity = directionSign * maximumSpeed;

            float updatedVerticalVelocity = math.lerp(verticalVelocity, targetVerticalVelocity, lerpFactor);
            float dampingFactor = math.saturate(1f - strength * edgeCurve * DeltaTime);

            return updatedVerticalVelocity * dampingFactor;
        }

        private float ApplyPreferredDepthInsideBand(float verticalVelocity, float normalizedDepth, float maximumSpeed, PreferredDepthParameters preferredDepthParameters, float strength) {
            float edgeFraction = math.clamp(preferredDepthParameters.EdgeFraction, 0.01f, 0.49f);
            float borderThickness = preferredDepthParameters.BandWidth * edgeFraction;

            float distanceToMinimum = normalizedDepth - preferredDepthParameters.MinimumNormalized;
            float distanceToMaximum = preferredDepthParameters.MaximumNormalized - normalizedDepth;
            float distanceToNearestEdge = math.min(distanceToMinimum, distanceToMaximum);

            if (distanceToNearestEdge >= borderThickness) {
                return verticalVelocity;
            }

            float t = math.saturate(1f - distanceToNearestEdge / math.max(borderThickness, 0.0001f));
            bool isNearBottomEdge = distanceToMinimum < distanceToMaximum;

            return ApplyPreferredDepthNearEdge(verticalVelocity, maximumSpeed, strength, t, isNearBottomEdge);
        }

        private float ApplyPreferredDepthNearEdge(float verticalVelocity, float maximumSpeed, float strength, float t, bool isNearBottomEdge) {
            float directionSign = isNearBottomEdge ? 1f : -1f;

            float innerStrength = strength * 0.5f;
            float lerpFactor = math.saturate(innerStrength * t);

            float targetVerticalVelocity = directionSign * maximumSpeed * (innerStrength * t);
            float updatedVerticalVelocity = math.lerp(verticalVelocity, targetVerticalVelocity, lerpFactor);

            float dampingFactor = math.saturate(1f - innerStrength * t * DeltaTime);
            return updatedVerticalVelocity * dampingFactor;
        }

        private float3 ApplyBoundsVelocity(int agentIndex, float3 velocity, float boundsTangentialDamping) {
            float danger = GetWallDanger(agentIndex);
            if (danger <= 0f || boundsTangentialDamping <= 0f) {
                return velocity;
            }

            float3 wallDirection = GetWallDirection(agentIndex);
            if (math.lengthsq(wallDirection) < 1e-8f) {
                return velocity;
            }

            float3 wallNormal = math.normalizesafe(wallDirection, float3.zero);
            return ApplyBoundsVelocityInternal(velocity, wallNormal, danger, boundsTangentialDamping);
        }

        private float3 ApplyBoundsVelocityInternal(float3 velocity, float3 wallNormal, float danger, float boundsTangentialDamping) {
            float radialVelocityScalar = math.dot(velocity, wallNormal);
            float3 radialVelocity = wallNormal * radialVelocityScalar;

            float3 tangentialVelocity = velocity - radialVelocity;
            float killFactor = math.saturate(danger * boundsTangentialDamping * DeltaTime);

            tangentialVelocity *= 1f - killFactor;
            return radialVelocity + tangentialVelocity;
        }

        private NeighbourAggregate LoadNeighbourAggregate(int agentIndex) {
            if (!NeighbourAggregates.IsCreated || (uint)agentIndex >= (uint)NeighbourAggregates.Length) {
                return default;
            }

            return NeighbourAggregates[agentIndex];
        }

        private bool TryLoadBehaviourContext(int agentIndex, out BehaviourContext behaviourContext) {
            behaviourContext = default;

            if (!Positions.IsCreated || (uint)agentIndex >= (uint)Positions.Length) {
                return false;
            }

            if (!PrevVelocities.IsCreated || (uint)agentIndex >= (uint)PrevVelocities.Length) {
                return false;
            }

            if (!BehaviourIds.IsCreated || (uint)agentIndex >= (uint)BehaviourIds.Length) {
                return false;
            }

            int behaviourIndex = BehaviourIds[agentIndex];
            if (!TryGetBehaviourSettings(behaviourIndex, out FlockBehaviourSettings behaviourSettings)) {
                return false;
            }

            behaviourContext = new BehaviourContext(Positions[agentIndex], PrevVelocities[agentIndex], behaviourIndex, behaviourSettings);
            return true;
        }

        private bool TryGetBehaviourSettings(int behaviourIndex, out FlockBehaviourSettings behaviourSettings) {
            behaviourSettings = default;

            if (!BehaviourSettings.IsCreated || (uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                return false;
            }

            behaviourSettings = BehaviourSettings[behaviourIndex];
            return true;
        }

        private int3 GetCell(float3 position) {
            float safeCellSize = math.max(CellSize, 0.0001f);

            float3 localPosition = position - GridOrigin;
            float3 scaledPosition = localPosition / safeCellSize;

            int3 cell = (int3)math.floor(scaledPosition);

            int3 maximumCell = GridResolution - new int3(1, 1, 1);
            return math.clamp(cell, new int3(0, 0, 0), maximumCell);
        }

        private int GetCellId(int3 cell) {
            return cell.x + cell.y * GridResolution.x + cell.z * GridResolution.x * GridResolution.y;
        }

        private static float ComputeHash01(uint seedValue) {
            seedValue ^= seedValue >> 17;
            seedValue *= 0xED5AD4BBu;
            seedValue ^= seedValue >> 11;
            seedValue *= 0xAC4C1B51u;
            seedValue ^= seedValue >> 15;
            seedValue *= 0x31848BABu;
            seedValue ^= seedValue >> 14;

            return (seedValue >> 8) * (1f / 16777216f);
        }

        private static float3 LimitVector(float3 value, float maximumLength) {
            if (maximumLength <= 0f) {
                return float3.zero;
            }

            float lengthSquared = math.lengthsq(value);
            if (lengthSquared <= 0f) {
                return value;
            }

            float maximumLengthSquared = maximumLength * maximumLength;
            if (lengthSquared <= maximumLengthSquared) {
                return value;
            }

            float inverseLength = math.rsqrt(lengthSquared);
            return value * (maximumLength * inverseLength);
        }

        private static float3 ApplyDamping(float3 velocity, float damping, float deltaTime) {
            if (damping <= 0f) {
                return velocity;
            }

            float factor = math.saturate(1f - damping * deltaTime);
            return velocity * factor;
        }

        private readonly struct BehaviourContext {
            public readonly float3 Position;
            public readonly float3 Velocity;
            public readonly int BehaviourIndex;
            public readonly FlockBehaviourSettings BehaviourSettings;

            public BehaviourContext(float3 position, float3 velocity, int behaviourIndex, FlockBehaviourSettings behaviourSettings) {
                Position = position;
                Velocity = velocity;
                BehaviourIndex = behaviourIndex;
                BehaviourSettings = behaviourSettings;
            }
        }

        private readonly struct PreferredDepthParameters {
            public readonly float MinimumNormalized;
            public readonly float MaximumNormalized;
            public readonly float BandWidth;
            public readonly float Weight;
            public readonly float BiasStrength;
            public readonly float EdgeFraction;

            public PreferredDepthParameters(float minimumNormalized, float maximumNormalized, float bandWidth, float weight, float biasStrength, float edgeFraction) {
                MinimumNormalized = minimumNormalized;
                MaximumNormalized = maximumNormalized;
                BandWidth = bandWidth;
                Weight = weight;
                BiasStrength = biasStrength;
                EdgeFraction = edgeFraction;
            }
        }
    }
}
