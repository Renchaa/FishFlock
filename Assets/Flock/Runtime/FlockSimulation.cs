// File: Assets/Flock/Runtime/FlockSimulation.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Jobs;
    using Flock.Runtime.Logging;
    using System;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    public sealed class FlockSimulation {
        const int DefaultGridCapacityMultiplier = 8;
        const float DefaultObstacleAvoidStrength = 2.0f;
        const float DefaultObstacleAvoidWeight = 1.0f;
        const float DefaultAttractionWeight = 1.0f;

        NativeArray<float3> positions;
        NativeArray<float3> velocities;
        NativeArray<float3> prevVelocities; // NEW
                                            // NEW: per-behaviour bounds response

        NativeArray<float> behaviourBoundsWeight;               // radial push
        NativeArray<float> behaviourBoundsTangentialDamping;    // kill sliding
        NativeArray<float> behaviourBoundsInfluenceSuppression; // how much to mute flocking near walls

        NativeArray<float3> wallDirections; // inward normal(s) near walls
        NativeArray<float> wallDangers;     // 0..1 (or a bit >1 if outside)

        // NEW: per-behaviour noise settings
        NativeArray<float> behaviourWanderStrength;
        NativeArray<float> behaviourWanderFrequency;
        NativeArray<float> behaviourGroupNoiseStrength;
        NativeArray<float> behaviourPatternWeight;
        NativeArray<float3> patternSteering;
        float simulationTime; // NEW: accumulated sim time for noise

        NativeArray<int> behaviourIds;
        NativeArray<float> behaviourMaxSpeed;
        NativeArray<float> behaviourMaxAcceleration;
        NativeArray<float> behaviourDesiredSpeed;
        NativeArray<float> behaviourNeighbourRadius;
        NativeArray<float> behaviourSeparationRadius;
        NativeArray<float> behaviourAlignmentWeight;
        NativeArray<float> behaviourCohesionWeight;
        NativeArray<float> behaviourSeparationWeight;
        NativeArray<float> behaviourInfluenceWeight;
        NativeArray<float> behaviourLeadershipWeight;
        NativeArray<uint> behaviourGroupMask;
        NativeArray<float> behaviourGroupFlowWeight;


        NativeArray<float> behaviourAvoidanceWeight;   // ADD
        NativeArray<float> behaviourNeutralWeight;     // ADD
        NativeArray<uint> behaviourAvoidMask;          // ADD
        NativeArray<uint> behaviourNeutralMask;        // ADD
        NativeArray<FlockObstacleData> obstacles;
        NativeArray<float3> obstacleSteering;
        NativeArray<float> behaviourAvoidResponse;
        NativeArray<float> behaviourAttractionWeight;
        NativeArray<float> behaviourSplitPanicThreshold; // NEW
        NativeArray<float> behaviourSplitLateralWeight;  // NEW
        NativeArray<float> behaviourSplitAccelBoost;
        // Grouping behaviour
        NativeArray<int> behaviourMinGroupSize;
        NativeArray<int> behaviourMaxGroupSize;
        NativeArray<float> behaviourGroupRadiusMultiplier;
        NativeArray<float> behaviourLonerRadiusMultiplier;
        NativeArray<float> behaviourLonerCohesionBoost;
        NativeArray<float> behaviourMinGroupSizeWeight;
        NativeArray<float> behaviourMaxGroupSizeWeight;
        NativeArray<byte> behaviourUsePreferredDepth;
        NativeArray<float> behaviourPreferredDepthMin;
        NativeArray<float> behaviourPreferredDepthMax;
        NativeArray<float> behaviourDepthBiasStrength;
        NativeArray<byte> behaviourDepthWinsOverAttractor;
        NativeArray<float> behaviourPreferredDepthMinNorm;
        NativeArray<float> behaviourPreferredDepthMaxNorm;
        NativeArray<float> behaviourPreferredDepthWeight;
        NativeArray<float> behaviourPreferredDepthEdgeFraction;
        NativeArray<float> behaviourBodyRadius;
        NativeArray<int> behaviourCellSearchRadius;
        NativeArray<int> neighbourVisitStamp;
        NativeArray<float> behaviourSchoolSpacingFactor;
        NativeArray<float> behaviourSchoolOuterFactor;
        NativeArray<float> behaviourSchoolStrength;
        NativeArray<float> behaviourSchoolInnerSoftness;
        NativeArray<float> behaviourSchoolDeadzoneFraction;
        NativeArray<float> behaviourSchoolRadialDamping;   // NEW

        NativeParallelMultiHashMap<int, int> cellToAgents;
        NativeParallelMultiHashMap<int, int> cellToObstacles;

        FlockEnvironmentData environmentData;

        IFlockLogger logger;

        NativeArray<FlockAttractorData> attractors;
        NativeArray<float3> attractionSteering;
        int attractorCount;        // NEW: relationship-related behaviour arrays
        NativeArray<int> cellToIndividualAttractor;
        NativeArray<int> cellToGroupAttractor;
        NativeArray<float> cellIndividualPriority;
        NativeArray<float> cellGroupPriority;

        int obstacleCount;
        int gridCellCount;
        public int AgentCount { get; private set; }

        public bool IsCreated => positions.IsCreated;

        public NativeArray<float3> Positions => positions;
        public NativeArray<float3> Velocities => velocities;

        public int ObstacleCount => obstacleCount;

        public float GlobalWanderMultiplier { get; set; } = 1.0f;
        public float GlobalGroupNoiseMultiplier { get; set; } = 1.0f;
        public float GlobalPatternMultiplier { get; set; } = 1.0f;

        public void Initialize(
            int agentCount,
            FlockEnvironmentData environment,
            NativeArray<FlockBehaviourSettings> behaviourSettings,
            FlockObstacleData[] obstaclesSource,
            FlockAttractorData[] attractorsSource,       // NEW
            Allocator allocator,
            IFlockLogger logger) {

            this.logger = logger;

            AgentCount = agentCount;
            environmentData = environment;

            AllocateAgentArrays(allocator);
            AllocateBehaviourArrays(behaviourSettings, allocator);
            AllocateGrid(allocator);
            AllocateObstacles(obstaclesSource, allocator);
            AllocateObstacleSimulationData(allocator);

            // NEW: attractors
            AllocateAttractors(attractorsSource, allocator);
            AllocateAttractorSimulationData(allocator);
            RebuildAttractorGrid(); // NEW: stamp attractors into grid cells

            InitializeAgentsRandomInsideBounds();

            FlockLog.Info(
                this.logger,
                FlockLogCategory.Simulation,
                $"FlockSimulation.Initialize: agents={AgentCount}, gridResolution={environmentData.GridResolution}, " +
                $"obstacles={obstacleCount}, attractors={attractorCount}.",
                null);

            for (int index = 0; index < behaviourSettings.Length; index += 1) {
                FlockBehaviourSettings behaviour = behaviourSettings[index];

                if (behaviour.MaxSpeed <= 0.0f || behaviour.MaxAcceleration <= 0.0f) {
                    FlockLog.Warning(
                        this.logger,
                        FlockLogCategory.Simulation,
                        $"Behaviour[{index}] has MaxSpeed={behaviour.MaxSpeed} or MaxAcceleration={behaviour.MaxAcceleration} <= 0. Agents may not move.",
                        null);
                }

                if (behaviour.NeighbourRadius <= 0.0f) {
                    FlockLog.Warning(
                        this.logger,
                        FlockLogCategory.Simulation,
                        $"Behaviour[{index}] has NeighbourRadius={behaviour.NeighbourRadius} <= 0. Agents will not see neighbours.",
                        null);
                }
            }
        }

        public JobHandle ScheduleStepJobs(
            float deltaTime,
            JobHandle inputHandle = default) {

            if (AgentCount == 0) {
                FlockLog.Warning(
                    logger,
                    FlockLogCategory.Simulation,
                    "ScheduleStepJobs called but AgentCount is 0.",
                    null);
                return inputHandle;
            }

            // NEW: accumulate simulation time for noise
            simulationTime += deltaTime;

            if (!cellToAgents.IsCreated) {
                FlockLog.Error(
                    logger,
                    FlockLogCategory.Simulation,
                    "ScheduleStepJobs called but cellToAgents is not created.",
                    null);
                return inputHandle;
            }

            if (obstacleCount > 0
                && cellToObstacles.IsCreated
                && obstacles.IsCreated) {
                BuildObstacleGrid();
            }

            cellToAgents.Clear();

            // Snapshot previous velocities.
            var copyJob = new CopyVelocitiesJob {
                Source = velocities,
                Destination = prevVelocities,
            };

            JobHandle copyHandle = copyJob.Schedule(
                AgentCount,
                64,
                inputHandle);

            var clearStampsJob = new ClearIntArrayJob {
                Array = neighbourVisitStamp,
            };

            JobHandle clearStampsHandle = clearStampsJob.Schedule(
                AgentCount,
                64,
                inputHandle);

            var assignJob = new AssignToGridJob {
                Positions = positions,
                BehaviourIds = behaviourIds,
                BehaviourBodyRadius = behaviourBodyRadius,

                CellSize = environmentData.CellSize,
                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,

                CellToAgents = cellToAgents.AsParallelWriter(),
            };

            JobHandle assignHandle = assignJob.Schedule(
                AgentCount,
                64,
                inputHandle);

            var boundsJob = new BoundsProbeJob {
                Positions = positions,
                BehaviourIds = behaviourIds,
                BehaviourSeparationRadius = behaviourSeparationRadius,
                EnvironmentData = environmentData,
                WallDirections = wallDirections,
                WallDangers = wallDangers,
            };

            JobHandle boundsHandle = boundsJob.Schedule(
                AgentCount,
                64,
                inputHandle);

            // ---------- Obstacles ----------
            bool useObstacleAvoidance =
                obstacleCount > 0
                && cellToObstacles.IsCreated
                && obstacleSteering.IsCreated;

            JobHandle obstacleHandle = assignHandle;

            if (useObstacleAvoidance) {
                var obstacleJob = new ObstacleAvoidanceJob {
                    Positions = positions,
                    Velocities = velocities,

                    BehaviourIds = behaviourIds,
                    BehaviourMaxSpeed = behaviourMaxSpeed,
                    BehaviourMaxAcceleration = behaviourMaxAcceleration,
                    BehaviourSeparationRadius = behaviourSeparationRadius,

                    Obstacles = obstacles,
                    CellToObstacles = cellToObstacles,

                    GridOrigin = environmentData.GridOrigin,
                    GridResolution = environmentData.GridResolution,
                    CellSize = environmentData.CellSize,

                    AvoidStrength = DefaultObstacleAvoidStrength,
                    ObstacleSteering = obstacleSteering,
                };

                obstacleHandle = obstacleJob.Schedule(
                    AgentCount,
                    64,
                    assignHandle);
            }

            // ---------- Attraction (per-cell lookup) ----------
            bool useAttraction =
                attractorCount > 0
                && attractors.IsCreated
                && attractionSteering.IsCreated
                && cellToIndividualAttractor.IsCreated
                && gridCellCount > 0;

            JobHandle attractionHandle = assignHandle;

            if (useAttraction) {
                var attractionJob = new AttractorSamplingJob {
                    Positions = positions,
                    BehaviourIds = behaviourIds,

                    Attractors = attractors,
                    BehaviourAttractionWeight = behaviourAttractionWeight,

                    CellToIndividualAttractor = cellToIndividualAttractor,

                    GridOrigin = environmentData.GridOrigin,
                    GridResolution = environmentData.GridResolution,
                    CellSize = environmentData.CellSize,

                    AttractionSteering = attractionSteering,

                    // Preferred depth behaviour per type (normalised band)
                    BehaviourUsePreferredDepth = behaviourUsePreferredDepth,
                    BehaviourPreferredDepthMin = behaviourPreferredDepthMinNorm,
                    BehaviourPreferredDepthMax = behaviourPreferredDepthMaxNorm,
                    BehaviourDepthWinsOverAttractor = behaviourDepthWinsOverAttractor,
                };

                attractionHandle = attractionJob.Schedule(
                    AgentCount,
                    64,
                    assignHandle);
            }

            // Flock job waits for obstacle + attraction + velocity copy.
            JobHandle flockDeps = JobHandle.CombineDependencies(
                obstacleHandle,
                attractionHandle);

            flockDeps = JobHandle.CombineDependencies(
                flockDeps,
                boundsHandle);

            flockDeps = JobHandle.CombineDependencies(
                flockDeps,
                copyHandle);

            flockDeps = JobHandle.CombineDependencies(
                flockDeps,
                clearStampsHandle);

            // File: Assets/Flock/Runtime/FlockSimulation.cs
            // Inside ScheduleStepJobs, when constructing flockJob:

            var flockJob = new FlockStepJob {
                // Core agent data
                Positions = positions,
                PrevVelocities = prevVelocities,
                Velocities = velocities,

                // NEW: bounds probe outputs (per-agent)
                WallDirections = wallDirections,
                WallDangers = wallDangers,

                // NEW: per-behaviour bounds response
                BehaviourBoundsWeight = behaviourBoundsWeight,
                BehaviourBoundsTangentialDamping = behaviourBoundsTangentialDamping,
                BehaviourBoundsInfluenceSuppression = behaviourBoundsInfluenceSuppression,

                // Per-agent behaviour index
                BehaviourIds = behaviourIds,

                BehaviourBodyRadius = behaviourBodyRadius,
                BehaviourSchoolSpacingFactor = behaviourSchoolSpacingFactor,
                BehaviourSchoolOuterFactor = behaviourSchoolOuterFactor,
                BehaviourSchoolStrength = behaviourSchoolStrength,
                BehaviourSchoolInnerSoftness = behaviourSchoolInnerSoftness,
                BehaviourSchoolDeadzoneFraction = behaviourSchoolDeadzoneFraction,

                // Movement + neighbour radii
                BehaviourMaxSpeed = behaviourMaxSpeed,
                BehaviourMaxAcceleration = behaviourMaxAcceleration,
                BehaviourDesiredSpeed = behaviourDesiredSpeed,
                BehaviourNeighbourRadius = behaviourNeighbourRadius,
                BehaviourSeparationRadius = behaviourSeparationRadius,

                // Classic boids weights
                BehaviourAlignmentWeight = behaviourAlignmentWeight,
                BehaviourCohesionWeight = behaviourCohesionWeight,
                BehaviourSeparationWeight = behaviourSeparationWeight,
                BehaviourInfluenceWeight = behaviourInfluenceWeight,
                BehaviourGroupFlowWeight = behaviourGroupFlowWeight,

                // Cross-type relations
                BehaviourAvoidanceWeight = behaviourAvoidanceWeight,
                BehaviourNeutralWeight = behaviourNeutralWeight,
                BehaviourAvoidMask = behaviourAvoidMask,
                BehaviourNeutralMask = behaviourNeutralMask,

                // Leadership / grouping masks
                BehaviourLeadershipWeight = behaviourLeadershipWeight,
                BehaviourGroupMask = behaviourGroupMask,

                // Avoidance response + split behaviour
                BehaviourAvoidResponse = behaviourAvoidResponse,
                BehaviourSplitPanicThreshold = behaviourSplitPanicThreshold,
                BehaviourSplitLateralWeight = behaviourSplitLateralWeight,
                BehaviourSplitAccelBoost = behaviourSplitAccelBoost,

                // Group size / loner / crowding
                BehaviourMinGroupSize = behaviourMinGroupSize,
                BehaviourMaxGroupSize = behaviourMaxGroupSize,
                BehaviourGroupRadiusMultiplier = behaviourGroupRadiusMultiplier,
                BehaviourLonerRadiusMultiplier = behaviourLonerRadiusMultiplier,
                BehaviourLonerCohesionBoost = behaviourLonerCohesionBoost,
                BehaviourMinGroupSizeWeight = behaviourMinGroupSizeWeight,
                BehaviourMaxGroupSizeWeight = behaviourMaxGroupSizeWeight,

                // Preferred depth (normalised)
                BehaviourUsePreferredDepth = behaviourUsePreferredDepth,
                BehaviourPreferredDepthMin = behaviourPreferredDepthMinNorm,
                BehaviourPreferredDepthMax = behaviourPreferredDepthMaxNorm,
                BehaviourDepthBiasStrength = behaviourDepthBiasStrength,
                BehaviourDepthWinsOverAttractor = behaviourDepthWinsOverAttractor,
                BehaviourPreferredDepthMinNorm = behaviourPreferredDepthMinNorm,
                BehaviourPreferredDepthMaxNorm = behaviourPreferredDepthMaxNorm,
                BehaviourPreferredDepthWeight = behaviourPreferredDepthWeight,
                BehaviourPreferredDepthEdgeFraction = behaviourPreferredDepthEdgeFraction,
                BehaviourSchoolRadialDamping = behaviourSchoolRadialDamping, // NEW

                // 🔽🔽🔽 ADD THIS BLOCK (noise arrays) RIGHT AFTER THE LINE ABOVE 🔽🔽🔽
                BehaviourWanderStrength = behaviourWanderStrength,
                BehaviourWanderFrequency = behaviourWanderFrequency,
                BehaviourGroupNoiseStrength = behaviourGroupNoiseStrength,
                BehaviourPatternWeight = behaviourPatternWeight,
                // 🔼🔼🔼 END OF NEW BLOCK 🔼🔼🔼

                // NEW: per-type neighbour cell search radius
                BehaviourCellSearchRadius = behaviourCellSearchRadius,

                // NEW: per-agent dedup stamp so big fish in many cells are counted once
                NeighbourVisitStamp = neighbourVisitStamp,

                // Spatial grid
                CellToAgents = cellToAgents,
                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                // Environment + timestep
                EnvironmentData = environmentData,
                DeltaTime = deltaTime,

                // NEW: noise globals
                NoiseTime = simulationTime,
                GlobalWanderMultiplier = GlobalWanderMultiplier,
                GlobalGroupNoiseMultiplier = GlobalGroupNoiseMultiplier,
                GlobalPatternMultiplier = GlobalPatternMultiplier,

                // NEW: pattern steering (optional)
                PatternSteering = patternSteering,

                // Obstacles
                UseObstacleAvoidance = useObstacleAvoidance,
                ObstacleAvoidWeight = DefaultObstacleAvoidWeight,
                ObstacleSteering = obstacleSteering,

                // Attractors
                UseAttraction = useAttraction,
                GlobalAttractionWeight = DefaultAttractionWeight,
                AttractionSteering = attractionSteering,
            };

            JobHandle flockHandle = flockJob.Schedule(
                AgentCount,
                64,
                flockDeps);

            var integrateJob = new IntegrateJob {
                Positions = positions,
                Velocities = velocities,
                EnvironmentData = environmentData,
                DeltaTime = deltaTime,
            };

            JobHandle integrateHandle = integrateJob.Schedule(
                AgentCount,
                64,
                flockHandle);

            return integrateHandle;
        }

        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // FIX 2: dispose bounds + wall arrays to avoid leaks

        public void Dispose() {
            DisposeArray(ref positions);
            DisposeArray(ref velocities);
            DisposeArray(ref prevVelocities);
            DisposeArray(ref behaviourIds);
            DisposeArray(ref wallDirections); // NEW
            DisposeArray(ref wallDangers);    // NEW

            DisposeArray(ref behaviourMaxSpeed);
            DisposeArray(ref behaviourMaxAcceleration);
            DisposeArray(ref behaviourDesiredSpeed);
            DisposeArray(ref behaviourNeighbourRadius);
            DisposeArray(ref behaviourSeparationRadius);
            DisposeArray(ref behaviourAlignmentWeight);
            DisposeArray(ref behaviourCohesionWeight);
            DisposeArray(ref behaviourSeparationWeight);
            DisposeArray(ref behaviourInfluenceWeight);
            DisposeArray(ref behaviourLeadershipWeight);
            DisposeArray(ref behaviourGroupMask);
            DisposeArray(ref behaviourGroupFlowWeight);

            // Bounds behaviour – NEW
            DisposeArray(ref behaviourBoundsWeight);
            DisposeArray(ref behaviourBoundsTangentialDamping);
            DisposeArray(ref behaviourBoundsInfluenceSuppression);

            // Relationship-related
            DisposeArray(ref behaviourAvoidanceWeight);
            DisposeArray(ref behaviourNeutralWeight);
            DisposeArray(ref behaviourAttractionWeight);
            DisposeArray(ref behaviourAvoidResponse);
            DisposeArray(ref behaviourAvoidMask);
            DisposeArray(ref behaviourNeutralMask);

            // Preferred depth
            DisposeArray(ref behaviourPreferredDepthMinNorm);
            DisposeArray(ref behaviourPreferredDepthMaxNorm);
            DisposeArray(ref behaviourPreferredDepthWeight);

            // Obstacles
            DisposeArray(ref obstacles);
            DisposeArray(ref obstacleSteering);

            DisposeArray(ref behaviourSplitPanicThreshold);
            DisposeArray(ref behaviourSplitLateralWeight);
            DisposeArray(ref behaviourSplitAccelBoost);

            DisposeArray(ref behaviourSchoolSpacingFactor);
            DisposeArray(ref behaviourSchoolOuterFactor);
            DisposeArray(ref behaviourSchoolStrength);
            DisposeArray(ref behaviourSchoolInnerSoftness);
            DisposeArray(ref behaviourSchoolDeadzoneFraction);

            // Grouping
            DisposeArray(ref behaviourMinGroupSize);
            DisposeArray(ref behaviourMaxGroupSize);
            DisposeArray(ref behaviourGroupRadiusMultiplier);
            DisposeArray(ref behaviourLonerRadiusMultiplier);
            DisposeArray(ref behaviourLonerCohesionBoost);

            DisposeArray(ref behaviourUsePreferredDepth);
            DisposeArray(ref behaviourPreferredDepthMin);
            DisposeArray(ref behaviourPreferredDepthMax);
            DisposeArray(ref behaviourDepthBiasStrength);
            DisposeArray(ref behaviourDepthWinsOverAttractor);
            DisposeArray(ref behaviourPreferredDepthEdgeFraction);
            DisposeArray(ref behaviourSchoolRadialDamping);   // NEW

            DisposeArray(ref behaviourBodyRadius);
            DisposeArray(ref behaviourCellSearchRadius);
            DisposeArray(ref neighbourVisitStamp);
            DisposeArray(ref behaviourMinGroupSizeWeight);   // NEW
            DisposeArray(ref behaviourMaxGroupSizeWeight);   // NEW

            // NEW: noise arrays
            DisposeArray(ref behaviourWanderStrength);
            DisposeArray(ref behaviourWanderFrequency);
            DisposeArray(ref behaviourGroupNoiseStrength);
            DisposeArray(ref behaviourPatternWeight);

            // NEW: per-agent pattern steering
            DisposeArray(ref patternSteering);

            if (cellToAgents.IsCreated) {
                cellToAgents.Dispose();
            }

            if (cellToObstacles.IsCreated) {
                cellToObstacles.Dispose();
            }

            // Attractors
            DisposeArray(ref attractors);
            DisposeArray(ref attractionSteering);

            // Per-cell attractor lookup arrays
            DisposeArray(ref cellToIndividualAttractor);
            DisposeArray(ref cellToGroupAttractor);
            DisposeArray(ref cellIndividualPriority);
            DisposeArray(ref cellGroupPriority);

            AgentCount = 0;
        }

        public void SetObstacleData(
            int index,
            FlockObstacleData data) {
            if (!obstacles.IsCreated) {
                return;
            }

            if ((uint)index >= (uint)obstacles.Length) {
                return;
            }

            obstacles[index] = data;
        }

        void AllocateAgentArrays(Allocator allocator) {
            positions = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            velocities = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            prevVelocities = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourIds = new NativeArray<int>(
                AgentCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            patternSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            neighbourVisitStamp = new NativeArray<int>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            wallDirections = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            wallDangers = new NativeArray<float>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            // Initialise with safe defaults (type 0) – controller will overwrite.
            for (int index = 0; index < AgentCount; index += 1) {
                behaviourIds[index] = 0;
            }
        }

        // =======================================================
        // 5) FlockSimulation.AllocateBehaviourArrays – REPLACE BODY
        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // =======================================================
        void AllocateBehaviourArrays(
            NativeArray<FlockBehaviourSettings> settings,
            Allocator allocator) {

            int behaviourCount = settings.Length;

            behaviourMaxSpeed = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMaxAcceleration = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourDesiredSpeed = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourNeighbourRadius = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSeparationRadius = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourAlignmentWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourCohesionWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSeparationWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourInfluenceWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourLeadershipWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupMask = new NativeArray<uint>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupFlowWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            // Relations
            behaviourAvoidanceWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourNeutralWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourAttractionWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourAvoidResponse = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourAvoidMask = new NativeArray<uint>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourNeutralMask = new NativeArray<uint>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            // Split behaviour
            behaviourSplitPanicThreshold = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSplitLateralWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSplitAccelBoost = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            // Grouping
            behaviourMinGroupSize = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMaxGroupSize = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupRadiusMultiplier = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourLonerRadiusMultiplier = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourLonerCohesionBoost = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMinGroupSizeWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMaxGroupSizeWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            // Preferred depth
            behaviourUsePreferredDepth = new NativeArray<byte>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourPreferredDepthMin = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourPreferredDepthMax = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourDepthBiasStrength = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourDepthWinsOverAttractor = new NativeArray<byte>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourPreferredDepthMinNorm = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourPreferredDepthMaxNorm = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourPreferredDepthWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourPreferredDepthEdgeFraction = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourCellSearchRadius = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourBodyRadius = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSchoolSpacingFactor = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSchoolOuterFactor = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSchoolStrength = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSchoolInnerSoftness = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSchoolDeadzoneFraction = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSchoolRadialDamping = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory); // NEW

            behaviourWanderStrength = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourWanderFrequency = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupNoiseStrength = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourPatternWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            // NEW: bounds per-behaviour
            behaviourBoundsWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourBoundsTangentialDamping = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourBoundsInfluenceSuppression = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);


            for (int index = 0; index < behaviourCount; index += 1) {
                FlockBehaviourSettings behaviour = settings[index];

                behaviourMaxSpeed[index] = behaviour.MaxSpeed;
                behaviourMaxAcceleration[index] = behaviour.MaxAcceleration;
                behaviourDesiredSpeed[index] = behaviour.DesiredSpeed;

                behaviourNeighbourRadius[index] = behaviour.NeighbourRadius;
                behaviourSeparationRadius[index] = behaviour.SeparationRadius;

                // NEW: body radius for occupancy
                behaviourBodyRadius[index] = math.max(0f, behaviour.BodyRadius);

                behaviourSchoolSpacingFactor[index] =
                    math.max(0.5f, behaviour.SchoolingSpacingFactor);

                behaviourSchoolOuterFactor[index] =
                    math.max(1f, behaviour.SchoolingOuterFactor);

                behaviourSchoolStrength[index] =
                    math.max(0f, behaviour.SchoolingStrength);

                behaviourSchoolInnerSoftness[index] =
                    math.clamp(behaviour.SchoolingInnerSoftness, 0f, 1f);

                behaviourSchoolDeadzoneFraction[index] =
                    math.clamp(behaviour.SchoolingDeadzoneFraction, 0f, 0.5f);

                behaviourSchoolRadialDamping[index] =
                    math.max(0f, behaviour.SchoolingRadialDamping);

                behaviourBoundsWeight[index] =
                   math.max(0f, behaviour.BoundsWeight);

                behaviourBoundsTangentialDamping[index] =
                    math.max(0f, behaviour.BoundsTangentialDamping);

                behaviourBoundsInfluenceSuppression[index] =
                    math.max(0f, behaviour.BoundsInfluenceSuppression);

                behaviourWanderStrength[index] =
                    math.max(0f, behaviour.WanderStrength);

                behaviourWanderFrequency[index] =
                    math.max(0f, behaviour.WanderFrequency);

                behaviourGroupNoiseStrength[index] =
                    math.max(0f, behaviour.GroupNoiseStrength);

                behaviourPatternWeight[index] =
                    math.max(0f, behaviour.PatternWeight);

                // NEW: per-type cell search radius (in grid cells), derived from neighbour radius
                float cellSize = math.max(environmentData.CellSize, 0.0001f);
                float viewRadius = math.max(0f, behaviour.NeighbourRadius);

                int cellRange = (int)math.ceil(viewRadius / cellSize);
                if (cellRange < 1) {
                    cellRange = 1;
                }

                behaviourCellSearchRadius[index] = cellRange;

                behaviourNeighbourRadius[index] = behaviour.NeighbourRadius;
                behaviourSeparationRadius[index] = behaviour.SeparationRadius;

                behaviourAlignmentWeight[index] = behaviour.AlignmentWeight;
                behaviourCohesionWeight[index] = behaviour.CohesionWeight;
                behaviourSeparationWeight[index] = behaviour.SeparationWeight;

                behaviourInfluenceWeight[index] = behaviour.InfluenceWeight;
                behaviourGroupFlowWeight[index] = behaviour.GroupFlowWeight;

                behaviourLeadershipWeight[index] = behaviour.LeadershipWeight;
                behaviourGroupMask[index] = behaviour.GroupMask;

                behaviourAvoidanceWeight[index] = behaviour.AvoidanceWeight;
                behaviourNeutralWeight[index] = behaviour.NeutralWeight;
                behaviourAttractionWeight[index] = behaviour.AttractionWeight;
                behaviourAvoidResponse[index] = behaviour.AvoidResponse;

                behaviourAvoidMask[index] = behaviour.AvoidMask;
                behaviourNeutralMask[index] = behaviour.NeutralMask;

                // Split
                behaviourSplitPanicThreshold[index] = behaviour.SplitPanicThreshold;
                behaviourSplitLateralWeight[index] = behaviour.SplitLateralWeight;
                behaviourSplitAccelBoost[index] = behaviour.SplitAccelBoost;

                // Grouping
                behaviourMinGroupSize[index] = behaviour.MinGroupSize;
                behaviourMaxGroupSize[index] = behaviour.MaxGroupSize;
                behaviourGroupRadiusMultiplier[index] = behaviour.GroupRadiusMultiplier;
                behaviourLonerRadiusMultiplier[index] = behaviour.LonerRadiusMultiplier;
                behaviourLonerCohesionBoost[index] = behaviour.LonerCohesionBoost;
                behaviourMinGroupSizeWeight[index] = behaviour.MinGroupSizeWeight;
                behaviourMaxGroupSizeWeight[index] = behaviour.MaxGroupSizeWeight;

                // Preferred depth (all normalised [0..1] against environment bounds)
                byte useDepth = behaviour.UsePreferredDepth != 0 ? (byte)1 : (byte)0;
                byte depthWins = behaviour.DepthWinsOverAttractor != 0 ? (byte)1 : (byte)0;

                behaviourUsePreferredDepth[index] = useDepth;
                behaviourPreferredDepthMin[index] = behaviour.PreferredDepthMinNorm;
                behaviourPreferredDepthMax[index] = behaviour.PreferredDepthMaxNorm;
                behaviourDepthBiasStrength[index] = behaviour.DepthBiasStrength;
                behaviourDepthWinsOverAttractor[index] = depthWins;

                behaviourPreferredDepthMinNorm[index] = behaviour.PreferredDepthMinNorm;
                behaviourPreferredDepthMaxNorm[index] = behaviour.PreferredDepthMaxNorm;
                behaviourPreferredDepthWeight[index] = behaviour.PreferredDepthWeight;
                behaviourPreferredDepthEdgeFraction[index] = behaviour.PreferredDepthEdgeFraction;
            }
        }

        void AllocateAttractors(
                   FlockAttractorData[] source,
                   Allocator allocator) {

            if (source == null || source.Length == 0) {
                attractorCount = 0;
                attractors = default;
                return;
            }

            attractorCount = source.Length;

            attractors = new NativeArray<FlockAttractorData>(
                attractorCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            // Environment vertical range for normalisation
            float envMinY = environmentData.BoundsCenter.y - environmentData.BoundsExtents.y;
            float envMaxY = environmentData.BoundsCenter.y + environmentData.BoundsExtents.y;
            float envHeight = math.max(envMaxY - envMinY, 0.0001f);

            for (int index = 0; index < attractorCount; index += 1) {
                FlockAttractorData data = source[index];

                float worldMinY;
                float worldMaxY;

                if (data.Shape == FlockAttractorShape.Sphere) {
                    // Simple sphere: radius in Y
                    worldMinY = data.Position.y - data.Radius;
                    worldMaxY = data.Position.y + data.Radius;
                } else {
                    // Box: approximate vertical extent using rotated half-extents
                    quaternion rot = data.BoxRotation;
                    float3 halfExtents = data.BoxHalfExtents;

                    float3 right = math.mul(rot, new float3(1f, 0f, 0f));
                    float3 up = math.mul(rot, new float3(0f, 1f, 0f));
                    float3 fwd = math.mul(rot, new float3(0f, 0f, 1f));

                    float extentY =
                        math.abs(right.y) * halfExtents.x +
                        math.abs(up.y) * halfExtents.y +
                        math.abs(fwd.y) * halfExtents.z;

                    worldMinY = data.Position.y - extentY;
                    worldMaxY = data.Position.y + extentY;
                }

                float depthMinNorm = math.saturate((worldMinY - envMinY) / envHeight);
                float depthMaxNorm = math.saturate((worldMaxY - envMinY) / envHeight);

                if (depthMaxNorm < depthMinNorm) {
                    float tmp = depthMinNorm;
                    depthMinNorm = depthMaxNorm;
                    depthMaxNorm = tmp;
                }

                data.DepthMinNorm = depthMinNorm;
                data.DepthMaxNorm = depthMaxNorm;

                attractors[index] = data;
            }
        }

        void AllocateAttractorSimulationData(Allocator allocator) {
            if (attractorCount <= 0) {
                attractionSteering = default;
                return;
            }

            attractionSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);
        }

        public void SetAttractorData(
            int index,
            FlockAttractorData data) {
            if (!attractors.IsCreated) {
                return;
            }

            if ((uint)index >= (uint)attractors.Length) {
                return;
            }

            attractors[index] = data;
        }

        void AllocateGrid(Allocator allocator) {
            gridCellCount = environmentData.GridResolution.x
                            * environmentData.GridResolution.y
                            * environmentData.GridResolution.z;

            int capacity = math.max(
                AgentCount * DefaultGridCapacityMultiplier,
                gridCellCount);

            cellToAgents = new NativeParallelMultiHashMap<int, int>(
                capacity,
                allocator);

            if (gridCellCount <= 0) {
                cellToIndividualAttractor = default;
                cellToGroupAttractor = default;
                cellIndividualPriority = default;
                cellGroupPriority = default;
                return;
            }

            // Per-cell attractor indices
            cellToIndividualAttractor = new NativeArray<int>(
                gridCellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            cellToGroupAttractor = new NativeArray<int>(
                gridCellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            cellIndividualPriority = new NativeArray<float>(
                gridCellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            cellGroupPriority = new NativeArray<float>(
                gridCellCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            // Init with "no attractor" and lowest priority
            for (int i = 0; i < gridCellCount; i += 1) {
                cellToIndividualAttractor[i] = -1;
                cellToGroupAttractor[i] = -1;
                cellIndividualPriority[i] = float.NegativeInfinity;
                cellGroupPriority[i] = float.NegativeInfinity;
            }
        }

        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // NEW METHOD: stamps Individual / Group attractors into grid cells
        public void RebuildAttractorGrid() {
            if (!cellToIndividualAttractor.IsCreated
                || !cellToGroupAttractor.IsCreated
                || !cellIndividualPriority.IsCreated
                || !cellGroupPriority.IsCreated
                || gridCellCount <= 0) {
                return;
            }

            // Reset per-cell mappings
            for (int i = 0; i < gridCellCount; i += 1) {
                cellToIndividualAttractor[i] = -1;
                cellToGroupAttractor[i] = -1;
                cellIndividualPriority[i] = float.NegativeInfinity;
                cellGroupPriority[i] = float.NegativeInfinity;
            }

            if (!attractors.IsCreated || attractorCount <= 0) {
                return;
            }

            float cellSize = math.max(environmentData.CellSize, 0.0001f);
            float3 origin = environmentData.GridOrigin;
            int3 res = environmentData.GridResolution;
            int layerSize = res.x * res.y;

            for (int index = 0; index < attractorCount; index += 1) {
                FlockAttractorData data = attractors[index];

                // Bounding sphere around the volume
                float radius = math.max(data.Radius, cellSize);
                float3 minPos = data.Position - new float3(radius);
                float3 maxPos = data.Position + new float3(radius);

                float3 minLocal = (minPos - origin) / cellSize;
                float3 maxLocal = (maxPos - origin) / cellSize;

                int3 minCell = (int3)math.floor(minLocal);
                int3 maxCell = (int3)math.floor(maxLocal);

                int3 minClamp = new int3(0, 0, 0);
                int3 maxClamp = res - new int3(1, 1, 1);

                minCell = math.clamp(minCell, minClamp, maxClamp);
                maxCell = math.clamp(maxCell, minClamp, maxClamp);

                for (int z = minCell.z; z <= maxCell.z; z += 1) {
                    for (int y = minCell.y; y <= maxCell.y; y += 1) {
                        int rowBase = y * res.x + z * layerSize;

                        for (int x = minCell.x; x <= maxCell.x; x += 1) {
                            int cellIndex = x + rowBase;

                            if (data.Usage == FlockAttractorUsage.Individual) {
                                if (data.CellPriority > cellIndividualPriority[cellIndex]) {
                                    cellIndividualPriority[cellIndex] = data.CellPriority;
                                    cellToIndividualAttractor[cellIndex] = index;
                                }
                            } else if (data.Usage == FlockAttractorUsage.Group) {
                                if (data.CellPriority > cellGroupPriority[cellIndex]) {
                                    cellGroupPriority[cellIndex] = data.CellPriority;
                                    cellToGroupAttractor[cellIndex] = index;
                                }
                            }
                        }
                    }
                }
            }
        }

        void AllocateObstacles(
            FlockObstacleData[] source,
            Allocator allocator) {
            if (source == null || source.Length == 0) {
                obstacleCount = 0;
                obstacles = default;

                FlockLog.Warning(
                    logger,
                    FlockLogCategory.Simulation,
                    "AllocateObstacles: source is null or empty. No obstacles will be used.",
                    null);

                return;
            }

            obstacleCount = source.Length;

            obstacles = new NativeArray<FlockObstacleData>(
                obstacleCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            for (int index = 0; index < obstacleCount; index += 1) {
                obstacles[index] = source[index];
            }

            FlockLog.Info(
                logger,
                FlockLogCategory.Simulation,
                $"AllocateObstacles: created {obstacleCount} obstacles.",
                null);
        }

        void AllocateObstacleSimulationData(Allocator allocator) {
            if (obstacleCount <= 0) {
                obstacleSteering = default;
                cellToObstacles = default;
                return;
            }

            obstacleSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            int capacity = math.max(
                obstacleCount * DefaultGridCapacityMultiplier,
                gridCellCount);

            cellToObstacles = new NativeParallelMultiHashMap<int, int>(
                capacity,
                allocator);
        }

        void BuildObstacleGrid() {
            if (!cellToObstacles.IsCreated || !obstacles.IsCreated || obstacleCount <= 0) {
                return;
            }

            cellToObstacles.Clear();

            for (int index = 0; index < obstacleCount; index += 1) {
                int cellIndex = GetCellIndexForPosition(obstacles[index].Position);
                if (cellIndex < 0) {
                    continue;
                }

                cellToObstacles.Add(cellIndex, index);
            }
        }

        public void SetAgentBehaviourIds(int[] behaviourIdsSource) {
            if (!behaviourIds.IsCreated) {
                return;
            }

            if (behaviourIdsSource == null) {
                return;
            }

            int count = AgentCount;

            if (behaviourIdsSource.Length < count) {
                count = behaviourIdsSource.Length;
            }

            for (int i = 0; i < count; i += 1) {
                behaviourIds[i] = behaviourIdsSource[i];
            }
        }

        int GetCellIndexForPosition(float3 position) {
            float cellSize = math.max(environmentData.CellSize, 0.0001f);
            float3 local = (position - environmentData.GridOrigin) / cellSize;

            int3 cell = (int3)math.floor(local);
            int3 res = environmentData.GridResolution;

            if (cell.x < 0 || cell.y < 0 || cell.z < 0
                || cell.x >= res.x || cell.y >= res.y || cell.z >= res.z) {
                return -1;
            }

            int layerSize = res.x * res.y;
            int index = cell.x + cell.y * res.x + cell.z * layerSize;
            return index;
        }

        void InitializeAgentsRandomInsideBounds() {
            float3 center = environmentData.BoundsCenter;
            float3 extents = environmentData.BoundsExtents;

            Unity.Mathematics.Random random = new Unity.Mathematics.Random(1234567u);

            float baseSpeed = 1.0f;
            if (behaviourMaxSpeed.IsCreated && behaviourMaxSpeed.Length > 0) {
                baseSpeed = math.max(0.1f, behaviourMaxSpeed[0] * 0.5f);
            }

            for (int index = 0; index < AgentCount; index += 1) {
                float3 randomOffset = random.NextFloat3(-extents, extents);
                positions[index] = center + randomOffset;

                float3 direction = math.normalize(random.NextFloat3(-1.0f, 1.0f));
                if (math.lengthsq(direction) < 1e-4f) {
                    direction = new float3(0.0f, 0.0f, 1.0f);
                }

                float3 v = direction * baseSpeed;
                velocities[index] = v;

                if (prevVelocities.IsCreated) {
                    prevVelocities[index] = v;
                }
            }

            FlockLog.Info(
                logger,
                FlockLogCategory.Simulation,
                $"Initialized {AgentCount} agents with random positions and baseSpeed={baseSpeed}.",
                null);
        }

        static void DisposeArray<T>(ref NativeArray<T> array)
            where T : struct {
            if (!array.IsCreated) {
                return;
            }

            array.Dispose();
            array = default;
        }
    }
}
