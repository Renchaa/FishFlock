// File: Assets/Flock/Runtime/FlockSimulation.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Jobs;
    using Flock.Runtime.Logging;
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
        NativeArray<float> behaviourAvoidanceWeight;   // ADD
        NativeArray<float> behaviourNeutralWeight;     // ADD
        NativeArray<uint> behaviourAvoidMask;          // ADD
        NativeArray<uint> behaviourNeutralMask;        // ADD
        NativeArray<FlockObstacleData> obstacles;
        NativeArray<float3> obstacleSteering;
        NativeArray<float> behaviourAvoidResponse;
        NativeArray<float> behaviourAttractionWeight;

        NativeParallelMultiHashMap<int, int> cellToAgents;
        NativeParallelMultiHashMap<int, int> cellToObstacles;

        FlockEnvironmentData environmentData;

        IFlockLogger logger;

        NativeArray<FlockAttractorData> attractors;
        NativeArray<float3> attractionSteering;
        int attractorCount;        // NEW: relationship-related behaviour arrays

        int obstacleCount;
        int gridCellCount;
        public int AgentCount { get; private set; }

        public bool IsCreated => positions.IsCreated;

        public NativeArray<float3> Positions => positions;
        public NativeArray<float3> Velocities => velocities;

        public int ObstacleCount => obstacleCount;

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

        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // REPLACE ScheduleStepJobs WITH THIS VERSION

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

            var assignJob = new AssignToGridJob {
                Positions = positions,
                CellSize = environmentData.CellSize,
                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellToAgents = cellToAgents.AsParallelWriter(),
            };

            JobHandle assignHandle = assignJob.Schedule(
                AgentCount,
                64,
                inputHandle);

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

            // NEW: attraction job
            bool useAttraction =
                attractorCount > 0
                && attractors.IsCreated
                && attractionSteering.IsCreated;

            JobHandle attractionHandle = assignHandle;

            if (useAttraction) {
                var attractionJob = new AttractorSamplingJob {
                    Positions = positions,
                    BehaviourIds = behaviourIds,

                    Attractors = attractors,
                    BehaviourAttractionWeight = behaviourAttractionWeight,

                    AttractorCount = attractorCount,
                    AttractionSteering = attractionSteering,
                };

                attractionHandle = attractionJob.Schedule(
                    AgentCount,
                    64,
                    assignHandle);
            }

            // Flock job waits for obstacle + attraction + velocity copy.
            JobHandle flockDeps = JobHandle.CombineDependencies(
                obstacleHandle,
                attractionHandle,
                copyHandle);

            var flockJob = new FlockStepJob {
                Positions = positions,
                PrevVelocities = prevVelocities,
                Velocities = velocities,

                BehaviourIds = behaviourIds,

                BehaviourMaxSpeed = behaviourMaxSpeed,
                BehaviourMaxAcceleration = behaviourMaxAcceleration,
                BehaviourDesiredSpeed = behaviourDesiredSpeed,
                BehaviourNeighbourRadius = behaviourNeighbourRadius,
                BehaviourSeparationRadius = behaviourSeparationRadius,
                BehaviourAlignmentWeight = behaviourAlignmentWeight,
                BehaviourCohesionWeight = behaviourCohesionWeight,
                BehaviourSeparationWeight = behaviourSeparationWeight,
                BehaviourInfluenceWeight = behaviourInfluenceWeight,

                BehaviourAvoidanceWeight = behaviourAvoidanceWeight,
                BehaviourNeutralWeight = behaviourNeutralWeight,
                BehaviourAvoidMask = behaviourAvoidMask,
                BehaviourNeutralMask = behaviourNeutralMask,

                BehaviourLeadershipWeight = behaviourLeadershipWeight,
                BehaviourGroupMask = behaviourGroupMask,
                BehaviourAvoidResponse = behaviourAvoidResponse,

                CellToAgents = cellToAgents,
                GridOrigin = environmentData.GridOrigin,
                GridResolution = environmentData.GridResolution,
                CellSize = environmentData.CellSize,

                EnvironmentData = environmentData,
                DeltaTime = deltaTime,

                UseObstacleAvoidance = useObstacleAvoidance,
                ObstacleAvoidWeight = DefaultObstacleAvoidWeight,
                ObstacleSteering = obstacleSteering,

                // NEW: attraction hook
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
        public void Dispose() {
            DisposeArray(ref positions);
            DisposeArray(ref velocities);
            DisposeArray(ref prevVelocities);
            DisposeArray(ref behaviourIds);

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

            // NEW: relationship-related
            DisposeArray(ref behaviourAvoidanceWeight);
            DisposeArray(ref behaviourNeutralWeight);
            DisposeArray(ref behaviourAttractionWeight);
            DisposeArray(ref behaviourAvoidResponse);
            DisposeArray(ref behaviourAvoidMask);
            DisposeArray(ref behaviourNeutralMask);

            // Obstacles
            DisposeArray(ref obstacles);
            DisposeArray(ref obstacleSteering);

            if (cellToAgents.IsCreated) {
                cellToAgents.Dispose();
            }

            if (cellToObstacles.IsCreated) {
                cellToObstacles.Dispose();
            }

            // NEW: attractors
            DisposeArray(ref attractors);
            DisposeArray(ref attractionSteering);

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

            // Initialise with safe defaults (type 0) – controller will overwrite.
            for (int index = 0; index < AgentCount; index += 1) {
                behaviourIds[index] = 0;
            }
        }

        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // REPLACE AllocateBehaviourArrays WITH THIS VERSION

        // ==========================================
        // 8) FlockSimulation.AllocateBehaviourArrays – now fills avoid/neutral/attraction
        // File: Assets/Flock/Runtime/FlockSimulation.cs
        // (REPLACE WHOLE METHOD)
        // ==========================================
        void AllocateBehaviourArrays(
            NativeArray<FlockBehaviourSettings> settings,
            Allocator allocator) {

            int behaviourCount = settings.Length;

            behaviourMaxSpeed = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourMaxAcceleration = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourDesiredSpeed = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourNeighbourRadius = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourSeparationRadius = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourAlignmentWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourCohesionWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourSeparationWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourInfluenceWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourLeadershipWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourGroupMask = new NativeArray<uint>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            // NEW: relationship-related arrays
            behaviourAvoidanceWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourNeutralWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourAttractionWeight = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourAvoidResponse = new NativeArray<float>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourAvoidMask = new NativeArray<uint>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourNeutralMask = new NativeArray<uint>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            for (int index = 0; index < behaviourCount; index += 1) {
                FlockBehaviourSettings behaviour = settings[index];

                behaviourMaxSpeed[index] = behaviour.MaxSpeed;
                behaviourMaxAcceleration[index] = behaviour.MaxAcceleration;
                behaviourDesiredSpeed[index] = behaviour.DesiredSpeed;

                behaviourNeighbourRadius[index] = behaviour.NeighbourRadius;
                behaviourSeparationRadius[index] = behaviour.SeparationRadius;

                behaviourAlignmentWeight[index] = behaviour.AlignmentWeight;
                behaviourCohesionWeight[index] = behaviour.CohesionWeight;
                behaviourSeparationWeight[index] = behaviour.SeparationWeight;

                behaviourInfluenceWeight[index] = behaviour.InfluenceWeight;

                behaviourLeadershipWeight[index] = behaviour.LeadershipWeight;
                behaviourGroupMask[index] = behaviour.GroupMask;

                // NEW: relationships
                behaviourAvoidanceWeight[index] = behaviour.AvoidanceWeight;
                behaviourNeutralWeight[index] = behaviour.NeutralWeight;
                behaviourAttractionWeight[index] = behaviour.AttractionWeight;
                behaviourAvoidResponse[index] = behaviour.AvoidResponse;

                behaviourAvoidMask[index] = behaviour.AvoidMask;
                behaviourNeutralMask[index] = behaviour.NeutralMask;
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

            for (int index = 0; index < attractorCount; index += 1) {
                attractors[index] = source[index];
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
