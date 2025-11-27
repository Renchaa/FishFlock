// File: Assets/Flock/Runtime/FlockController.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using System.Collections.Generic;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;
    using UnityEngine;

    public sealed class FlockController : MonoBehaviour, IFlockLogger {
        [Header("Fish Types")]
        [SerializeField] FishTypePreset[] fishTypes;

        [Header("Bounds (Box)")]
        [SerializeField] Vector3 boundsCenter = Vector3.zero;
        [SerializeField] Vector3 boundsExtents = new Vector3(10.0f, 10.0f, 10.0f);

        [Header("Grid")]
        [SerializeField] float cellSize = 2.0f;

        [Header("Movement")]
        [SerializeField] float globalDamping = 0.5f;

        [Header("Debug")]
        [SerializeField] bool debugDrawBounds = true;
        [SerializeField] bool debugDrawGrid = false;
        [SerializeField] bool debugDrawAgents = true;
        [SerializeField] bool debugDrawNeighbourhood = false;
        [SerializeField, Min(0)] int debugAgentIndex = 0;

        [Header("Obstacles")]
        [SerializeField] FlockObstacle[] staticObstacles;
        [SerializeField] FlockObstacle[] dynamicObstacles;

        int[] dynamicObstacleIndices;

        [Header("Logging")]
        [SerializeField] FlockLogLevel enabledLogLevels = FlockLogLevel.All;
        [SerializeField] FlockLogCategory enabledLogCategories = FlockLogCategory.All;

        [Header("Debug Obstacle Avoidance")]
        [SerializeField] bool debugObstacleAvoidance = false;
        [SerializeField, Range(1, 32)] int debugObstacleCellsToLog = 4;

        [Header("Interactions")]
        [SerializeField] FishInteractionMatrix interactionMatrix;

        [Header("Attractors")]
        [SerializeField] FlockAttractorArea[] staticAttractors;
        [SerializeField] FlockAttractorArea[] dynamicAttractors;

        int[] dynamicAttractorIndices;

        public FlockLogLevel EnabledLevels => enabledLogLevels;
        public FlockLogCategory EnabledCategories => enabledLogCategories;

        FlockSimulation simulation;
        NativeArray<FlockBehaviourSettings> behaviourSettingsArray;
        int[] agentBehaviourIds;
        int totalAgentCount;

        readonly List<Transform> agentTransforms = new List<Transform>();

        void Awake() {
            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"FlockController.Awake on '{name}'.",
                this);

            CreateSimulation();
            SpawnAgents();
        }

        void Update() {
            if (simulation == null || !simulation.IsCreated) {
                return;
            }

            UpdateDynamicObstacles();
            UpdateDynamicAttractors();    // NEW

            JobHandle handle = simulation.ScheduleStepJobs(Time.deltaTime);
            handle.Complete();

            ApplySimulationToTransforms();
        }

        void OnDestroy() {
            if (simulation != null) {
                simulation.Dispose();
                simulation = null;

                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"Disposed FlockSimulation for '{name}'.",
                    this);
            }

            if (behaviourSettingsArray.IsCreated) {
                behaviourSettingsArray.Dispose();
            }
        }

        void CreateBehaviourSettingsArray() {
            if (fishTypes == null || fishTypes.Length == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    $"Fish types array is empty on FlockController '{name}'.",
                    this);
                return;
            }

            if (behaviourSettingsArray.IsCreated) {
                behaviourSettingsArray.Dispose();
            }

            int behaviourCount = fishTypes.Length;
            behaviourSettingsArray = new NativeArray<FlockBehaviourSettings>(
                behaviourCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            // Compile interaction matrix → weights + masks
            float[] compiledLeadership;
            float[] compiledAvoidance;
            float[] compiledNeutral;
            uint[] compiledFriendlyMasks;
            uint[] compiledAvoidMasks;
            uint[] compiledNeutralMasks;

            FlockInteractionCompiler.BuildInteractionData(
                fishTypes,
                interactionMatrix,
                out compiledLeadership,
                out compiledAvoidance,
                out compiledNeutral,
                out compiledFriendlyMasks,
                out compiledAvoidMasks,
                out compiledNeutralMasks);

            totalAgentCount = 0;

            for (int i = 0; i < behaviourCount; i += 1) {
                FishTypePreset preset = fishTypes[i];
                if (preset == null) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"FishTypes[{i}] is null on '{name}'. Skipping.",
                        this);
                    behaviourSettingsArray[i] = default;
                    continue;
                }

                FlockBehaviourSettings settings = preset.ToSettings();

                // Leadership weight from matrix (or default)
                if (compiledLeadership != null && i < compiledLeadership.Length) {
                    settings.LeadershipWeight = compiledLeadership[i];
                } else {
                    settings.LeadershipWeight = 1.0f;
                }

                // Avoidance weight from matrix (or default)
                if (compiledAvoidance != null && i < compiledAvoidance.Length) {
                    settings.AvoidanceWeight = compiledAvoidance[i];
                } else {
                    settings.AvoidanceWeight = 1.0f;
                }

                // Neutral weight from matrix (or default)
                if (compiledNeutral != null && i < compiledNeutral.Length) {
                    settings.NeutralWeight = compiledNeutral[i];
                } else {
                    settings.NeutralWeight = 1.0f;
                }

                // Relationship masks
                settings.GroupMask = compiledFriendlyMasks != null && i < compiledFriendlyMasks.Length
                    ? compiledFriendlyMasks[i]
                    : 0u;

                settings.AvoidMask = compiledAvoidMasks != null && i < compiledAvoidMasks.Length
                    ? compiledAvoidMasks[i]
                    : 0u;

                settings.NeutralMask = compiledNeutralMasks != null && i < compiledNeutralMasks.Length
                    ? compiledNeutralMasks[i]
                    : 0u;

                behaviourSettingsArray[i] = settings;

                int count = Mathf.Max(0, preset.SpawnCount);
                totalAgentCount += count;
            }

            if (totalAgentCount <= 0) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"TotalAgentCount computed from FishTypes on '{name}' is <= 0. No agents will be spawned.",
                    this);
                agentBehaviourIds = null;
                return;
            }

            agentBehaviourIds = new int[totalAgentCount];
            int writeIndex = 0;

            for (int typeIndex = 0; typeIndex < fishTypes.Length; typeIndex += 1) {
                FishTypePreset preset = fishTypes[typeIndex];
                if (preset == null) {
                    continue;
                }

                int count = Mathf.Max(0, preset.SpawnCount);
                for (int j = 0; j < count && writeIndex < totalAgentCount; j += 1) {
                    agentBehaviourIds[writeIndex] = typeIndex;
                    writeIndex += 1;
                }
            }

            if (writeIndex < totalAgentCount) {
                int lastType = Mathf.Clamp(fishTypes.Length - 1, 0, fishTypes.Length - 1);
                for (; writeIndex < totalAgentCount; writeIndex += 1) {
                    agentBehaviourIds[writeIndex] = lastType;
                }
            }

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Created behaviour settings for {behaviourCount} fish types and planned {totalAgentCount} agents on '{name}'.",
                this);
        }

        // REPLACE CreateSimulation IN FlockController.cs
        void CreateSimulation() {
            CreateBehaviourSettingsArray();

            if (agentBehaviourIds == null || agentBehaviourIds.Length == 0) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"CreateSimulation on '{name}' aborted: no agents planned from FishTypes.",
                    this);
                return;
            }

            if (!behaviourSettingsArray.IsCreated || behaviourSettingsArray.Length == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    $"Behaviour settings array is not created or empty on '{name}'.",
                    this);
                return;
            }

            FlockEnvironmentData environmentData = BuildEnvironmentData();
            FlockObstacleData[] obstacleData = BuildObstacleData();
            FlockAttractorData[] attractorData = BuildAttractorData();   // NEW

            simulation = new FlockSimulation();

            simulation.Initialize(
                agentBehaviourIds.Length,
                environmentData,
                behaviourSettingsArray,
                obstacleData,
                attractorData,               // NEW
                Allocator.Persistent,
                this);

            simulation.SetAgentBehaviourIds(agentBehaviourIds);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Created FlockSimulation with {agentBehaviourIds.Length} agents for '{name}', " +
                $"{obstacleData.Length} obstacles and {attractorData.Length} attractors.",
                this);
        }

        FlockObstacleData[] BuildObstacleData() {
            int staticCount = staticObstacles != null ? staticObstacles.Length : 0;
            int dynamicCount = dynamicObstacles != null ? dynamicObstacles.Length : 0;

            int totalCount = staticCount + dynamicCount;

            if (totalCount == 0) {
                dynamicObstacleIndices = System.Array.Empty<int>();
                return System.Array.Empty<FlockObstacleData>();
            }

            FlockObstacleData[] data = new FlockObstacleData[totalCount];
            int writeIndex = 0;

            // Static obstacles: no need to track indices, they never move.
            if (staticCount > 0) {
                for (int index = 0; index < staticCount; index += 1) {
                    FlockObstacle obstacle = staticObstacles[index];
                    if (obstacle == null) {
                        continue;
                    }

                    data[writeIndex] = obstacle.ToData();
                    writeIndex += 1;
                }
            }

            // Dynamic obstacles: keep index mapping so we can update positions each frame.
            if (dynamicCount > 0) {
                if (dynamicObstacleIndices == null || dynamicObstacleIndices.Length != dynamicCount) {
                    dynamicObstacleIndices = new int[dynamicCount];
                }

                for (int index = 0; index < dynamicCount; index += 1) {
                    FlockObstacle obstacle = dynamicObstacles[index];

                    if (obstacle == null) {
                        dynamicObstacleIndices[index] = -1;
                        continue;
                    }

                    data[writeIndex] = obstacle.ToData();
                    dynamicObstacleIndices[index] = writeIndex;
                    writeIndex += 1;
                }
            } else {
                dynamicObstacleIndices = System.Array.Empty<int>();
            }

            if (writeIndex < totalCount) {
                System.Array.Resize(ref data, writeIndex);
            }

            return data;
        }

        FlockEnvironmentData BuildEnvironmentData() {
            FlockEnvironmentData environmentData;

            environmentData.BoundsType = FlockBoundsType.Box;
            environmentData.BoundsCenter = boundsCenter;
            environmentData.BoundsExtents = boundsExtents;
            environmentData.BoundsRadius = math.length(boundsExtents);

            environmentData.CellSize = math.max(cellSize, 0.1f);

            float3 min = environmentData.BoundsCenter - environmentData.BoundsExtents;
            float3 max = environmentData.BoundsCenter + environmentData.BoundsExtents;
            float3 size = max - min;

            int3 resolution;
            resolution.x = math.max(1, (int)math.ceil(size.x / environmentData.CellSize));
            resolution.y = math.max(1, (int)math.ceil(size.y / environmentData.CellSize));
            resolution.z = math.max(1, (int)math.ceil(size.z / environmentData.CellSize));

            environmentData.GridOrigin = min;
            environmentData.GridResolution = resolution;
            environmentData.GlobalDamping = math.max(globalDamping, 0.0f);

            return environmentData;
        }

        // REPLACE SpawnAgents IN FlockController

        void SpawnAgents() {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"SpawnAgents called on '{name}' but simulation is not created.",
                    this);
                return;
            }

            if (fishTypes == null || fishTypes.Length == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    $"FishTypes array is empty on '{name}'. No agents will be spawned.",
                    this);
                return;
            }

            if (agentBehaviourIds == null
                || agentBehaviourIds.Length != simulation.AgentCount) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    $"SpawnAgents on '{name}' has mismatched agentBehaviourIds (null or wrong length).",
                    this);
                return;
            }

            agentTransforms.Clear();

            NativeArray<float3> positionsNative = simulation.Positions;
            int count = simulation.AgentCount;

            for (int index = 0; index < count; index += 1) {
                int behaviourIndex = agentBehaviourIds[index];

                if ((uint)behaviourIndex >= (uint)fishTypes.Length
                    || fishTypes[behaviourIndex] == null) {
                    continue;
                }

                FishTypePreset preset = fishTypes[behaviourIndex];
                GameObject prefab = preset.Prefab;

                if (prefab == null) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"FishType '{preset.DisplayName}' on '{name}' has no prefab. Skipping agent {index}.",
                        this);
                    continue;
                }

                float3 position = positionsNative[index];

                GameObject instance = Instantiate(
                    prefab,
                    position,
                    Quaternion.identity,
                    transform);

                agentTransforms.Add(instance.transform);
            }

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Spawned {agentTransforms.Count} agent instances for '{name}'.",
                this);
        }


        void ApplySimulationToTransforms() {
            NativeArray<float3> positions = simulation.Positions;
            NativeArray<float3> velocities = simulation.Velocities;

            int count = math.min(
                agentTransforms.Count,
                math.min(positions.Length, velocities.Length));

            for (int index = 0; index < count; index += 1) {
                float3 position = positions[index];
                float3 velocity = velocities[index];
                Transform agentTransform = agentTransforms[index];

                // Update position from simulation
                agentTransform.position = position;

                // Rotate fish to face its velocity direction (if moving)
                float speedSquared = math.lengthsq(velocity);
                if (speedSquared > 1e-6f) {
                    Vector3 forward = new Vector3(
                        velocity.x,
                        velocity.y,
                        velocity.z);

                    agentTransform.rotation = Quaternion.LookRotation(
                        forward.normalized,
                        Vector3.up);
                }
            }
        }

        void UpdateDynamicObstacles() {
            if (simulation == null || !simulation.IsCreated) {
                return;
            }

            if (dynamicObstacles == null
                || dynamicObstacles.Length == 0
                || dynamicObstacleIndices == null
                || dynamicObstacleIndices.Length == 0) {
                return;
            }

            int count = Mathf.Min(dynamicObstacles.Length, dynamicObstacleIndices.Length);

            for (int index = 0; index < count; index += 1) {
                int obstacleIndex = dynamicObstacleIndices[index];
                if (obstacleIndex < 0) {
                    continue;
                }

                FlockObstacle obstacle = dynamicObstacles[index];
                if (obstacle == null) {
                    continue;
                }

                FlockObstacleData data = obstacle.ToData();
                simulation.SetObstacleData(obstacleIndex, data);
            }
        }

        FlockAttractorData[] BuildAttractorData() {
            int staticCount = staticAttractors != null ? staticAttractors.Length : 0;
            int dynamicCount = dynamicAttractors != null ? dynamicAttractors.Length : 0;

            int totalCount = 0;

            if (staticCount > 0) {
                for (int i = 0; i < staticCount; i += 1) {
                    if (staticAttractors[i] != null) {
                        totalCount += 1;
                    }
                }
            }

            if (dynamicCount > 0) {
                for (int i = 0; i < dynamicCount; i += 1) {
                    if (dynamicAttractors[i] != null) {
                        totalCount += 1;
                    }
                }
            }

            if (totalCount == 0) {
                dynamicAttractorIndices = System.Array.Empty<int>();
                return System.Array.Empty<FlockAttractorData>();
            }

            FlockAttractorData[] data = new FlockAttractorData[totalCount];
            int writeIndex = 0;

            // Static attractors
            if (staticCount > 0) {
                for (int i = 0; i < staticCount; i += 1) {
                    FlockAttractorArea area = staticAttractors[i];
                    if (area == null) {
                        continue;
                    }

                    uint mask = ComputeAttractorMask(area);
                    data[writeIndex] = area.ToData(mask);
                    writeIndex += 1;
                }
            }

            // Dynamic attractors
            if (dynamicCount > 0) {
                if (dynamicAttractorIndices == null || dynamicAttractorIndices.Length != dynamicCount) {
                    dynamicAttractorIndices = new int[dynamicCount];
                }

                for (int i = 0; i < dynamicCount; i += 1) {
                    FlockAttractorArea area = dynamicAttractors[i];

                    if (area == null) {
                        dynamicAttractorIndices[i] = -1;
                        continue;
                    }

                    uint mask = ComputeAttractorMask(area);
                    data[writeIndex] = area.ToData(mask);
                    dynamicAttractorIndices[i] = writeIndex;
                    writeIndex += 1;
                }
            } else {
                dynamicAttractorIndices = System.Array.Empty<int>();
            }

            if (writeIndex < totalCount) {
                System.Array.Resize(ref data, writeIndex);
            }

            return data;
        }

        // --- NEW: helper to turn area.AttractedTypes into a bitmask over fishTypes[] ---
        uint ComputeAttractorMask(FlockAttractorArea area) {
            if (area == null || fishTypes == null || fishTypes.Length == 0) {
                return uint.MaxValue; // affect all types
            }

            FishTypePreset[] targetTypes = area.AttractedTypes;
            if (targetTypes == null || targetTypes.Length == 0) {
                return uint.MaxValue; // affect all types
            }

            uint mask = 0u;

            for (int t = 0; t < targetTypes.Length; t += 1) {
                FishTypePreset target = targetTypes[t];
                if (target == null) {
                    continue;
                }

                for (int i = 0; i < fishTypes.Length; i += 1) {
                    if (fishTypes[i] == target) {
                        if (i < 32) {
                            mask |= (1u << i);
                        }
                        break;
                    }
                }
            }

            if (mask == 0u) {
                return uint.MaxValue; // fallback: if mapping failed, affect everyone
            }

            return mask;
        }

        void UpdateDynamicAttractors() {
            if (simulation == null || !simulation.IsCreated) {
                return;
            }

            if (dynamicAttractors == null
                || dynamicAttractors.Length == 0
                || dynamicAttractorIndices == null
                || dynamicAttractorIndices.Length == 0) {
                return;
            }

            int count = Mathf.Min(dynamicAttractors.Length, dynamicAttractorIndices.Length);
            bool anyUpdated = false;

            for (int i = 0; i < count; i += 1) {
                int attractorIndex = dynamicAttractorIndices[i];
                if (attractorIndex < 0) {
                    continue;
                }

                FlockAttractorArea area = dynamicAttractors[i];
                if (area == null) {
                    continue;
                }

                uint mask = ComputeAttractorMask(area);
                FlockAttractorData data = area.ToData(mask);
                simulation.SetAttractorData(attractorIndex, data);
                anyUpdated = true;
            }

            // Re-stamp attractors into grid if anything moved / changed
            if (anyUpdated) {
                simulation.RebuildAttractorGrid();
            }
        }

        #region Debug

        void OnDrawGizmosSelected() {
            // Rebuild environment data from current inspector values.
            // This matches what the simulation was created with, as long as you do not
            // change values at runtime via code.
            FlockEnvironmentData environmentData = BuildEnvironmentData();

            if (debugDrawBounds) {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(
                    environmentData.BoundsCenter,
                    environmentData.BoundsExtents * 2.0f);
            }

            if (!Application.isPlaying || simulation == null || !simulation.IsCreated) {
                return;
            }

            NativeArray<float3> positions = simulation.Positions;

            if (debugDrawGrid) {
                DrawGridGizmos(environmentData);
            }

            if (debugDrawAgents) {
                DrawAgentsGizmos(positions);
            }

            if (debugDrawNeighbourhood) {
                DrawNeighbourhoodGizmos(positions, environmentData);
            }
        }

        void DrawGridGizmos(FlockEnvironmentData environmentData) {
            float3 origin = environmentData.GridOrigin;
            float cell = environmentData.CellSize;
            int3 res = environmentData.GridResolution;

            // Safety guard – drawing millions of cubes will tank the editor.
            int totalCells = res.x * res.y * res.z;
            if (totalCells > 10_000) {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.6f, 1.0f, 0.15f);

            for (int x = 0; x < res.x; x += 1) {
                for (int y = 0; y < res.y; y += 1) {
                    for (int z = 0; z < res.z; z += 1) {
                        float3 center = origin + new float3(
                            (x + 0.5f) * cell,
                            (y + 0.5f) * cell,
                            (z + 0.5f) * cell);

                        Gizmos.DrawWireCube(
                            center,
                            new float3(cell, cell, cell));
                    }
                }
            }
        }

        void DrawAgentsGizmos(NativeArray<float3> positions) {
            Gizmos.color = Color.cyan;

            int length = positions.Length;

            for (int index = 0; index < length; index += 1) {
                Gizmos.DrawSphere(
                    positions[index],
                    0.1f);
            }
        }

        void DrawNeighbourhoodGizmos(
            NativeArray<float3> positions,
            FlockEnvironmentData environmentData) {
            int length = positions.Length;
            if (length == 0) {
                return;
            }

            int index = math.clamp(
                debugAgentIndex,
                0,
                length - 1);

            float3 agentPosition = positions[index];

            // For now we use the first behaviour profile's neighbour radius.
            float neighbourRadius = 0.0f;
            if (behaviourSettingsArray.IsCreated && behaviourSettingsArray.Length > 0) {
                neighbourRadius = behaviourSettingsArray[0].NeighbourRadius;
            }

            if (neighbourRadius <= 0.0f) {
                return;
            }

            // Draw the selected agent.
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(agentPosition, 0.15f);

            // Draw its perception radius.
            Gizmos.color = new Color(1.0f, 1.0f, 0.0f, 0.25f);
            Gizmos.DrawWireSphere(
                agentPosition,
                neighbourRadius);

            // Highlight its grid cell.
            int3 cell = GetCell(agentPosition, environmentData);
            float cellSize = environmentData.CellSize;
            float3 origin = environmentData.GridOrigin;
            float3 cellCenter = origin + new float3(
                (cell.x + 0.5f) * cellSize,
                (cell.y + 0.5f) * cellSize,
                (cell.z + 0.5f) * cellSize);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(
                cellCenter,
                new float3(cellSize, cellSize, cellSize));

            // Draw neighbours inside radius.
            float radiusSquared = neighbourRadius * neighbourRadius;
            Gizmos.color = Color.red;

            for (int i = 0; i < length; i += 1) {
                if (i == index) {
                    continue;
                }

                float3 other = positions[i];
                float3 offset = other - agentPosition;
                float distanceSquared = math.lengthsq(offset);

                if (distanceSquared <= radiusSquared) {
                    Gizmos.DrawSphere(other, 0.12f);
                }
            }
        }

        static int3 GetCell(
            float3 position,
            FlockEnvironmentData environmentData) {
            float3 local = position - environmentData.GridOrigin;
            float3 scaled = local / math.max(environmentData.CellSize, 0.0001f);

            int3 cell = (int3)math.floor(scaled);
            int3 max = environmentData.GridResolution - new int3(1, 1, 1);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                max);
        }
        #endregion

    }
}
