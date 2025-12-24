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

    public sealed partial class FlockSimulation {
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
        NativeArray<float> behaviourGroupNoiseDirectionRate;
        NativeArray<float> behaviourGroupNoiseSpeedWeight;
        NativeArray<int> cellAgentStarts;      // per cell: start index into cellAgentPairs
        NativeArray<int> cellAgentCounts;      // per cell: number of entries
        NativeArray<CellAgentPair> cellAgentPairs; // packed (CellId, AgentIndex), sorted by CellId
        NativeArray<int> agentCellCounts;      // how many cells this agent occupies this frame
        NativeArray<int> agentCellIds;         // size = AgentCount * maxCellsPerAgent (per-agent block)
        NativeArray<int> agentEntryStarts;     // exclusive prefix sum of agentCellCounts
        NativeArray<int> totalAgentPairCount;  // length 1: total occupied-cell entries this frame
        NativeArray<int> touchedAgentCells;    // unique cell ids written this frame
        NativeArray<int> touchedAgentCellCount;// length 1: how many touched cells
        NativeParallelMultiHashMap<int, int> cellToObstacles;
        FlockEnvironmentData environmentData;

        int maxCellsPerAgent;
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
        readonly List<IndexedObstacleChange> pendingObstacleChanges =
            new List<IndexedObstacleChange>(32);

        readonly List<IndexedAttractorChange> pendingAttractorChanges =
            new List<IndexedAttractorChange>(32);

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

            // Hard fence: this frame must run after caller deps + previous frame jobs
            JobHandle frameDeps = JobHandle.CombineDependencies(inputHandle, inFlightHandle);

            // Validate packed grid buffers exist
            if (!ValidatePackedAgentGridCreated()) {
                FlockLog.Error(
                    logger,
                    FlockLogCategory.Simulation,
                    "ScheduleStepJobs called but packed agent grid is not created.",
                    null);
                return frameDeps;
            }

            // Stage 0: apply pending main-thread changes via jobs (safe)
            JobHandle applyDeps = ScheduleApplyPendingChanges(frameDeps);

            // Stage 1: clear per-frame buffers
            JobHandle clearPatternHandle = ScheduleClearPatternSteering(applyDeps);

            // Stage 2/3 feature flags + grid rebuilds
            bool useObstacleAvoidance = ShouldUseObstacleAvoidance();
            JobHandle obstacleGridHandle = ScheduleRebuildObstacleGridIfDirty(applyDeps, useObstacleAvoidance);

            bool useAttraction = ShouldUseAttraction();
            JobHandle attractorGridHandle = ScheduleRebuildAttractorGridIfDirty(applyDeps, useAttraction);

            // Packed agent grid rebuild (no hash map)
            JobHandle assignHandle = ScheduleRebuildPackedAgentGrid(applyDeps);

            // Bounds probe (independent)
            JobHandle boundsHandle = ScheduleBoundsProbe(applyDeps);

            // Velocity double-buffering
            NativeArray<float3> velRead = velocities;       // last frame (stable)
            NativeArray<float3> velWrite = prevVelocities;  // this frame (write target)

            // Obstacles / attraction sampling (depends on assign + relevant grids)
            JobHandle obstacleHandle = ScheduleObstacleAvoidance(assignHandle, obstacleGridHandle, useObstacleAvoidance, velRead);
            JobHandle attractionHandle = ScheduleAttractorSampling(assignHandle, attractorGridHandle, useAttraction);

            // Layer-2 group noise field (per-cell)
            bool useGroupNoiseField = ShouldUseGroupNoiseField();
            JobHandle groupNoiseHandle = ScheduleGroupNoiseField(applyDeps, useGroupNoiseField);

            // Layer-3 patterns (stacked into patternSteering)
            JobHandle patternHandle = ScheduleLayer3Patterns(clearPatternHandle, out bool anyPattern);

            // Combine dependencies for flock job
            JobHandle flockDeps = JobHandle.CombineDependencies(obstacleHandle, attractionHandle);
            flockDeps = JobHandle.CombineDependencies(flockDeps, boundsHandle);
            flockDeps = JobHandle.CombineDependencies(flockDeps, clearPatternHandle);

            if (useGroupNoiseField) {
                flockDeps = JobHandle.CombineDependencies(flockDeps, groupNoiseHandle);
            }

            if (anyPattern) {
                flockDeps = JobHandle.CombineDependencies(flockDeps, patternHandle);
            }

            // Main flock step
            JobHandle flockHandle = ScheduleFlockStep(deltaTime, velRead, velWrite, useObstacleAvoidance, useAttraction, flockDeps);

            // Integrate positions
            JobHandle integrateHandle = ScheduleIntegrate(deltaTime, velWrite, flockHandle);

            // Swap buffers for next frame
            velocities = velWrite;
            prevVelocities = velRead;

            // Record as our internal in-flight fence
            inFlightHandle = integrateHandle;

            return integrateHandle;
        }

        // -----------------------------
        // Validation
        // -----------------------------
        bool ValidatePackedAgentGridCreated() {
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

        // -----------------------------
        // Stage 0: pending changes
        // -----------------------------
        JobHandle ScheduleApplyPendingChanges(JobHandle frameDeps) {
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

            return applyDeps;
        }

        // -----------------------------
        // Stage 1: per-frame clears
        // -----------------------------
        JobHandle ScheduleClearPatternSteering(JobHandle deps) {
            var clearPatternJob = new Flock.Runtime.Jobs.ClearFloat3ArrayJob {
                Array = patternSteering,
            };

            return clearPatternJob.Schedule(AgentCount, 64, deps);
        }

        // -----------------------------
        // Feature flags
        // -----------------------------
        bool ShouldUseObstacleAvoidance() {
            return obstacleCount > 0
                && cellToObstacles.IsCreated
                && obstacles.IsCreated
                && gridCellCount > 0;
        }

        bool ShouldUseAttraction() {
            return attractorCount > 0
                && attractors.IsCreated
                && cellToIndividualAttractor.IsCreated
                && cellToGroupAttractor.IsCreated
                && cellIndividualPriority.IsCreated
                && cellGroupPriority.IsCreated
                && gridCellCount > 0;
        }

        bool ShouldUseGroupNoiseField() {
            return cellGroupNoise.IsCreated && gridCellCount > 0;
        }

        // -----------------------------
        // Stage 2: obstacle grid rebuild
        // -----------------------------
        JobHandle ScheduleRebuildObstacleGridIfDirty(JobHandle deps, bool useObstacleAvoidance) {
            if (!useObstacleAvoidance || !obstacleGridDirty) {
                return deps;
            }

            var clearMapJob = new Flock.Runtime.Jobs.ClearMultiHashMapJob {
                Map = cellToObstacles,
            };

            JobHandle clearMapHandle = clearMapJob.Schedule(deps);

            var buildJob = new Flock.Runtime.Jobs.BuildObstacleGridJob {
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

        // -----------------------------
        // Stage 3: attractor grid rebuild
        // -----------------------------
        JobHandle ScheduleRebuildAttractorGridIfDirty(JobHandle deps, bool useAttraction) {
            if (!useAttraction || !attractorGridDirty) {
                return deps;
            }

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

            JobHandle attractorGridHandle = rebuildJob.Schedule(deps);

            attractorGridDirty = false;
            return attractorGridHandle;
        }

        // -----------------------------
        // Stage 4: packed agent grid rebuild
        // -----------------------------
        JobHandle ScheduleRebuildPackedAgentGrid(JobHandle deps) {
            var clearTouchedJob = new Flock.Runtime.Jobs.ClearTouchedAgentCellsJob {
                TouchedCells = touchedAgentCells,
                TouchedCount = touchedAgentCellCount,
                CellStarts = cellAgentStarts,
                CellCounts = cellAgentCounts,
            };

            JobHandle clearTouchedHandle = clearTouchedJob.Schedule(deps);

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

            JobHandle buildCellIdsHandle = buildCellIdsJob.Schedule(AgentCount, 64, clearTouchedHandle);

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

            JobHandle fillPairsHandle = fillPairsJob.Schedule(AgentCount, 64, prefixHandle);

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

            return buildRangesJob.Schedule(sortPairsHandle);
        }

        // -----------------------------
        // Bounds probe
        // -----------------------------
        JobHandle ScheduleBoundsProbe(JobHandle deps) {
            var boundsJob = new BoundsProbeJob {
                Positions = positions,
                BehaviourIds = behaviourIds,
                BehaviourSeparationRadius = behaviourSeparationRadius,
                EnvironmentData = environmentData,
                WallDirections = wallDirections,
                WallDangers = wallDangers,
            };

            return boundsJob.Schedule(AgentCount, 64, deps);
        }

        // -----------------------------
        // Obstacles avoidance
        // -----------------------------
        JobHandle ScheduleObstacleAvoidance(
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

            return obstacleJob.Schedule(AgentCount, 64, obstacleDeps);
        }

        // -----------------------------
        // Attraction sampling
        // -----------------------------
        JobHandle ScheduleAttractorSampling(
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

            return attractionJob.Schedule(AgentCount, 64, attractionDeps);
        }

        // -----------------------------
        // Layer-2 group noise field
        // -----------------------------
        JobHandle ScheduleGroupNoiseField(JobHandle deps, bool useGroupNoiseField) {
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

        // -----------------------------
        // Layer-3 patterns
        // -----------------------------
        JobHandle ScheduleLayer3Patterns(JobHandle clearPatternHandle, out bool anyPattern) {
            JobHandle patternHandle = clearPatternHandle;
            anyPattern = false;

            // 1) Baked patterns
            if (layer3PatternCommands != null && layer3PatternCommands.Length > 0) {
                for (int i = 0; i < layer3PatternCommands.Length; i += 1) {
                    FlockLayer3PatternCommand cmd = layer3PatternCommands[i];

                    if (cmd.Strength <= 0f) {
                        continue;
                    }

                    switch (cmd.Kind) {
                        case FlockLayer3PatternKind.SphereShell: {
                                if (layer3SphereShells == null || (uint)cmd.PayloadIndex >= (uint)layer3SphereShells.Length) {
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
                                if (layer3BoxShells == null || (uint)cmd.PayloadIndex >= (uint)layer3BoxShells.Length) {
                                    continue;
                                }

                                FlockLayer3PatternBoxShell b = layer3BoxShells[cmd.PayloadIndex];
                                if (b.Thickness <= 0f || b.HalfExtents.x <= 0f || b.HalfExtents.y <= 0f || b.HalfExtents.z <= 0f) {
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
                                if (b.Thickness <= 0f || b.HalfExtents.x <= 0f || b.HalfExtents.y <= 0f || b.HalfExtents.z <= 0f) {
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
            bool useDynamicSphere = patternSphereRadius > 0f && patternSphereStrength > 0f;
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

            return patternHandle;
        }

        // -----------------------------
        // Main flock step + integrate
        // -----------------------------
        JobHandle ScheduleFlockStep(
            float deltaTime,
            NativeArray<float3> velRead,
            NativeArray<float3> velWrite,
            bool useObstacleAvoidance,
            bool useAttraction,
            JobHandle deps) {

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

            return flockJob.Schedule(AgentCount, 64, deps);
        }

        JobHandle ScheduleIntegrate(float deltaTime, NativeArray<float3> velWrite, JobHandle deps) {
            var integrateJob = new IntegrateJob {
                Positions = positions,
                Velocities = velWrite,
                EnvironmentData = environmentData,
                DeltaTime = deltaTime,
            };

            return integrateJob.Schedule(AgentCount, 64, deps);
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