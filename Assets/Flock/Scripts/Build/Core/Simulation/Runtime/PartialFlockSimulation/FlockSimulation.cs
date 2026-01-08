// File: Assets/Flock/Runtime/FlockSimulation.cs
namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockSimulation {
    using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
    using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;
    using Flock.Scripts.Build.Influence.Environment.Attractors.Jobs;
    using Flock.Scripts.Build.Influence.Environment.Obstacles.Jobs;
    using Flock.Scripts.Build.Influence.Environment.Bounds.Jobs;
    using Flock.Scripts.Build.Influence.Environment.Bounds.Data;
    using Flock.Scripts.Build.Influence.PatternVolume.Data;
    using Flock.Scripts.Build.Influence.PatternVolume.Jobs;
    using Flock.Scripts.Build.Infrastructure.Grid.Data;
    using Flock.Scripts.Build.Influence.Noise.Profiles;
    using Flock.Scripts.Build.Infrastructure.Grid.Jobs;
    using Flock.Scripts.Build.Influence.Noise.Data;
    using Flock.Scripts.Build.Influence.Noise.Jobs;
    using Flock.Scripts.Build.Agents.Fish.Data;
    using Flock.Scripts.Build.Agents.Fish.Jobs;
    using Flock.Scripts.Build.Utilities.Jobs;

    using System;
    using Unity.Jobs;
    using Unity.Collections;
    using Unity.Mathematics;
    using Flock.Scripts.Build.Debug;
    using System.Collections.Generic;
    using Flock.Scripts.Build.Influence.Environment.Data;

    /**
    * <summary>
    * Core flock simulation runtime. Owns native buffers and schedules the per-frame job graph.
    * </summary>
    */
    public sealed partial class FlockSimulation {
        #region Constants

        private const float DefaultObstacleAvoidStrength = 2.0f;
        private const float DefaultObstacleAvoidWeight = 1.0f;
        private const float DefaultAttractionWeight = 1.0f;

        #endregion

        #region Public Properties

        public int AgentCount { get; private set; }

        public bool IsCreated => positions.IsCreated;
        public NativeArray<float3> Positions => positions;
        public NativeArray<float3> Velocities => velocities;
        public float GlobalWanderMultiplier { get; set; } = 1.0f;
        public float GlobalGroupNoiseMultiplier { get; set; } = 1.0f;
        public float GlobalPatternMultiplier { get; set; } = 1.0f;
        public float GroupNoiseFrequency { get; set; } = 0.3f;

        #endregion

        #region Private Fields

        // Core agent state.
        private NativeArray<float3> positions;
        private NativeArray<float3> velocities;
        private NativeArray<float3> prevVelocities;
        private NativeArray<int> behaviourIds;
        private NativeArray<FlockBehaviourSettings> behaviourSettings;
        private NativeArray<int> behaviourCellSearchRadius;
        private float simulationTime;

        // Environment / bounds response.
        private FlockEnvironmentData environmentData;
        private NativeArray<float3> wallDirections;
        private NativeArray<float> wallDangers;

        // Patterns.
        // Patterns.
        private NativeArray<float3> patternSteering;
        private NativeArray<float3> cellGroupNoise;

        // was: FlockLayer3PatternCommand / SphereShell / BoxShell
        private PatternVolumeCommand[] layer3PatternCommands = Array.Empty<PatternVolumeCommand>();
        private PatternVolumeSphereShell[] layer3SphereShells = Array.Empty<PatternVolumeSphereShell>();
        private PatternVolumeBoxShell[] layer3BoxShells = Array.Empty<PatternVolumeBoxShell>();

        private float3 patternSphereCenter;
        private float patternSphereRadius;
        private float patternSphereThickness;
        private float patternSphereStrength;
        private uint patternSphereBehaviourMask;

        private readonly List<int> runtimeLayer3Active = new List<int>(16);

        // was: List<FlockLayer3PatternBoxShell> / SphereShell
        private readonly List<PatternVolumeBoxShell> runtimeBoxShells =
            new List<PatternVolumeBoxShell>(16);
        private readonly List<PatternVolumeSphereShell> runtimeSphereShells =
            new List<PatternVolumeSphereShell>(16);

        private readonly List<RuntimePatternVolumeInstance> runtimeLayer3Patterns =
            new List<RuntimePatternVolumeInstance>(16);

        private readonly Stack<int> runtimeBoxShellFree = new Stack<int>(16);
        private readonly Stack<int> runtimeSphereShellFree = new Stack<int>(16);
        private readonly Stack<int> runtimeLayer3Free = new Stack<int>(16);

        // Obstacles.
        private NativeArray<FlockObstacleData> obstacles;
        private NativeArray<float3> obstacleSteering;
        private NativeParallelMultiHashMap<int, int> cellToObstacles;
        private readonly List<IndexedObstacleChange> pendingObstacleChanges =
            new List<IndexedObstacleChange>(32);

        private int obstacleCount;
        private bool obstacleGridDirty;

        // Attractors.
        private NativeArray<FlockAttractorData> attractors;
        private NativeArray<float3> attractionSteering;
        private NativeArray<int> cellToIndividualAttractor;
        private NativeArray<int> cellToGroupAttractor;
        private NativeArray<float> cellIndividualPriority;
        private NativeArray<float> cellGroupPriority;
        private readonly List<IndexedAttractorChange> pendingAttractorChanges =
            new List<IndexedAttractorChange>(32);

        private int attractorCount;
        private bool attractorGridDirty;

        // Grid / neighbour sampling.

        private NativeArray<int> cellAgentStarts;
        private NativeArray<int> cellAgentCounts;
        private NativeArray<CellAgentPair> cellAgentPairs;
        private NativeArray<int> agentCellCounts;
        private NativeArray<int> agentCellIds;
        private NativeArray<int> agentEntryStarts;
        private NativeArray<int> totalAgentPairCount;
        private NativeArray<int> touchedAgentCells;
        private NativeArray<int> touchedAgentCellCount;
        private NativeArray<NeighbourAggregate> neighbourAggregates;

        private int gridCellCount;
        private int maxCellsPerAgent;

        // Job safety / staging.
        private JobHandle inFlightHandle;

        private bool pendingBehaviourIdsDirty;
        private int pendingBehaviourIdsCount;
        private int[] pendingBehaviourIdsManaged;

        // Logging / services.
        private IFlockLogger logger;

        // Layer-2 group noise active state.
        private FlockGroupNoisePatternType activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.SimpleSine;
        private FlockGroupNoiseCommonSettings activeLayer2GroupNoiseCommon = FlockGroupNoiseCommonSettings.Default;
        private FlockGroupNoiseSimpleSinePayload activeLayer2SimpleSine = FlockGroupNoiseSimpleSinePayload.Default;
        private FlockGroupNoiseVerticalBandsPayload activeLayer2VerticalBands = FlockGroupNoiseVerticalBandsPayload.Default;
        private GroupNoiseVortexPayload activeLayer2Vortex = GroupNoiseVortexPayload.Default;
        private FlockGroupNoiseSphereShellPayload activeLayer2SphereShell = FlockGroupNoiseSphereShellPayload.Default;

        #endregion

        #region Public API

        /**
        * <summary>
        * Initializes (or re-initializes) the simulation, allocating all native buffers and building initial grids.
        * </summary>
        * <param name="agentCount">Total number of agents to simulate.</param>
        * <param name="environment">Environment configuration (bounds/grid).</param>
        * <param name="behaviourSettings">Per-behaviour parameter array.</param>
        * <param name="obstaclesSource">Initial obstacle data.</param>
        * <param name="attractorsSource">Initial attractor data.</param>
        * <param name="allocator">Allocator used for native buffers.</param>
        * <param name="logger">Logger sink.</param>
        */
        public void Initialize(
            int agentCount,
            FlockEnvironmentData environment,
            NativeArray<FlockBehaviourSettings> behaviourSettings,
            FlockObstacleData[] obstaclesSource,
            FlockAttractorData[] attractorsSource,
            Allocator allocator,
            IFlockLogger logger) {

            // If someone re-initializes, do it safely.
            if (IsCreated) {
                inFlightHandle.Complete();
                Dispose();
            }

            this.logger = logger;

            AgentCount = agentCount;
            environmentData = environment;

            simulationTime = 0f;
            inFlightHandle = default;

            // Reset Layer-2 active pattern state (optional, but deterministic)
            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.SimpleSine;
            activeLayer2GroupNoiseCommon = FlockGroupNoiseCommonSettings.Default;
            activeLayer2SimpleSine = FlockGroupNoiseSimpleSinePayload.Default;
            activeLayer2VerticalBands = FlockGroupNoiseVerticalBandsPayload.Default;
            activeLayer2Vortex = GroupNoiseVortexPayload.Default;
            activeLayer2SphereShell = FlockGroupNoiseSphereShellPayload.Default;

            // Reset Layer-3 baked + runtime state
            layer3PatternCommands = Array.Empty<PatternVolumeCommand>();
            layer3SphereShells = Array.Empty<PatternVolumeSphereShell>();
            layer3BoxShells = Array.Empty<PatternVolumeBoxShell>();

            runtimeLayer3Patterns.Clear();
            runtimeLayer3Active.Clear();
            runtimeLayer3Free.Clear();
            runtimeSphereShells.Clear();
            runtimeSphereShellFree.Clear();
            runtimeBoxShells.Clear();
            runtimeBoxShellFree.Clear();

            // Pattern sphere defaults
            patternSphereCenter = environmentData.BoundsCenter;
            patternSphereRadius = 0f;
            patternSphereThickness = 0f;
            patternSphereStrength = 0f;
            patternSphereBehaviourMask = uint.MaxValue;

            // Pending-change state (used by ScheduleStepJobs staging)
            pendingObstacleChanges.Clear();
            pendingAttractorChanges.Clear();

            pendingBehaviourIdsDirty = false;
            pendingBehaviourIdsCount = 0;
            pendingBehaviourIdsManaged = (AgentCount > 0) ? new int[AgentCount] : Array.Empty<int>();

            obstacleGridDirty = true;
            attractorGridDirty = true;

            // Allocate all native buffers
            AllocateAgentArrays(allocator);
            AllocateBehaviourArrays(behaviourSettings, allocator);
            AllocateGrid(allocator);

            AllocateObstacles(obstaclesSource, allocator);
            AllocateObstacleSimulationData(allocator);

            AllocateAttractors(attractorsSource, allocator);
            AllocateAttractorSimulationData(allocator);

            // Build initial grids ONCE during init (safe: no jobs running yet).
            if (obstacleCount > 0 && cellToObstacles.IsCreated && obstacles.IsCreated && gridCellCount > 0) {
                BuildObstacleGrid();
                obstacleGridDirty = false;
            }

            if (attractorCount > 0
                && attractors.IsCreated
                && cellToIndividualAttractor.IsCreated
                && cellToGroupAttractor.IsCreated
                && cellIndividualPriority.IsCreated
                && cellGroupPriority.IsCreated
                && gridCellCount > 0) {
                attractorGridDirty = true;
            } else {
                attractorGridDirty = false;
            }

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

        /**
        * <summary>
        * Schedules the simulation step jobs for this frame and returns the final dependency handle.
        * </summary>
        * <param name="deltaTime">Frame delta time.</param>
        * <param name="inputHandle">Caller-provided dependencies.</param>
        * <returns>Handle for the scheduled job chain.</returns>
        */
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

            simulationTime += deltaTime;

            JobHandle frameDeps = JobHandle.CombineDependencies(inputHandle, inFlightHandle);

            if (!ValidatePackedAgentGridCreated()) {
                FlockLog.Error(
                    logger,
                    FlockLogCategory.Simulation,
                    "ScheduleStepJobs called but packed agent grid is not created.",
                    null);
                return frameDeps;
            }

            JobHandle applyDeps = ScheduleApplyPendingChanges(frameDeps);

            JobHandle clearPatternHandle = ScheduleClearPatternSteering(applyDeps);

            bool useObstacleAvoidance = ShouldUseObstacleAvoidance();
            JobHandle obstacleGridHandle = ScheduleRebuildObstacleGridIfDirty(applyDeps, useObstacleAvoidance);

            bool useAttraction = ShouldUseAttraction();
            JobHandle attractorGridHandle = ScheduleRebuildAttractorGridIfDirty(applyDeps, useAttraction);

            JobHandle assignHandle = ScheduleRebuildPackedAgentGrid(applyDeps);

            JobHandle boundsHandle = ScheduleBoundsProbe(applyDeps);

            NativeArray<float3> velRead = velocities;
            NativeArray<float3> velWrite = prevVelocities;

            JobHandle obstacleHandle = ScheduleObstacleAvoidance(assignHandle, obstacleGridHandle, useObstacleAvoidance, velRead);
            JobHandle attractionHandle = ScheduleAttractorSampling(assignHandle, attractorGridHandle, useAttraction);

            bool useGroupNoiseField = ShouldUseGroupNoiseField();
            JobHandle groupNoiseHandle = ScheduleGroupNoiseField(applyDeps, useGroupNoiseField);

            JobHandle patternHandle = ScheduleLayer3Patterns(clearPatternHandle, out bool anyPattern);

            JobHandle flockDeps = JobHandle.CombineDependencies(obstacleHandle, attractionHandle);
            flockDeps = JobHandle.CombineDependencies(flockDeps, boundsHandle);
            flockDeps = JobHandle.CombineDependencies(flockDeps, clearPatternHandle);

            if (useGroupNoiseField) {
                flockDeps = JobHandle.CombineDependencies(flockDeps, groupNoiseHandle);
            }

            if (anyPattern) {
                flockDeps = JobHandle.CombineDependencies(flockDeps, patternHandle);
            }

            JobHandle neighbourAggHandle = ScheduleNeighbourAggregate(velRead, assignHandle);

            JobHandle steeringHandle = ScheduleSteeringIntegrate(
                deltaTime,
                velRead,
                velWrite,
                useObstacleAvoidance,
                useAttraction,
                neighbourAggHandle,
                flockDeps);

            JobHandle integrateHandle = ScheduleIntegrate(deltaTime, velWrite, steeringHandle);

            velocities = velWrite;
            prevVelocities = velRead;

            inFlightHandle = integrateHandle;

            return integrateHandle;
        }

        /**
        * <summary>
        * Disposes all native allocations owned by the simulation. Completes any in-flight jobs first.
        * </summary>
        */
        public void Dispose() {
            inFlightHandle.Complete();
            inFlightHandle = default;

            DisposeArray(ref positions);
            DisposeArray(ref velocities);
            DisposeArray(ref prevVelocities);
            DisposeArray(ref behaviourIds);
            DisposeArray(ref wallDirections);
            DisposeArray(ref wallDangers);

            DisposeArray(ref obstacles);
            DisposeArray(ref obstacleSteering);

            DisposeArray(ref patternSteering);

            DisposeArray(ref cellAgentStarts);
            DisposeArray(ref cellAgentCounts);
            DisposeArray(ref cellAgentPairs);

            DisposeArray(ref agentCellCounts);
            DisposeArray(ref agentCellIds);
            DisposeArray(ref agentEntryStarts);
            DisposeArray(ref totalAgentPairCount);

            DisposeArray(ref touchedAgentCells);
            DisposeArray(ref touchedAgentCellCount);

            if (cellToObstacles.IsCreated) {
                cellToObstacles.Dispose();
                cellToObstacles = default;
            }

            DisposeArray(ref attractors);
            DisposeArray(ref attractionSteering);

            DisposeArray(ref cellToIndividualAttractor);
            DisposeArray(ref cellToGroupAttractor);
            DisposeArray(ref cellIndividualPriority);
            DisposeArray(ref cellGroupPriority);
            DisposeArray(ref cellGroupNoise);

            DisposeArray(ref behaviourSettings);
            DisposeArray(ref behaviourCellSearchRadius);

            DisposeArray(ref neighbourAggregates);

            AgentCount = 0;

            // Optional hygiene (managed state)
            pendingObstacleChanges.Clear();
            pendingAttractorChanges.Clear();
            pendingBehaviourIdsDirty = false;
            pendingBehaviourIdsCount = 0;
        }

        #endregion

        #region Normal Methods

        private JobHandle ScheduleNeighbourAggregate(NativeArray<float3> velRead, JobHandle deps) {
            var job = new NeighbourAggregateJob {
                Positions = positions,
                PrevVelocities = velRead,

                BehaviourIds = behaviourIds,
                BehaviourSettings = behaviourSettings,
                BehaviourCellSearchRadius = behaviourCellSearchRadius,

                CellAgentStarts = cellAgentStarts,
                CellAgentCounts = cellAgentCounts,
                CellAgentPairs = cellAgentPairs,

                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                OutAggregates = neighbourAggregates,
            };

            return job.Schedule(AgentCount, 64, deps);
        }

        private JobHandle ScheduleSteeringIntegrate(
            float deltaTime,
            NativeArray<float3> velRead,
            NativeArray<float3> velWrite,
            bool useObstacleAvoidance,
            bool useAttraction,
            JobHandle neighbourAggHandle,
            JobHandle deps) {

            JobHandle steeringDeps = JobHandle.CombineDependencies(deps, neighbourAggHandle);

            var job = new SteeringIntegrateJob {
                NeighbourAggregates = neighbourAggregates,

                Positions = positions,
                PrevVelocities = velRead,
                Velocities = velWrite,

                WallDirections = wallDirections,
                WallDangers = wallDangers,

                BehaviourIds = behaviourIds,
                BehaviourSettings = behaviourSettings,

                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                EnvironmentData = environmentData,
                DeltaTime = deltaTime,

                NoiseTime = simulationTime,
                GlobalWanderMultiplier = GlobalWanderMultiplier,
                GlobalGroupNoiseMultiplier = GlobalGroupNoiseMultiplier,
                GlobalPatternMultiplier = GlobalPatternMultiplier,

                PatternSteering = patternSteering,
                CellGroupNoise = cellGroupNoise,

                UseObstacleAvoidance = useObstacleAvoidance,
                ObstacleAvoidWeight = DefaultObstacleAvoidWeight,
                ObstacleSteering = obstacleSteering,

                UseAttraction = useAttraction,
                GlobalAttractionWeight = DefaultAttractionWeight,
                AttractionSteering = attractionSteering,
            };

            return job.Schedule(AgentCount, 64, steeringDeps);
        }

        private bool ValidatePackedAgentGridCreated() {
            return cellAgentStarts.IsCreated
                && cellAgentCounts.IsCreated
                && cellAgentPairs.IsCreated
                && agentCellCounts.IsCreated
                && agentCellIds.IsCreated
                && agentEntryStarts.IsCreated
                && totalAgentPairCount.IsCreated
                && touchedAgentCells.IsCreated
                && touchedAgentCellCount.IsCreated;
        }

        private JobHandle ScheduleApplyPendingChanges(JobHandle frameDeps) {
            JobHandle applyDeps = frameDeps;

            if (pendingBehaviourIdsDirty && pendingBehaviourIdsCount > 0) {
                int count = math.min(pendingBehaviourIdsCount, AgentCount);

                var tmp = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < count; i += 1) {
                    tmp[i] = pendingBehaviourIdsManaged[i];
                }

                var copyJob = new CopyIntArrayJob {
                    Source = tmp,
                    Destination = behaviourIds,
                    Count = count,
                };

                JobHandle copyHandle = copyJob.Schedule(count, 64, applyDeps);
                tmp.Dispose(copyHandle);

                applyDeps = copyHandle;
                pendingBehaviourIdsDirty = false;
                pendingBehaviourIdsCount = 0;
            }

            if (pendingObstacleChanges.Count > 0 && obstacles.IsCreated) {
                int changeCount = pendingObstacleChanges.Count;

                var tmp = new NativeArray<IndexedObstacleChange>(
                    changeCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < changeCount; i += 1) {
                    tmp[i] = pendingObstacleChanges[i];
                }

                pendingObstacleChanges.Clear();

                var applyJob = new ApplyIndexedObstacleChangesJob {
                    Changes = tmp,
                    Obstacles = obstacles,
                };

                JobHandle applyHandle = applyJob.Schedule(changeCount, 64, applyDeps);
                tmp.Dispose(applyHandle);

                applyDeps = applyHandle;
                obstacleGridDirty = true;
            }

            if (pendingAttractorChanges.Count > 0 && attractors.IsCreated) {
                int changeCount = pendingAttractorChanges.Count;

                var tmp = new NativeArray<IndexedAttractorChange>(
                    changeCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < changeCount; i += 1) {
                    tmp[i] = pendingAttractorChanges[i];
                }

                pendingAttractorChanges.Clear();

                float envMinY = environmentData.BoundsCenter.y - environmentData.BoundsExtents.y;
                float envMaxY = environmentData.BoundsCenter.y + environmentData.BoundsExtents.y;
                float envHeight = math.max(envMaxY - envMinY, 0.0001f);

                var applyJob = new ApplyIndexedAttractorChangesJob {
                    Changes = tmp,
                    Attractors = attractors,
                    EnvMinY = envMinY,
                    EnvHeight = envHeight,
                };

                JobHandle applyHandle = applyJob.Schedule(changeCount, 64, applyDeps);
                tmp.Dispose(applyHandle);

                applyDeps = applyHandle;
                attractorGridDirty = true;
            }

            return applyDeps;
        }

        private JobHandle ScheduleClearPatternSteering(JobHandle deps) {
            var clearPatternJob = new ClearFloat3ArrayJob {
                Array = patternSteering,
            };

            return clearPatternJob.Schedule(AgentCount, 64, deps);
        }

        private bool ShouldUseObstacleAvoidance() {
            return obstacleCount > 0
                && cellToObstacles.IsCreated
                && obstacles.IsCreated
                && gridCellCount > 0;
        }

        private bool ShouldUseAttraction() {
            return attractorCount > 0
                && attractors.IsCreated
                && cellToIndividualAttractor.IsCreated
                && cellToGroupAttractor.IsCreated
                && cellIndividualPriority.IsCreated
                && cellGroupPriority.IsCreated
                && gridCellCount > 0;
        }

        private bool ShouldUseGroupNoiseField() {
            return cellGroupNoise.IsCreated && gridCellCount > 0;
        }

        private JobHandle ScheduleRebuildObstacleGridIfDirty(JobHandle deps, bool useObstacleAvoidance) {
            if (!useObstacleAvoidance || !obstacleGridDirty) {
                return deps;
            }

            var clearMapJob = new ClearMultiHashMapJob {
                Map = cellToObstacles,
            };

            JobHandle clearMapHandle = clearMapJob.Schedule(deps);

            var buildJob = new BuildObstacleGridJob {
                Obstacles = obstacles,
                CellToObstacles = cellToObstacles.AsParallelWriter(),
                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,
            };

            JobHandle obstacleGridHandle = buildJob.Schedule(obstacleCount, 1, clearMapHandle);

            obstacleGridDirty = false;
            return obstacleGridHandle;
        }

        private JobHandle ScheduleRebuildAttractorGridIfDirty(JobHandle deps, bool useAttraction) {
            if (!useAttraction || !attractorGridDirty) {
                return deps;
            }

            var rebuildJob = new RebuildAttractorGridJob {
                Attractors = attractors,
                AttractorCount = attractorCount,

                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                CellToIndividualAttractor = cellToIndividualAttractor,
                CellToGroupAttractor = cellToGroupAttractor,
                CellIndividualPriority = cellIndividualPriority,
                CellGroupPriority = cellGroupPriority,
            };

            JobHandle attractorGridHandle = rebuildJob.Schedule(deps);

            attractorGridDirty = false;
            return attractorGridHandle;
        }

        private JobHandle ScheduleRebuildPackedAgentGrid(JobHandle deps) {
            var clearTouchedJob = new ClearTouchedAgentCellsJob {
                TouchedCells = touchedAgentCells,
                TouchedCount = touchedAgentCellCount,
                CellStarts = cellAgentStarts,
                CellCounts = cellAgentCounts,
            };

            JobHandle clearTouchedHandle = clearTouchedJob.Schedule(deps);

            var buildCellIdsJob = new AssignToGridJob {
                Positions = positions,
                BehaviourIds = behaviourIds,
                BehaviourSettings = behaviourSettings,

                CellSize = environmentData.CellSize,
                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,

                MaxCellsPerAgent = maxCellsPerAgent,
                AgentCellCounts = agentCellCounts,
                AgentCellIds = agentCellIds,
            };

            JobHandle buildCellIdsHandle = buildCellIdsJob.Schedule(AgentCount, 64, clearTouchedHandle);

            var prefixJob = new ExclusivePrefixSumIntJob {
                Counts = agentCellCounts,
                Starts = agentEntryStarts,
                Total = totalAgentPairCount,
            };

            JobHandle prefixHandle = prefixJob.Schedule(buildCellIdsHandle);

            var fillPairsJob = new FillCellAgentPairsJob {
                MaxCellsPerAgent = maxCellsPerAgent,
                AgentCellCounts = agentCellCounts,
                AgentCellIds = agentCellIds,
                AgentEntryStarts = agentEntryStarts,
                OutPairs = cellAgentPairs,
            };

            JobHandle fillPairsHandle = fillPairsJob.Schedule(AgentCount, 64, prefixHandle);

            var sortPairsJob = new SortCellAgentPairsJob {
                Pairs = cellAgentPairs,
                Total = totalAgentPairCount,
            };

            JobHandle sortPairsHandle = sortPairsJob.Schedule(fillPairsHandle);

            var buildRangesJob = new BuildCellAgentRangesJob {
                Pairs = cellAgentPairs,
                Total = totalAgentPairCount,
                CellStarts = cellAgentStarts,
                CellCounts = cellAgentCounts,
                TouchedCells = touchedAgentCells,
                TouchedCount = touchedAgentCellCount,
            };

            return buildRangesJob.Schedule(sortPairsHandle);
        }

        private JobHandle ScheduleBoundsProbe(JobHandle deps) {
            var boundsJob = new BoundsProbeJob {
                Positions = positions,
                BehaviourIds = behaviourIds,
                BehaviourSettings = behaviourSettings,

                EnvironmentData = environmentData,
                WallDirections = wallDirections,
                WallDangers = wallDangers,
            };

            return boundsJob.Schedule(AgentCount, 64, deps);
        }

        private JobHandle ScheduleObstacleAvoidance(
            JobHandle assignHandle,
            JobHandle obstacleGridHandle,
            bool useObstacleAvoidance,
            NativeArray<float3> velRead) {

            if (!useObstacleAvoidance) {
                return assignHandle;
            }

            JobHandle obstacleDeps = JobHandle.CombineDependencies(assignHandle, obstacleGridHandle);

            var obstacleJob = new ObstacleAvoidanceJob {
                Positions = positions,
                Velocities = velRead,

                BehaviourIds = behaviourIds,
                BehaviourSettings = behaviourSettings,

                Obstacles = obstacles,
                CellToObstacles = cellToObstacles,

                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                AvoidStrength = DefaultObstacleAvoidStrength,
                ObstacleSteering = obstacleSteering,
            };

            return obstacleJob.Schedule(AgentCount, 64, obstacleDeps);
        }

        private JobHandle ScheduleAttractorSampling(
            JobHandle assignHandle,
            JobHandle attractorGridHandle,
            bool useAttraction) {

            if (!useAttraction) {
                return assignHandle;
            }

            JobHandle attractionDeps = JobHandle.CombineDependencies(assignHandle, attractorGridHandle);

            var attractionJob = new AttractorSamplingJob {
                Positions = positions,
                BehaviourIds = behaviourIds,

                Attractors = attractors,

                CellToIndividualAttractor = cellToIndividualAttractor,

                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                AttractionSteering = attractionSteering,
                BehaviourSettings = behaviourSettings,
            };

            return attractionJob.Schedule(AgentCount, 64, attractionDeps);
        }

        private JobHandle ScheduleGroupNoiseField(JobHandle deps, bool useGroupNoiseField) {
            if (!useGroupNoiseField) {
                return deps;
            }

            switch (activeLayer2GroupNoiseKind) {
                case FlockGroupNoisePatternType.VerticalBands: {
                        var job = new GroupNoiseFieldVerticalBandsJob {
                            Time = simulationTime,
                            Frequency = GroupNoiseFrequency,
                            GridResolution = environmentData.GridResolution,
                            CellNoise = cellGroupNoise,
                            Common = activeLayer2GroupNoiseCommon,
                            Payload = activeLayer2VerticalBands,
                        };
                        return job.Schedule(gridCellCount, 64, deps);
                    }

                case FlockGroupNoisePatternType.Vortex: {
                        var job = new GroupNoiseFieldVortexJob {
                            Time = simulationTime,
                            Frequency = GroupNoiseFrequency,
                            GridResolution = environmentData.GridResolution,
                            CellNoise = cellGroupNoise,
                            Common = activeLayer2GroupNoiseCommon,
                            Payload = activeLayer2Vortex,
                        };
                        return job.Schedule(gridCellCount, 64, deps);
                    }

                case FlockGroupNoisePatternType.SphereShell: {
                        var job = new GroupNoiseFieldSphereShellJob {
                            Time = simulationTime,
                            Frequency = GroupNoiseFrequency,
                            GridResolution = environmentData.GridResolution,
                            CellNoise = cellGroupNoise,
                            Common = activeLayer2GroupNoiseCommon,
                            Payload = activeLayer2SphereShell,
                        };
                        return job.Schedule(gridCellCount, 64, deps);
                    }

                case FlockGroupNoisePatternType.SimpleSine:
                default: {
                        var job = new GroupNoiseFieldSimpleSineJob {
                            Time = simulationTime,
                            Frequency = GroupNoiseFrequency,
                            GridResolution = environmentData.GridResolution,
                            CellNoise = cellGroupNoise,
                            Common = activeLayer2GroupNoiseCommon,
                            Payload = activeLayer2SimpleSine,
                        };
                        return job.Schedule(gridCellCount, 64, deps);
                    }
            }
        }

        private JobHandle ScheduleLayer3Patterns(JobHandle clearPatternHandle, out bool anyPattern) {
            JobHandle patternHandle = clearPatternHandle;
            anyPattern = false;

            // 1) Baked patterns
            if (layer3PatternCommands != null && layer3PatternCommands.Length > 0) {
                for (int i = 0; i < layer3PatternCommands.Length; i += 1) {
                    PatternVolumeCommand cmd = layer3PatternCommands[i];

                    if (cmd.Strength <= 0f) {
                        continue;
                    }

                    switch (cmd.Kind) {
                        case PatternVolumeKind.SphereShell: {
                                if (layer3SphereShells == null || (uint)cmd.PayloadIndex >= (uint)layer3SphereShells.Length) {
                                    continue;
                                }

                                PatternVolumeSphereShell s = layer3SphereShells[cmd.PayloadIndex];
                                if (s.Radius <= 0f || s.Thickness <= 0f) {
                                    continue;
                                }

                                var job = new PatternVolumeSphereShellJob {
                                    Center = s.Center,
                                    Radius = s.Radius,
                                    Thickness = s.Thickness,
                                };

                                patternHandle = SchedulePatternVolumeJob(
                                    ref job,
                                    positions,
                                    behaviourIds,
                                    patternSteering,
                                    cmd.BehaviourMask,
                                    cmd.Strength,
                                    AgentCount,
                                    patternHandle);

                                anyPattern = true;
                                break;
                            }

                        case PatternVolumeKind.BoxShell: {
                                if (layer3BoxShells == null || (uint)cmd.PayloadIndex >= (uint)layer3BoxShells.Length) {
                                    continue;
                                }

                                PatternVolumeBoxShell b = layer3BoxShells[cmd.PayloadIndex];
                                if (b.Thickness <= 0f || b.HalfExtents.x <= 0f || b.HalfExtents.y <= 0f || b.HalfExtents.z <= 0f) {
                                    continue;
                                }

                                var job = new PatternVolumeBoxShellJob {
                                    Center = b.Center,
                                    HalfExtents = b.HalfExtents,
                                    Thickness = b.Thickness,
                                };

                                patternHandle = SchedulePatternVolumeJob(
                                    ref job,
                                    positions,
                                    behaviourIds,
                                    patternSteering,
                                    cmd.BehaviourMask,
                                    cmd.Strength,
                                    AgentCount,
                                    patternHandle);

                                anyPattern = true;
                                break;
                            }
                    }
                }
            }

            if (runtimeLayer3Active.Count > 0) {
                for (int a = 0; a < runtimeLayer3Active.Count; a += 1) {
                    int patternIndex = runtimeLayer3Active[a];
                    if ((uint)patternIndex >= (uint)runtimeLayer3Patterns.Count) {
                        continue;
                    }

                    RuntimePatternVolumeInstance inst = runtimeLayer3Patterns[patternIndex];
                    if (inst.Active == 0 || inst.Strength <= 0f) {
                        continue;
                    }

                    switch (inst.Kind) {
                        case PatternVolumeKind.SphereShell: {
                                int payloadIndex = inst.PayloadIndex;
                                if ((uint)payloadIndex >= (uint)runtimeSphereShells.Count) {
                                    continue;
                                }

                                PatternVolumeSphereShell s = runtimeSphereShells[payloadIndex];
                                if (s.Radius <= 0f || s.Thickness <= 0f) {
                                    continue;
                                }

                                var job = new PatternVolumeSphereShellJob {
                                    Center = s.Center,
                                    Radius = s.Radius,
                                    Thickness = s.Thickness,
                                };

                                patternHandle = SchedulePatternVolumeJob(
                                    ref job,
                                    positions,
                                    behaviourIds,
                                    patternSteering,
                                    inst.BehaviourMask,
                                    inst.Strength,
                                    AgentCount,
                                    patternHandle);

                                anyPattern = true;
                                break;
                            }

                        case PatternVolumeKind.BoxShell: {
                                int payloadIndex = inst.PayloadIndex;
                                if ((uint)payloadIndex >= (uint)runtimeBoxShells.Count) {
                                    continue;
                                }

                                PatternVolumeBoxShell b = runtimeBoxShells[payloadIndex];

                                if (b.Thickness <= 0f || b.HalfExtents.x <= 0f || b.HalfExtents.y <= 0f || b.HalfExtents.z <= 0f) {
                                    continue;
                                }

                                var job = new PatternVolumeBoxShellJob {
                                    Center = b.Center,
                                    HalfExtents = b.HalfExtents,
                                    Thickness = b.Thickness,
                                };

                                patternHandle = SchedulePatternVolumeJob(
                                    ref job,
                                    positions,
                                    behaviourIds,
                                    patternSteering,
                                    inst.BehaviourMask,
                                    inst.Strength,
                                    AgentCount,
                                    patternHandle);

                                anyPattern = true;
                                break;
                            }
                    }
                }
            }

            bool useDynamicSphere = patternSphereRadius > 0f && patternSphereStrength > 0f;
            if (useDynamicSphere) {
                var dynJob = new PatternVolumeSphereShellJob {
                    Center = patternSphereCenter,
                    Radius = patternSphereRadius,
                    Thickness = patternSphereThickness,
                };

                patternHandle = SchedulePatternVolumeJob(
                    ref dynJob,
                    positions,
                    behaviourIds,
                    patternSteering,
                    patternSphereBehaviourMask,
                    patternSphereStrength,
                    AgentCount,
                    patternHandle);

                anyPattern = true;
            }

            return patternHandle;
        }

        private JobHandle ScheduleIntegrate(float deltaTime, NativeArray<float3> velWrite, JobHandle deps) {
            var integrateJob = new IntegrateJob {
                Positions = positions,
                Velocities = velWrite,
                EnvironmentData = environmentData,
                DeltaTime = deltaTime,
            };

            return integrateJob.Schedule(AgentCount, 64, deps);
        }

        #endregion

        #region Private Static Methods

        private static void DisposeArray<T>(ref NativeArray<T> array)
            where T : struct {

            if (!array.IsCreated) {
                return;
            }

            array.Dispose();
            array = default;
        }

        #endregion

        #region Inner Structs

        private struct RuntimePatternVolumeInstance {
            public int Generation;
            public byte Active;
            public int ActiveListIndex;

            public PatternVolumeKind Kind;
            public int PayloadIndex;
            public float Strength;
            public uint BehaviourMask;
        }

        #endregion
    }
}
