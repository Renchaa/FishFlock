// File: Assets/Flock/Runtime/FlockSimulation.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Jobs;
    using Flock.Runtime.Logging;
    using System;
    using System.Collections.Generic;
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
        NativeArray<float3> prevVelocities;
        // NEW: per-behaviour bounds response

        NativeArray<float> behaviourBoundsWeight;               // radial push
        NativeArray<float> behaviourBoundsTangentialDamping;    // kill sliding
        NativeArray<float> behaviourBoundsInfluenceSuppression; // how much to mute flocking near walls

        NativeArray<float3> wallDirections; // inward normal(s) near walls
        NativeArray<float> wallDangers;     // 0..1 (or a bit >1 if outside)

        NativeArray<float> behaviourWanderStrength;
        NativeArray<float> behaviourWanderFrequency;
        NativeArray<float> behaviourGroupNoiseStrength;
        NativeArray<float> behaviourPatternWeight;
        NativeArray<float3> patternSteering;

        NativeArray<float3> cellGroupNoise;  // NEW: per-cell group noise direction

        float simulationTime; // NEW: accumulated sim time for noise
        FlockLayer3PatternCommand[] layer3PatternCommands = Array.Empty<FlockLayer3PatternCommand>();
        FlockLayer3PatternSphereShell[] layer3SphereShells = Array.Empty<FlockLayer3PatternSphereShell>();
        FlockLayer3PatternBoxShell[] layer3BoxShells = Array.Empty<FlockLayer3PatternBoxShell>();

        // ==============================
        // Runtime-instanced Layer-3 patterns (start/stop by handle)
        // ==============================
        struct RuntimeLayer3PatternInstance {
            public int Generation;          // increments on Stop() to invalidate stale handles
            public byte Active;             // 1 = active, 0 = inactive
            public int ActiveListIndex;     // index inside runtimeLayer3Active for O(1) removal

            public FlockLayer3PatternKind Kind;
            public int PayloadIndex;        // index into the kind-specific runtime payload list
            public float Strength;
            public uint BehaviourMask;
        }

        readonly List<RuntimeLayer3PatternInstance> runtimeLayer3Patterns = new List<RuntimeLayer3PatternInstance>(16);
        readonly List<int> runtimeLayer3Active = new List<int>(16);
        readonly Stack<int> runtimeLayer3Free = new Stack<int>(16);

        // Runtime payload storage per-kind (add more lists/stacks here when you add new kinds)
        readonly List<FlockLayer3PatternSphereShell> runtimeSphereShells = new List<FlockLayer3PatternSphereShell>(16);
        readonly Stack<int> runtimeSphereShellFree = new Stack<int>(16);

        readonly List<FlockLayer3PatternBoxShell> runtimeBoxShells = new List<FlockLayer3PatternBoxShell>(16);
        readonly Stack<int> runtimeBoxShellFree = new Stack<int>(16);

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
        NativeArray<float> behaviourDepthBiasStrength;
        NativeArray<byte> behaviourDepthWinsOverAttractor;
        NativeArray<float> behaviourPreferredDepthMinNorm;
        NativeArray<float> behaviourPreferredDepthMaxNorm;
        NativeArray<float> behaviourPreferredDepthWeight;
        NativeArray<float> behaviourPreferredDepthEdgeFraction;
        NativeArray<float> behaviourBodyRadius;
        NativeArray<int> behaviourCellSearchRadius;
        NativeArray<float> behaviourSchoolSpacingFactor;
        NativeArray<float> behaviourSchoolOuterFactor;
        NativeArray<float> behaviourSchoolStrength;
        NativeArray<float> behaviourSchoolInnerSoftness;
        NativeArray<float> behaviourSchoolDeadzoneFraction;
        NativeArray<float> behaviourSchoolRadialDamping;   // NEW

        // NEW: per-behaviour group-noise tuning
        NativeArray<float> behaviourGroupNoiseDirectionRate;
        NativeArray<float> behaviourGroupNoiseSpeedWeight;

        NativeArray<int> cellAgentStarts;      // per cell: start index into cellAgentPairs
        NativeArray<int> cellAgentCounts;      // per cell: number of entries
        NativeArray<CellAgentPair> cellAgentPairs; // packed (CellId, AgentIndex), sorted by CellId

        // Per-agent temporary buffers to build the packed grid
        NativeArray<int> agentCellCounts;      // how many cells this agent occupies this frame
        NativeArray<int> agentCellIds;         // size = AgentCount * maxCellsPerAgent (per-agent block)
        NativeArray<int> agentEntryStarts;     // exclusive prefix sum of agentCellCounts
        NativeArray<int> totalAgentPairCount;  // length 1: total occupied-cell entries this frame

        // Track touched cells so we clear only what we wrote last frame
        NativeArray<int> touchedAgentCells;    // unique cell ids written this frame
        NativeArray<int> touchedAgentCellCount;// length 1: how many touched cells

        int maxCellsPerAgent;
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

        NativeArray<int> behaviourMaxNeighbourChecks;
        NativeArray<int> behaviourMaxFriendlySamples;
        NativeArray<int> behaviourMaxSeparationSamples;

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
        public float GroupNoiseFrequency { get; set; } = 0.3f;

        FlockGroupNoisePatternType activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.SimpleSine;
        FlockGroupNoiseCommonSettings activeLayer2GroupNoiseCommon = FlockGroupNoiseCommonSettings.Default;
        FlockGroupNoiseSimpleSinePayload activeLayer2SimpleSine = FlockGroupNoiseSimpleSinePayload.Default;
        FlockGroupNoiseVerticalBandsPayload activeLayer2VerticalBands = FlockGroupNoiseVerticalBandsPayload.Default;
        FlockGroupNoiseVortexPayload activeLayer2Vortex = FlockGroupNoiseVortexPayload.Default;
        FlockGroupNoiseSphereShellPayload activeLayer2SphereShell = FlockGroupNoiseSphereShellPayload.Default;

        float3 patternSphereCenter;
        float patternSphereRadius;
        float patternSphereThickness;
        float patternSphereStrength;
        uint patternSphereBehaviourMask;

        // ------------------------------------------
        // Job safety / staging
        // ------------------------------------------
        JobHandle inFlightHandle;

        // Dirty flags (set by setters, consumed by ScheduleStepJobs)
        bool obstacleGridDirty;
        bool attractorGridDirty;

        // Staged agent behaviour ids (managed -> applied at next ScheduleStepJobs)
        bool pendingBehaviourIdsDirty;
        int pendingBehaviourIdsCount;
        int[] pendingBehaviourIdsManaged;

        // Staged indexed updates (managed -> copied into TempJob arrays on schedule)
        readonly List<Flock.Runtime.Jobs.IndexedObstacleChange> pendingObstacleChanges =
            new List<Flock.Runtime.Jobs.IndexedObstacleChange>(32);

        readonly List<Flock.Runtime.Jobs.IndexedAttractorChange> pendingAttractorChanges =
            new List<Flock.Runtime.Jobs.IndexedAttractorChange>(32);

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
            activeLayer2Vortex = FlockGroupNoiseVortexPayload.Default;
            activeLayer2SphereShell = FlockGroupNoiseSphereShellPayload.Default;

            // Reset Layer-3 baked + runtime state
            layer3PatternCommands = Array.Empty<FlockLayer3PatternCommand>();
            layer3SphereShells = Array.Empty<FlockLayer3PatternSphereShell>();
            layer3BoxShells = Array.Empty<FlockLayer3PatternBoxShell>();

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

        // ADD in FlockSimulation (public API)
        public void SetLayer2GroupNoiseSimpleSine(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseSimpleSinePayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.SimpleSine;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2SimpleSine = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to SimpleSine.",
                null);
        }

        public void SetLayer2GroupNoiseVerticalBands(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseVerticalBandsPayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.VerticalBands;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2VerticalBands = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to VerticalBands.",
                null);
        }

        public void SetLayer2GroupNoiseVortex(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseVortexPayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.Vortex;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2Vortex = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to Vortex.",
                null);
        }

        public void SetLayer2GroupNoiseSphereShell(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseSphereShellPayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.SphereShell;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2SphereShell = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to SphereShell.",
                null);
        }

        public void SetLayer3Patterns(
            FlockLayer3PatternCommand[] commands,
            FlockLayer3PatternSphereShell[] sphereShellPayloads,
            FlockLayer3PatternBoxShell[] boxShellPayloads) {

            layer3PatternCommands = commands ?? Array.Empty<FlockLayer3PatternCommand>();
            layer3SphereShells = sphereShellPayloads ?? Array.Empty<FlockLayer3PatternSphereShell>();
            layer3BoxShells = boxShellPayloads ?? Array.Empty<FlockLayer3PatternBoxShell>();

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"Layer-3 baked patterns set: commands={layer3PatternCommands.Length}, sphereShells={layer3SphereShells.Length}, boxShells={layer3BoxShells.Length}.",
                null);
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

            // Accumulate simulation time for noise / patterns
            simulationTime += deltaTime;

            // Hard fence: this frame must run after:
            // - caller deps (inputHandle)
            // - previous frame in-flight jobs (inFlightHandle)
            JobHandle frameDeps = JobHandle.CombineDependencies(inputHandle, inFlightHandle);

            // ------------------------------------------------------------------
            // Validate packed grid buffers exist
            // ------------------------------------------------------------------
            if (!cellAgentStarts.IsCreated
                || !cellAgentCounts.IsCreated
                || !cellAgentPairs.IsCreated
                || !agentCellCounts.IsCreated
                || !agentCellIds.IsCreated
                || !agentEntryStarts.IsCreated
                || !totalAgentPairCount.IsCreated
                || !touchedAgentCells.IsCreated
                || !touchedAgentCellCount.IsCreated) {

                FlockLog.Error(
                    logger,
                    FlockLogCategory.Simulation,
                    "ScheduleStepJobs called but packed agent grid is not created.",
                    null);

                return frameDeps;
            }

            // ------------------------------------------------------------------
            // Stage 0: apply pending main-thread changes via jobs (safe)
            // ------------------------------------------------------------------
            JobHandle applyDeps = frameDeps;

            // A) Pending behaviour ids (managed -> TempJob -> copy job -> dispose)
            if (pendingBehaviourIdsDirty && pendingBehaviourIdsCount > 0) {
                int count = math.min(pendingBehaviourIdsCount, AgentCount);

                var tmp = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < count; i += 1) {
                    tmp[i] = pendingBehaviourIdsManaged[i];
                }

                var copyJob = new Flock.Runtime.Jobs.CopyIntArrayJob {
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

            // B) Pending obstacle changes (managed list -> TempJob -> apply job -> dispose)
            if (pendingObstacleChanges.Count > 0 && obstacles.IsCreated) {
                int changeCount = pendingObstacleChanges.Count;

                var tmp = new NativeArray<Flock.Runtime.Jobs.IndexedObstacleChange>(
                    changeCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < changeCount; i += 1) {
                    tmp[i] = pendingObstacleChanges[i];
                }

                pendingObstacleChanges.Clear();

                var applyJob = new Flock.Runtime.Jobs.ApplyIndexedObstacleChangesJob {
                    Changes = tmp,
                    Obstacles = obstacles,
                };

                JobHandle applyHandle = applyJob.Schedule(changeCount, 64, applyDeps);

                tmp.Dispose(applyHandle);

                applyDeps = applyHandle;
                obstacleGridDirty = true;
            }

            // C) Pending attractor changes (managed list -> TempJob -> apply job -> dispose)
            if (pendingAttractorChanges.Count > 0 && attractors.IsCreated) {
                int changeCount = pendingAttractorChanges.Count;

                var tmp = new NativeArray<Flock.Runtime.Jobs.IndexedAttractorChange>(
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

                var applyJob = new Flock.Runtime.Jobs.ApplyIndexedAttractorChangesJob {
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

            // ------------------------------------------------------------------
            // Stage 1: clear per-frame buffers via jobs (NO main-thread loops)
            // ------------------------------------------------------------------
            var clearPatternJob = new Flock.Runtime.Jobs.ClearFloat3ArrayJob {
                Array = patternSteering,
            };

            JobHandle clearPatternHandle = clearPatternJob.Schedule(AgentCount, 64, applyDeps);

            // ------------------------------------------------------------------
            // Stage 2: rebuild obstacle grid safely (job-owned)
            // ------------------------------------------------------------------
            bool useObstacleAvoidance =
                obstacleCount > 0
                && cellToObstacles.IsCreated
                && obstacles.IsCreated
                && gridCellCount > 0;

            JobHandle obstacleGridHandle = applyDeps;

            if (useObstacleAvoidance && obstacleGridDirty) {
                var clearMapJob = new Flock.Runtime.Jobs.ClearMultiHashMapJob {
                    Map = cellToObstacles,
                };

                JobHandle clearMapHandle = clearMapJob.Schedule(applyDeps);

                var buildJob = new Flock.Runtime.Jobs.BuildObstacleGridJob {
                    Obstacles = obstacles,
                    CellToObstacles = cellToObstacles.AsParallelWriter(),

                    GridOrigin = environmentData.GridOrigin,
                    GridResolution = environmentData.GridResolution,
                    CellSize = environmentData.CellSize,
                };

                // Schedule with obstacleCount iterations; each iteration adds its covered cells
                obstacleGridHandle = buildJob.Schedule(obstacleCount, 1, clearMapHandle);

                obstacleGridDirty = false;
            }

            // ------------------------------------------------------------------
            // Stage 3: rebuild attractor grid safely (job-owned)
            // ------------------------------------------------------------------
            bool useAttraction =
                attractorCount > 0
                && attractors.IsCreated
                && cellToIndividualAttractor.IsCreated
                && cellToGroupAttractor.IsCreated
                && cellIndividualPriority.IsCreated
                && cellGroupPriority.IsCreated
                && gridCellCount > 0;

            JobHandle attractorGridHandle = applyDeps;

            if (useAttraction && attractorGridDirty) {
                var rebuildJob = new Flock.Runtime.Jobs.RebuildAttractorGridJob {
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

                attractorGridHandle = rebuildJob.Schedule(applyDeps);
                attractorGridDirty = false;
            }

            // ------------------------------------------------------------------
            // Packed agent grid rebuild (no hash map)
            // ------------------------------------------------------------------
            var clearTouchedJob = new Flock.Runtime.Jobs.ClearTouchedAgentCellsJob {
                TouchedCells = touchedAgentCells,
                TouchedCount = touchedAgentCellCount,
                CellStarts = cellAgentStarts,
                CellCounts = cellAgentCounts,
            };

            JobHandle clearTouchedHandle = clearTouchedJob.Schedule(applyDeps);

            var buildCellIdsJob = new AssignToGridJob {
                Positions = positions,
                BehaviourIds = behaviourIds,
                BehaviourBodyRadius = behaviourBodyRadius,

                CellSize = environmentData.CellSize,
                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,

                MaxCellsPerAgent = maxCellsPerAgent,
                AgentCellCounts = agentCellCounts,
                AgentCellIds = agentCellIds,
            };

            JobHandle buildCellIdsHandle = buildCellIdsJob.Schedule(
                AgentCount,
                64,
                clearTouchedHandle);

            var prefixJob = new Flock.Runtime.Jobs.ExclusivePrefixSumIntJob {
                Counts = agentCellCounts,
                Starts = agentEntryStarts,
                Total = totalAgentPairCount,
            };

            JobHandle prefixHandle = prefixJob.Schedule(buildCellIdsHandle);

            var fillPairsJob = new Flock.Runtime.Jobs.FillCellAgentPairsJob {
                MaxCellsPerAgent = maxCellsPerAgent,
                AgentCellCounts = agentCellCounts,
                AgentCellIds = agentCellIds,
                AgentEntryStarts = agentEntryStarts,
                OutPairs = cellAgentPairs,
            };

            JobHandle fillPairsHandle = fillPairsJob.Schedule(
                AgentCount,
                64,
                prefixHandle);

            var sortPairsJob = new Flock.Runtime.Jobs.SortCellAgentPairsJob {
                Pairs = cellAgentPairs,
                Total = totalAgentPairCount,
            };

            JobHandle sortPairsHandle = sortPairsJob.Schedule(fillPairsHandle);

            var buildRangesJob = new Flock.Runtime.Jobs.BuildCellAgentRangesJob {
                Pairs = cellAgentPairs,
                Total = totalAgentPairCount,
                CellStarts = cellAgentStarts,
                CellCounts = cellAgentCounts,
                TouchedCells = touchedAgentCells,
                TouchedCount = touchedAgentCellCount,
            };

            JobHandle assignHandle = buildRangesJob.Schedule(sortPairsHandle);

            // ------------------------------------------------------------------
            // Bounds probe (independent; only needs applyDeps fence)
            // ------------------------------------------------------------------
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
                applyDeps);

            // ------------------------------------------------------------------
            // Velocity double-buffering
            // ------------------------------------------------------------------
            NativeArray<float3> velRead = velocities;       // last frame (stable)
            NativeArray<float3> velWrite = prevVelocities;  // this frame (write target)

            // ------------------------------------------------------------------
            // Obstacles avoidance job (depends on assign + obstacle grid build)
            // ------------------------------------------------------------------
            JobHandle obstacleHandle = assignHandle;

            if (useObstacleAvoidance) {
                JobHandle obstacleDeps = JobHandle.CombineDependencies(assignHandle, obstacleGridHandle);

                var obstacleJob = new ObstacleAvoidanceJob {
                    Positions = positions,
                    Velocities = velRead,

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
                    obstacleDeps);
            }

            // ------------------------------------------------------------------
            // Attraction sampling job (depends on assign + attractor grid build)
            // ------------------------------------------------------------------
            JobHandle attractionHandle = assignHandle;

            if (useAttraction) {
                JobHandle attractionDeps = JobHandle.CombineDependencies(assignHandle, attractorGridHandle);

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

                    BehaviourUsePreferredDepth = behaviourUsePreferredDepth,
                    BehaviourPreferredDepthMin = behaviourPreferredDepthMinNorm,
                    BehaviourPreferredDepthMax = behaviourPreferredDepthMaxNorm,
                    BehaviourDepthWinsOverAttractor = behaviourDepthWinsOverAttractor,
                };

                attractionHandle = attractionJob.Schedule(
                    AgentCount,
                    64,
                    attractionDeps);
            }

            // ------------------------------------------------------------------
            // Layer-2 group noise field (per-cell)
            // ------------------------------------------------------------------
            bool useGroupNoiseField =
                cellGroupNoise.IsCreated
                && gridCellCount > 0;

            JobHandle groupNoiseHandle = applyDeps;

            if (useGroupNoiseField) {
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

                            groupNoiseHandle = job.Schedule(gridCellCount, 64, applyDeps);
                            break;
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

                            groupNoiseHandle = job.Schedule(gridCellCount, 64, applyDeps);
                            break;
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

                            groupNoiseHandle = job.Schedule(gridCellCount, 64, applyDeps);
                            break;
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

                            groupNoiseHandle = job.Schedule(gridCellCount, 64, applyDeps);
                            break;
                        }
                }
            }

            // ------------------------------------------------------------------
            // Layer-3 patterns (stacked into patternSteering)
            // IMPORTANT: pattern jobs start after clearPatternHandle.
            // ------------------------------------------------------------------
            JobHandle patternHandle = clearPatternHandle;
            bool anyPattern = false;

            // 1) Baked patterns
            if (layer3PatternCommands != null && layer3PatternCommands.Length > 0) {
                for (int i = 0; i < layer3PatternCommands.Length; i += 1) {
                    FlockLayer3PatternCommand cmd = layer3PatternCommands[i];

                    if (cmd.Strength <= 0f) {
                        continue;
                    }

                    switch (cmd.Kind) {
                        case FlockLayer3PatternKind.SphereShell: {
                                if (layer3SphereShells == null
                                    || (uint)cmd.PayloadIndex >= (uint)layer3SphereShells.Length) {
                                    continue;
                                }

                                FlockLayer3PatternSphereShell s = layer3SphereShells[cmd.PayloadIndex];

                                if (s.Radius <= 0f || s.Thickness <= 0f) {
                                    continue;
                                }

                                var job = new PatternSphereJob {
                                    Center = s.Center,
                                    Radius = s.Radius,
                                    Thickness = s.Thickness,
                                };

                                patternHandle = ScheduleLayer3PatternJob(
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

                        case FlockLayer3PatternKind.BoxShell: {
                                if (layer3BoxShells == null
                                    || (uint)cmd.PayloadIndex >= (uint)layer3BoxShells.Length) {
                                    continue;
                                }

                                FlockLayer3PatternBoxShell b = layer3BoxShells[cmd.PayloadIndex];

                                if (b.Thickness <= 0f
                                    || b.HalfExtents.x <= 0f
                                    || b.HalfExtents.y <= 0f
                                    || b.HalfExtents.z <= 0f) {
                                    continue;
                                }

                                var job = new PatternBoxJob {
                                    Center = b.Center,
                                    HalfExtents = b.HalfExtents,
                                    Thickness = b.Thickness,
                                };

                                patternHandle = ScheduleLayer3PatternJob(
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

            // 2) Runtime-instanced patterns
            if (runtimeLayer3Active.Count > 0) {
                for (int a = 0; a < runtimeLayer3Active.Count; a += 1) {
                    int patternIndex = runtimeLayer3Active[a];

                    if ((uint)patternIndex >= (uint)runtimeLayer3Patterns.Count) {
                        continue;
                    }

                    RuntimeLayer3PatternInstance inst = runtimeLayer3Patterns[patternIndex];

                    if (inst.Active == 0 || inst.Strength <= 0f) {
                        continue;
                    }

                    switch (inst.Kind) {
                        case FlockLayer3PatternKind.SphereShell: {
                                int payloadIndex = inst.PayloadIndex;

                                if ((uint)payloadIndex >= (uint)runtimeSphereShells.Count) {
                                    continue;
                                }

                                FlockLayer3PatternSphereShell s = runtimeSphereShells[payloadIndex];

                                if (s.Radius <= 0f || s.Thickness <= 0f) {
                                    continue;
                                }

                                var job = new PatternSphereJob {
                                    Center = s.Center,
                                    Radius = s.Radius,
                                    Thickness = s.Thickness,
                                };

                                patternHandle = ScheduleLayer3PatternJob(
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

                        case FlockLayer3PatternKind.BoxShell: {
                                int payloadIndex = inst.PayloadIndex;

                                if ((uint)payloadIndex >= (uint)runtimeBoxShells.Count) {
                                    continue;
                                }

                                FlockLayer3PatternBoxShell b = runtimeBoxShells[payloadIndex];

                                if (b.Thickness <= 0f
                                    || b.HalfExtents.x <= 0f
                                    || b.HalfExtents.y <= 0f
                                    || b.HalfExtents.z <= 0f) {
                                    continue;
                                }

                                var job = new PatternBoxJob {
                                    Center = b.Center,
                                    HalfExtents = b.HalfExtents,
                                    Thickness = b.Thickness,
                                };

                                patternHandle = ScheduleLayer3PatternJob(
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

            // 3) Dynamic runtime sphere (SetPatternSphereTarget)
            bool useDynamicSphere =
                patternSphereRadius > 0f
                && patternSphereStrength > 0f;

            if (useDynamicSphere) {
                var dynJob = new PatternSphereJob {
                    Center = patternSphereCenter,
                    Radius = patternSphereRadius,
                    Thickness = patternSphereThickness,
                };

                patternHandle = ScheduleLayer3PatternJob(
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

            // ------------------------------------------------------------------
            // Combine dependencies for flock job
            // Ensure we always depend on clearPatternHandle so PatternSteering is zeroed.
            // ------------------------------------------------------------------
            JobHandle flockDeps = JobHandle.CombineDependencies(obstacleHandle, attractionHandle);

            flockDeps = JobHandle.CombineDependencies(flockDeps, boundsHandle);
            flockDeps = JobHandle.CombineDependencies(flockDeps, clearPatternHandle);

            if (useGroupNoiseField) {
                flockDeps = JobHandle.CombineDependencies(flockDeps, groupNoiseHandle);
            }

            if (anyPattern) {
                flockDeps = JobHandle.CombineDependencies(flockDeps, patternHandle);
            }

            // ------------------------------------------------------------------
            // Main flock step
            // ------------------------------------------------------------------
            var flockJob = new FlockStepJob {
                // Core agent data
                Positions = positions,
                PrevVelocities = velRead,
                Velocities = velWrite,

                // Bounds probe outputs (per-agent)
                WallDirections = wallDirections,
                WallDangers = wallDangers,

                // Per-behaviour bounds response
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
                BehaviourDepthBiasStrength = behaviourDepthBiasStrength,
                BehaviourPreferredDepthMinNorm = behaviourPreferredDepthMinNorm,
                BehaviourPreferredDepthMaxNorm = behaviourPreferredDepthMaxNorm,
                BehaviourPreferredDepthWeight = behaviourPreferredDepthWeight,
                BehaviourPreferredDepthEdgeFraction = behaviourPreferredDepthEdgeFraction,
                BehaviourSchoolRadialDamping = behaviourSchoolRadialDamping,

                // Noise per behaviour
                BehaviourWanderStrength = behaviourWanderStrength,
                BehaviourWanderFrequency = behaviourWanderFrequency,
                BehaviourGroupNoiseStrength = behaviourGroupNoiseStrength,
                BehaviourPatternWeight = behaviourPatternWeight,

                // Group noise extra
                BehaviourGroupNoiseDirectionRate = behaviourGroupNoiseDirectionRate,
                BehaviourGroupNoiseSpeedWeight = behaviourGroupNoiseSpeedWeight,

                // Neighbour search + dedup
                BehaviourCellSearchRadius = behaviourCellSearchRadius,

                // Spatial grid
                CellAgentStarts = cellAgentStarts,
                CellAgentCounts = cellAgentCounts,
                CellAgentPairs = cellAgentPairs,

                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                // Environment + timestep
                EnvironmentData = environmentData,
                DeltaTime = deltaTime,

                // Noise globals + pattern steering
                NoiseTime = simulationTime,
                GlobalWanderMultiplier = GlobalWanderMultiplier,
                GlobalGroupNoiseMultiplier = GlobalGroupNoiseMultiplier,
                GlobalPatternMultiplier = GlobalPatternMultiplier,

                PatternSteering = patternSteering,
                CellGroupNoise = cellGroupNoise,

                // Obstacles
                UseObstacleAvoidance = useObstacleAvoidance,
                ObstacleAvoidWeight = DefaultObstacleAvoidWeight,
                ObstacleSteering = obstacleSteering,

                // Attractors
                UseAttraction = useAttraction,
                GlobalAttractionWeight = DefaultAttractionWeight,
                AttractionSteering = attractionSteering,

                BehaviourMaxNeighbourChecks = behaviourMaxNeighbourChecks,
                BehaviourMaxFriendlySamples = behaviourMaxFriendlySamples,
                BehaviourMaxSeparationSamples = behaviourMaxSeparationSamples,
            };

            JobHandle flockHandle = flockJob.Schedule(
                AgentCount,
                64,
                flockDeps);

            // ------------------------------------------------------------------
            // Integrate positions
            // ------------------------------------------------------------------
            var integrateJob = new IntegrateJob {
                Positions = positions,
                Velocities = velWrite,
                EnvironmentData = environmentData,
                DeltaTime = deltaTime,
            };

            JobHandle integrateHandle = integrateJob.Schedule(
                AgentCount,
                64,
                flockHandle);

            // Swap buffers for next frame
            velocities = velWrite;
            prevVelocities = velRead;

            // Record as our internal in-flight fence
            inFlightHandle = integrateHandle;

            return integrateHandle;
        }


        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // FIX 2: dispose bounds + wall arrays to avoid leaks

        public void Dispose() {
            // HARD RULE: complete all jobs before freeing any memory they may touch.
            inFlightHandle.Complete();
            inFlightHandle = default;

            DisposeArray(ref positions);
            DisposeArray(ref velocities);
            DisposeArray(ref prevVelocities);
            DisposeArray(ref behaviourIds);
            DisposeArray(ref wallDirections);
            DisposeArray(ref wallDangers);

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

            DisposeArray(ref behaviourBoundsWeight);
            DisposeArray(ref behaviourBoundsTangentialDamping);
            DisposeArray(ref behaviourBoundsInfluenceSuppression);

            DisposeArray(ref behaviourAvoidanceWeight);
            DisposeArray(ref behaviourNeutralWeight);
            DisposeArray(ref behaviourAttractionWeight);
            DisposeArray(ref behaviourAvoidResponse);
            DisposeArray(ref behaviourAvoidMask);
            DisposeArray(ref behaviourNeutralMask);

            DisposeArray(ref behaviourPreferredDepthMinNorm);
            DisposeArray(ref behaviourPreferredDepthMaxNorm);
            DisposeArray(ref behaviourPreferredDepthWeight);

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

            DisposeArray(ref behaviourMinGroupSize);
            DisposeArray(ref behaviourMaxGroupSize);
            DisposeArray(ref behaviourGroupRadiusMultiplier);
            DisposeArray(ref behaviourLonerRadiusMultiplier);
            DisposeArray(ref behaviourLonerCohesionBoost);

            DisposeArray(ref behaviourUsePreferredDepth);
            DisposeArray(ref behaviourDepthBiasStrength);
            DisposeArray(ref behaviourDepthWinsOverAttractor);
            DisposeArray(ref behaviourPreferredDepthEdgeFraction);
            DisposeArray(ref behaviourSchoolRadialDamping);

            DisposeArray(ref behaviourBodyRadius);
            DisposeArray(ref behaviourCellSearchRadius);
            DisposeArray(ref behaviourMinGroupSizeWeight);
            DisposeArray(ref behaviourMaxGroupSizeWeight);

            DisposeArray(ref behaviourWanderStrength);
            DisposeArray(ref behaviourWanderFrequency);
            DisposeArray(ref behaviourGroupNoiseStrength);
            DisposeArray(ref behaviourPatternWeight);

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

            DisposeArray(ref behaviourGroupNoiseDirectionRate);
            DisposeArray(ref behaviourGroupNoiseSpeedWeight);

            DisposeArray(ref behaviourMaxNeighbourChecks);
            DisposeArray(ref behaviourMaxFriendlySamples);
            DisposeArray(ref behaviourMaxSeparationSamples);

            AgentCount = 0;

            // Optional hygiene (managed state)
            pendingObstacleChanges.Clear();
            pendingAttractorChanges.Clear();
            pendingBehaviourIdsDirty = false;
            pendingBehaviourIdsCount = 0;
        }

        /// <summary>
        /// Enable or update the pattern sphere that layer-3 patterns steer towards.
        /// Only behaviours with PatternWeight > 0 will react to this.
        /// </summary>
        public void SetPatternSphereTarget(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            patternSphereCenter = center;
            patternSphereRadius = math.max(0f, radius);
            patternSphereStrength = math.max(0f, strength);
            patternSphereBehaviourMask = behaviourMask;

            if (thickness <= 0f) {
                patternSphereThickness = patternSphereRadius * 0.25f;
            } else {
                patternSphereThickness = math.max(0.001f, thickness);
            }
        }

        static JobHandle ScheduleLayer3PatternJob<T>(
            ref T job,
            NativeArray<float3> positions,
            NativeArray<int> behaviourIds,
            NativeArray<float3> patternSteering,
            uint behaviourMask,
            float strength,
            int agentCount,
            JobHandle inputHandle)
            where T : struct, IJobParallelFor, IFlockLayer3PatternJob {

            job.SetCommonData(
                positions,
                behaviourIds,
                patternSteering,
                behaviourMask,
                strength);

            return job.Schedule(agentCount, 64, inputHandle);
        }


        /// <summary>
        /// Disable the pattern sphere influence completely.
        /// </summary>
        public void ClearPatternSphere() {
            patternSphereRadius = 0f;
            patternSphereStrength = 0f;
            patternSphereThickness = 0f;
        }

        public void SetObstacleData(int index, FlockObstacleData data) {
            if (!IsCreated || !obstacles.IsCreated) {
                return;
            }

            if ((uint)index >= (uint)obstacles.Length) {
                return;
            }

            pendingObstacleChanges.Add(new Flock.Runtime.Jobs.IndexedObstacleChange {
                Index = index,
                Data = data,
            });

            // Mark grid dirty; rebuild happens in ScheduleStepJobs safely.
            obstacleGridDirty = true;
        }

        bool TryGetRuntimePattern(
    FlockLayer3PatternHandle handle,
    out RuntimeLayer3PatternInstance inst) {

            inst = default;

            if (!handle.IsValid) {
                return false;
            }

            int index = handle.Index;

            if ((uint)index >= (uint)runtimeLayer3Patterns.Count) {
                return false;
            }

            inst = runtimeLayer3Patterns[index];

            if (inst.Generation != handle.Generation) {
                return false; // stale handle
            }

            return true;
        }

        int AcquireRuntimePatternSlot() {
            if (runtimeLayer3Free.Count > 0) {
                return runtimeLayer3Free.Pop();
            }

            runtimeLayer3Patterns.Add(new RuntimeLayer3PatternInstance {
                Generation = 0,
                Active = 0,
                ActiveListIndex = -1,
                Kind = 0,
                PayloadIndex = -1,
                Strength = 0f,
                BehaviourMask = 0u,
            });

            return runtimeLayer3Patterns.Count - 1;
        }

        int AcquireSphereShellPayloadSlot() {
            if (runtimeSphereShellFree.Count > 0) {
                return runtimeSphereShellFree.Pop();
            }

            runtimeSphereShells.Add(default);
            return runtimeSphereShells.Count - 1;
        }

        int AcquireBoxShellPayloadSlot() {
            if (runtimeBoxShellFree.Count > 0) {
                return runtimeBoxShellFree.Pop();
            }

            runtimeBoxShells.Add(default);
            return runtimeBoxShells.Count - 1;
        }

        void RemoveFromActiveList(int patternIndex, ref RuntimeLayer3PatternInstance inst) {
            int listIndex = inst.ActiveListIndex;

            if (listIndex < 0 || listIndex >= runtimeLayer3Active.Count) {
                inst.ActiveListIndex = -1;
                return;
            }

            int last = runtimeLayer3Active.Count - 1;
            int movedPatternIndex = runtimeLayer3Active[last];

            runtimeLayer3Active[listIndex] = movedPatternIndex;
            runtimeLayer3Active.RemoveAt(last);

            // Update the moved pattern's ActiveListIndex
            if (movedPatternIndex != patternIndex) {
                RuntimeLayer3PatternInstance moved = runtimeLayer3Patterns[movedPatternIndex];
                moved.ActiveListIndex = listIndex;
                runtimeLayer3Patterns[movedPatternIndex] = moved;
            }

            inst.ActiveListIndex = -1;
        }

        /// <summary>
        /// Start a runtime SphereShell pattern instance and get a handle you can Update/Stop.
        /// No per-frame allocations; safe to call occasionally from gameplay code.
        /// </summary>
        public FlockLayer3PatternHandle StartPatternSphereShell(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return FlockLayer3PatternHandle.Invalid;
            }

            radius = math.max(0f, radius);
            strength = math.max(0f, strength);

            if (radius <= 0f || strength <= 0f) {
                return FlockLayer3PatternHandle.Invalid;
            }

            float t = thickness <= 0f ? (radius * 0.25f) : thickness;
            t = math.max(0.001f, t);

            int payloadIndex = AcquireSphereShellPayloadSlot();
            runtimeSphereShells[payloadIndex] = new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = radius,
                Thickness = t,
            };

            int patternIndex = AcquireRuntimePatternSlot();
            RuntimeLayer3PatternInstance inst = runtimeLayer3Patterns[patternIndex];

            inst.Active = 1;
            inst.Kind = FlockLayer3PatternKind.SphereShell;
            inst.PayloadIndex = payloadIndex;
            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            inst.ActiveListIndex = runtimeLayer3Active.Count;
            runtimeLayer3Active.Add(patternIndex);

            runtimeLayer3Patterns[patternIndex] = inst;

            var handle = new FlockLayer3PatternHandle {
                Index = patternIndex,
                Generation = inst.Generation,
            };

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StartPatternSphereShell: handle=({handle.Index}:{handle.Generation}) radius={radius:0.###} thickness={t:0.###} strength={strength:0.###}.",
                null);

            return handle;
        }

        /// <summary>
        /// Update a running runtime SphereShell pattern by handle (ideal for moving bubbles).
        /// No allocations; call every frame if needed.
        /// </summary>
        public bool UpdatePatternSphereShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return false;
            }

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance inst)) {
                return false;
            }

            if (inst.Active == 0 || inst.Kind != FlockLayer3PatternKind.SphereShell) {
                return false;
            }

            int payloadIndex = inst.PayloadIndex;
            if ((uint)payloadIndex >= (uint)runtimeSphereShells.Count) {
                return false;
            }

            radius = math.max(0f, radius);
            strength = math.max(0f, strength);

            float t = thickness <= 0f ? (radius * 0.25f) : thickness;
            t = math.max(0.001f, t);

            runtimeSphereShells[payloadIndex] = new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = radius,
                Thickness = t,
            };

            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            runtimeLayer3Patterns[handle.Index] = inst;
            return true;
        }

        public FlockLayer3PatternHandle StartPatternBoxShell(
    float3 center,
    float3 halfExtents,
    float thickness = -1f,
    float strength = 1f,
    uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return FlockLayer3PatternHandle.Invalid;
            }

            strength = math.max(0f, strength);

            if (halfExtents.x <= 0f
                || halfExtents.y <= 0f
                || halfExtents.z <= 0f
                || strength <= 0f) {
                return FlockLayer3PatternHandle.Invalid;
            }

            float3 he = new float3(
                math.max(halfExtents.x, 0.001f),
                math.max(halfExtents.y, 0.001f),
                math.max(halfExtents.z, 0.001f));

            float t = thickness <= 0f
                ? math.cmin(he) * 0.25f
                : thickness;

            t = math.max(0.001f, t);

            int payloadIndex = AcquireBoxShellPayloadSlot();
            runtimeBoxShells[payloadIndex] = new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = he,
                Thickness = t,
            };

            int patternIndex = AcquireRuntimePatternSlot();
            RuntimeLayer3PatternInstance inst = runtimeLayer3Patterns[patternIndex];

            inst.Active = 1;
            inst.Kind = FlockLayer3PatternKind.BoxShell;
            inst.PayloadIndex = payloadIndex;
            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            inst.ActiveListIndex = runtimeLayer3Active.Count;
            runtimeLayer3Active.Add(patternIndex);

            runtimeLayer3Patterns[patternIndex] = inst;

            var handle = new FlockLayer3PatternHandle {
                Index = patternIndex,
                Generation = inst.Generation,
            };

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StartPatternBoxShell: handle=({handle.Index}:{handle.Generation}) halfExtents=({he.x:0.###},{he.y:0.###},{he.z:0.###}) thickness={t:0.###} strength={strength:0.###}.",
                null);

            return handle;
        }

        public bool UpdatePatternBoxShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return false;
            }

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance inst)) {
                return false;
            }

            if (inst.Active == 0 || inst.Kind != FlockLayer3PatternKind.BoxShell) {
                return false;
            }

            int payloadIndex = inst.PayloadIndex;
            if ((uint)payloadIndex >= (uint)runtimeBoxShells.Count) {
                return false;
            }

            strength = math.max(0f, strength);

            if (halfExtents.x <= 0f
                || halfExtents.y <= 0f
                || halfExtents.z <= 0f
                || strength <= 0f) {
                return false;
            }

            float3 he = new float3(
                math.max(halfExtents.x, 0.001f),
                math.max(halfExtents.y, 0.001f),
                math.max(halfExtents.z, 0.001f));

            float t = thickness <= 0f
                ? math.cmin(he) * 0.25f
                : thickness;

            t = math.max(0.001f, t);

            runtimeBoxShells[payloadIndex] = new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = he,
                Thickness = t,
            };

            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            runtimeLayer3Patterns[handle.Index] = inst;
            return true;
        }

        /// <summary>
        /// Stop a runtime Layer-3 pattern instance by handle (only that one stops).
        /// </summary>
        public bool StopLayer3Pattern(FlockLayer3PatternHandle handle) {
            if (!IsCreated) {
                return false;
            }

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance inst)) {
                return false;
            }

            if (inst.Active == 0) {
                return false;
            }

            int patternIndex = handle.Index;

            // Remove from active list (O(1))
            RemoveFromActiveList(patternIndex, ref inst);

            switch (inst.Kind) {
                case FlockLayer3PatternKind.SphereShell: {
                        if (inst.PayloadIndex >= 0) {
                            runtimeSphereShellFree.Push(inst.PayloadIndex);
                        }
                        break;
                    }

                case FlockLayer3PatternKind.BoxShell: {
                        if (inst.PayloadIndex >= 0) {
                            runtimeBoxShellFree.Push(inst.PayloadIndex);
                        }
                        break;
                    }
            }

            inst.Active = 0;
            inst.PayloadIndex = -1;
            inst.Strength = 0f;
            inst.BehaviourMask = 0u;

            // Invalidate existing handle
            inst.Generation += 1;

            runtimeLayer3Patterns[patternIndex] = inst;
            runtimeLayer3Free.Push(patternIndex);

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StopLayer3Pattern: stopped handle=({handle.Index}:{handle.Generation}) kind={inst.Kind}.",
                null);

            return true;
        }

        // =====================================
        // FlockSimulation.cs – AllocateAgentArrays
        // ADD agentGroupNoiseDir allocation
        // =====================================
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

            wallDirections = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            wallDangers = new NativeArray<float>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

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

            // NEW: group noise tuning
            behaviourGroupNoiseDirectionRate = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupNoiseSpeedWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            // NEW: bounds per-behaviour
            behaviourBoundsWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourBoundsTangentialDamping = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourBoundsInfluenceSuppression = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourMaxNeighbourChecks = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMaxFriendlySamples = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMaxSeparationSamples = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            for (int index = 0; index < behaviourCount; index += 1) {
                FlockBehaviourSettings behaviour = settings[index];

                behaviourMaxSpeed[index] = behaviour.MaxSpeed;
                behaviourMaxAcceleration[index] = behaviour.MaxAcceleration;
                behaviourDesiredSpeed[index] = behaviour.DesiredSpeed;

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

                behaviourGroupNoiseDirectionRate[index] =
                    math.max(0f, behaviour.GroupNoiseDirectionRate);
                behaviourGroupNoiseSpeedWeight[index] =
                    math.max(0f, behaviour.GroupNoiseSpeedWeight);

                behaviourPatternWeight[index] =
                    math.max(0f, behaviour.PatternWeight);

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
                behaviourDepthBiasStrength[index] = behaviour.DepthBiasStrength;
                behaviourDepthWinsOverAttractor[index] = depthWins;

                behaviourPreferredDepthMinNorm[index] = behaviour.PreferredDepthMinNorm;
                behaviourPreferredDepthMaxNorm[index] = behaviour.PreferredDepthMaxNorm;
                behaviourPreferredDepthWeight[index] = behaviour.PreferredDepthWeight;
                behaviourPreferredDepthEdgeFraction[index] = behaviour.PreferredDepthEdgeFraction;

                behaviourMaxNeighbourChecks[index] = behaviour.MaxNeighbourChecks;
                behaviourMaxFriendlySamples[index] = behaviour.MaxFriendlySamples;
                behaviourMaxSeparationSamples[index] = behaviour.MaxSeparationSamples;
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
            // Always allocate (AgentCount length). Feature is gated by useAttraction bool.
            attractionSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);
        }


        public void SetAttractorData(int index, FlockAttractorData data) {
            if (!IsCreated || !attractors.IsCreated) {
                return;
            }

            if ((uint)index >= (uint)attractors.Length) {
                return;
            }

            pendingAttractorChanges.Add(new Flock.Runtime.Jobs.IndexedAttractorChange {
                Index = index,
                Data = data,
            });

            attractorGridDirty = true;
        }

        // =====================================
        // 2) FlockSimulation.cs – AllocateGrid
        // REPLACE BODY with the one below
        // =====================================
        void AllocateGrid(Allocator allocator) {
            gridCellCount = environmentData.GridResolution.x
                            * environmentData.GridResolution.y
                            * environmentData.GridResolution.z;

            // NEW: allocate per-cell group noise field
            if (gridCellCount > 0) {
                cellGroupNoise = new NativeArray<float3>(
                    gridCellCount,
                    allocator,
                    NativeArrayOptions.ClearMemory);
            } else {
                cellGroupNoise = default;
            }

            // -------------------- Packed agent grid allocation --------------------
            if (gridCellCount > 0) {
                cellAgentStarts = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
                cellAgentCounts = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.ClearMemory);

                for (int i = 0; i < gridCellCount; i += 1) {
                    cellAgentStarts[i] = -1;
                }

                float cellSizeSafe = math.max(environmentData.CellSize, 0.0001f);

                float maxBodyRadius = 0f;
                if (behaviourBodyRadius.IsCreated && behaviourBodyRadius.Length > 0) {
                    for (int i = 0; i < behaviourBodyRadius.Length; i += 1) {
                        maxBodyRadius = math.max(maxBodyRadius, behaviourBodyRadius[i]);
                    }
                }

                if (maxBodyRadius <= 0f) {
                    maxCellsPerAgent = 1;
                } else {
                    int span = (int)math.ceil((2f * maxBodyRadius) / cellSizeSafe) + 2;
                    span = math.max(span, 1);
                    maxCellsPerAgent = span * span * span;
                }

                int pairCapacity = math.max(AgentCount * maxCellsPerAgent, AgentCount);

                agentCellCounts = new NativeArray<int>(AgentCount, allocator, NativeArrayOptions.ClearMemory);
                agentCellIds = new NativeArray<int>(AgentCount * maxCellsPerAgent, allocator, NativeArrayOptions.UninitializedMemory);
                agentEntryStarts = new NativeArray<int>(AgentCount, allocator, NativeArrayOptions.UninitializedMemory);

                cellAgentPairs = new NativeArray<Flock.Runtime.Jobs.CellAgentPair>(pairCapacity, allocator, NativeArrayOptions.UninitializedMemory);

                totalAgentPairCount = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);

                int touchedCap = math.min(gridCellCount, pairCapacity);
                touchedAgentCells = new NativeArray<int>(touchedCap, allocator, NativeArrayOptions.UninitializedMemory);
                touchedAgentCellCount = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
            } else {
                cellAgentStarts = default;
                cellAgentCounts = default;
                cellAgentPairs = default;

                agentCellCounts = default;
                agentCellIds = default;
                agentEntryStarts = default;
                totalAgentPairCount = default;

                touchedAgentCells = default;
                touchedAgentCellCount = default;

                maxCellsPerAgent = 1;
            }

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

        public void RebuildAttractorGrid() {
            attractorGridDirty = true;
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

        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // REPLACE METHOD: AllocateObstacleSimulationData
        // REPLACE METHOD: AllocateObstacleSimulationData
        void AllocateObstacleSimulationData(Allocator allocator) {
            // Always allocate (AgentCount length). Feature is gated by useObstacleAvoidance bool.
            obstacleSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            if (obstacleCount <= 0 || gridCellCount <= 0 || !obstacles.IsCreated) {
                cellToObstacles = default;
                return;
            }

            float cellSize = math.max(environmentData.CellSize, 0.0001f);
            float3 origin = environmentData.GridOrigin;
            int3 res = environmentData.GridResolution;

            float3 gridMin = origin;
            float3 gridMax = origin + (float3)res * cellSize;

            long cap = 0;

            for (int i = 0; i < obstacleCount; i += 1) {
                FlockObstacleData o = obstacles[i];

                float r = math.max(0.0f, o.Radius);
                r = math.max(r, cellSize * 0.5f);

                float3 minW = o.Position - new float3(r);
                float3 maxW = o.Position + new float3(r);

                if (maxW.x < gridMin.x || minW.x > gridMax.x ||
                    maxW.y < gridMin.y || minW.y > gridMax.y ||
                    maxW.z < gridMin.z || minW.z > gridMax.z) {
                    continue;
                }

                float3 minLocal = (minW - origin) / cellSize;
                float3 maxLocal = (maxW - origin) / cellSize;

                int3 minCell = (int3)math.floor(minLocal);
                int3 maxCell = (int3)math.floor(maxLocal);

                minCell = math.clamp(minCell, new int3(0, 0, 0), res - new int3(1, 1, 1));
                maxCell = math.clamp(maxCell, new int3(0, 0, 0), res - new int3(1, 1, 1));

                long cx = (long)(maxCell.x - minCell.x + 1);
                long cy = (long)(maxCell.y - minCell.y + 1);
                long cz = (long)(maxCell.z - minCell.z + 1);

                cap += cx * cy * cz;
            }

            cap = (long)(cap * 1.25f) + 16;
            cap = math.max(cap, (long)obstacleCount * 4L);
            cap = math.max(cap, (long)gridCellCount);

            int capacity = (int)math.min(cap, (long)int.MaxValue);

            cellToObstacles = new NativeParallelMultiHashMap<int, int>(
                capacity,
                allocator);
        }

        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // REPLACE METHOD: BuildObstacleGrid
        void BuildObstacleGrid() {
            if (!cellToObstacles.IsCreated || !obstacles.IsCreated || obstacleCount <= 0 || gridCellCount <= 0) {
                return;
            }

            cellToObstacles.Clear();

            float cellSize = math.max(environmentData.CellSize, 0.0001f);
            float3 origin = environmentData.GridOrigin;
            int3 res = environmentData.GridResolution;

            float3 gridMin = origin;
            float3 gridMax = origin + (float3)res * cellSize;

            int layerSize = res.x * res.y;

            for (int index = 0; index < obstacleCount; index += 1) {
                FlockObstacleData o = obstacles[index];

                float r = math.max(0.0f, o.Radius);
                r = math.max(r, cellSize * 0.5f);

                float3 minW = o.Position - new float3(r);
                float3 maxW = o.Position + new float3(r);

                // Reject if completely outside grid bounds
                if (maxW.x < gridMin.x || minW.x > gridMax.x ||
                    maxW.y < gridMin.y || minW.y > gridMax.y ||
                    maxW.z < gridMin.z || minW.z > gridMax.z) {
                    continue;
                }

                float3 minLocal = (minW - origin) / cellSize;
                float3 maxLocal = (maxW - origin) / cellSize;

                int3 minCell = (int3)math.floor(minLocal);
                int3 maxCell = (int3)math.floor(maxLocal);

                minCell = math.clamp(minCell, new int3(0, 0, 0), res - new int3(1, 1, 1));
                maxCell = math.clamp(maxCell, new int3(0, 0, 0), res - new int3(1, 1, 1));

                for (int z = minCell.z; z <= maxCell.z; z += 1) {
                    for (int y = minCell.y; y <= maxCell.y; y += 1) {
                        int rowBase = y * res.x + z * layerSize;

                        for (int x = minCell.x; x <= maxCell.x; x += 1) {
                            int cellIndex = x + rowBase;
                            cellToObstacles.Add(cellIndex, index);
                        }
                    }
                }
            }
        }

        public void SetAgentBehaviourIds(int[] behaviourIdsSource) {
            if (behaviourIdsSource == null) {
                return;
            }

            if (pendingBehaviourIdsManaged == null || pendingBehaviourIdsManaged.Length != AgentCount) {
                pendingBehaviourIdsManaged = new int[AgentCount];
            }

            int count = math.min(AgentCount, behaviourIdsSource.Length);

            for (int i = 0; i < count; i += 1) {
                pendingBehaviourIdsManaged[i] = behaviourIdsSource[i];
            }

            pendingBehaviourIdsCount = count;
            pendingBehaviourIdsDirty = true;
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