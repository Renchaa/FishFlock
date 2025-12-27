// File: Assets/Flock/Runtime/FlockController.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using System;
    using System.Collections.Generic;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;
    using UnityEngine;

    /**
    * <summary>
    * Creates, owns, and steps the flock simulation, and binds it to spawned agent GameObjects.
    * </summary>
    */
    public sealed partial class FlockController : MonoBehaviour, IFlockLogger {
        [Header("Fish Types"), HideInInspector]
        [SerializeField] private FishTypePreset[] fishTypes;

        [Header("Spawning")]
        [SerializeField] private FlockMainSpawner mainSpawner;

        [Header("Group Noise Pattern")]
        [SerializeField] private GroupNoisePatternProfile groupNoisePattern;

        [Header("Bounds")]
        [SerializeField] private FlockBoundsType boundsType = FlockBoundsType.Box;
        [SerializeField] private Vector3 boundsCenter = Vector3.zero;
        [SerializeField] private Vector3 boundsExtents = new Vector3(10.0f, 10.0f, 10.0f);

        // Used when boundsType == Sphere
        [SerializeField, Min(0f)] private float boundsSphereRadius = 10.0f;

        [Header("Grid")]
        [SerializeField] private float cellSize = 2.0f;

        [Header("Movement")]
        [SerializeField] private float globalDamping = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool debugDrawBounds = true;
        [SerializeField] private bool debugDrawGrid = false;
        [SerializeField] private bool debugDrawAgents = true;
        [SerializeField] private bool debugDrawNeighbourhood = false;
        [SerializeField, Min(0)] private int debugAgentIndex = 0;

        // NEW: per-agent radius debug toggles
        [SerializeField] private bool debugDrawBodyRadius = true;
        [SerializeField] private bool debugDrawSeparationRadius = true;
        [SerializeField] private bool debugDrawNeighbourRadiusSphere = true;
        [SerializeField] private bool debugDrawGridSearchRadiusSphere = false;

        [Header("Obstacles")]
        [SerializeField] private FlockObstacle[] staticObstacles;
        [SerializeField] private FlockObstacle[] dynamicObstacles;

        [Header("Logging")]
        [SerializeField] private FlockLogLevel enabledLogLevels = FlockLogLevel.All;
        [SerializeField] private FlockLogCategory enabledLogCategories = FlockLogCategory.All;

        [Header("Debug Obstacle Avoidance")]
        [SerializeField] private bool debugObstacleAvoidance = false;
        [SerializeField, Range(1, 32)] private int debugObstacleCellsToLog = 4;

        [Header("Interactions")]
        [SerializeField] private FishInteractionMatrix interactionMatrix;

        [Header("Attractors")]
        [SerializeField] private FlockAttractorArea[] staticAttractors;
        [SerializeField] private FlockAttractorArea[] dynamicAttractors;

        [Header("Layer-3 Patterns")]
        [SerializeField] private FlockLayer3PatternProfile[] layer3Patterns;

        /**
        * <summary>
        * Gets the enabled log levels for this controller.
        * </summary>
        */
        public FlockLogLevel EnabledLevels => enabledLogLevels;

        /**
        * <summary>
        * Gets the enabled log categories for this controller.
        * </summary>
        */
        public FlockLogCategory EnabledCategories => enabledLogCategories;

        /**
        * <summary>
        * Gets the fish type presets used by this controller (index = behaviour id).
        * </summary>
        */
        public FishTypePreset[] FishTypes => fishTypes;

        /**
        * <summary>
        * Gets the main spawner used to build agent distributions and initial positions.
        * </summary>
        */
        public FlockMainSpawner MainSpawner => mainSpawner;

        /**
        * <summary>
        * Gets the layer-3 pattern profiles used by the simulation.
        * </summary>
        */
        public FlockLayer3PatternProfile[] Layer3Patterns => layer3Patterns;

        private FlockSimulation simulation;
        private NativeArray<FlockBehaviourSettings> behaviourSettingsArray;
        private int[] agentBehaviourIds;
        private int totalAgentCount;

        private readonly List<Transform> agentTransforms = new List<Transform>();

        private void Awake() {
            // Configure global logging filter from this controller
            FlockLog.SetGlobalMask(enabledLogLevels, enabledLogCategories);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"FlockController.Awake on '{name}'.",
                this);

            CreateSimulation();
            SpawnAgents();
        }

        private void Update() {
            if (simulation == null || !simulation.IsCreated) {
                return;
            }

            UpdateDynamicObstacles();
            UpdateDynamicAttractors();    // NEW

            JobHandle handle = simulation.ScheduleStepJobs(Time.deltaTime);
            handle.Complete();

            ApplySimulationToTransforms();
        }

        private void OnDestroy() {
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

        private void ApplyLayer2GroupNoisePatternToSimulation() {
            if (simulation == null || !simulation.IsCreated) {
                return;
            }

            // Default if nothing assigned
            if (groupNoisePattern == null) {
                simulation.SetLayer2GroupNoiseSimpleSine(
                    FlockGroupNoiseCommonSettings.Default,
                    FlockGroupNoiseSimpleSinePayload.Default);
                return;
            }

            var common = groupNoisePattern.ToCommonSettings();

            switch (groupNoisePattern.PatternType) {
                case FlockGroupNoisePatternType.VerticalBands:
                    simulation.SetLayer2GroupNoiseVerticalBands(common, groupNoisePattern.ToVerticalBandsPayload());
                    break;

                case FlockGroupNoisePatternType.Vortex:
                    simulation.SetLayer2GroupNoiseVortex(common, groupNoisePattern.ToVortexPayload());
                    break;

                case FlockGroupNoisePatternType.SphereShell:
                    simulation.SetLayer2GroupNoiseSphereShell(common, groupNoisePattern.ToSphereShellPayload());
                    break;

                case FlockGroupNoisePatternType.SimpleSine:
                default:
                    simulation.SetLayer2GroupNoiseSimpleSine(common, groupNoisePattern.ToSimpleSinePayload());
                    break;
            }
        }

        private void CreateBehaviourSettingsArray() {
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

            for (int index = 0; index < behaviourCount; index += 1) {
                FishTypePreset preset = fishTypes[index];
                if (preset == null) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"FishTypes[{index}] is null on '{name}'. Skipping.",
                        this);
                    behaviourSettingsArray[index] = default;
                    continue;
                }

                FlockBehaviourSettings settings = preset.ToSettings();

                // Leadership weight from matrix (or default)
                if (compiledLeadership != null && index < compiledLeadership.Length) {
                    settings.LeadershipWeight = compiledLeadership[index];
                } else {
                    settings.LeadershipWeight = 1.0f;
                }

                // Avoidance weight from matrix (or default)
                if (compiledAvoidance != null && index < compiledAvoidance.Length) {
                    settings.AvoidanceWeight = compiledAvoidance[index];
                } else {
                    settings.AvoidanceWeight = 1.0f;
                }

                // Neutral weight from matrix (or default)
                if (compiledNeutral != null && index < compiledNeutral.Length) {
                    settings.NeutralWeight = compiledNeutral[index];
                } else {
                    settings.NeutralWeight = 1.0f;
                }

                // Relationship masks
                settings.GroupMask = compiledFriendlyMasks != null && index < compiledFriendlyMasks.Length
                    ? compiledFriendlyMasks[index]
                    : 0u;

                settings.AvoidMask = compiledAvoidMasks != null && index < compiledAvoidMasks.Length
                    ? compiledAvoidMasks[index]
                    : 0u;

                settings.NeutralMask = compiledNeutralMasks != null && index < compiledNeutralMasks.Length
                    ? compiledNeutralMasks[index]
                    : 0u;

                behaviourSettingsArray[index] = settings;
            }

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Created behaviour settings for {behaviourCount} fish types on '{name}'.",
                this);
        }

        private void CreateSimulation() {
            CreateBehaviourSettingsArray();

            if (!behaviourSettingsArray.IsCreated || behaviourSettingsArray.Length == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    $"Behaviour settings array is not created or empty on '{name}'.",
                    this);
                return;
            }

            if (mainSpawner == null) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    $"FlockController '{name}' has no FlockMainSpawner assigned.",
                    this);
                return;
            }

            // Build agent behaviour IDs from spawner (all counts live there now)
            agentBehaviourIds = mainSpawner.BuildAgentBehaviourIds(fishTypes);

            if (agentBehaviourIds == null || agentBehaviourIds.Length == 0) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"CreateSimulation on '{name}' aborted: spawner returned no agents.",
                    this);
                return;
            }

            totalAgentCount = agentBehaviourIds.Length;

            FlockEnvironmentData environmentData = BuildEnvironmentData();
            FlockObstacleData[] obstacleData = BuildObstacleData();
            FlockAttractorData[] attractorData = BuildAttractorData();   // NEW

            simulation = new FlockSimulation();

            simulation.Initialize(
                agentBehaviourIds.Length,
                environmentData,
                behaviourSettingsArray,
                obstacleData,
                attractorData,
                Allocator.Persistent,
                this);

            ApplyLayer2GroupNoisePatternToSimulation();

            simulation.SetAgentBehaviourIds(agentBehaviourIds);

            BuildLayer3PatternRuntime(
                environmentData,
                out var layer3Commands,
                out var layer3SphereShells,
                out var layer3BoxShells);

            simulation.SetLayer3Patterns(
                layer3Commands,
                layer3SphereShells,
                layer3BoxShells);

            // Let the spawner write initial positions into the simulation
            mainSpawner.AssignInitialPositions(
                environmentData,
                fishTypes,
                agentBehaviourIds,
                simulation.Positions);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Created FlockSimulation with {agentBehaviourIds.Length} agents for '{name}', " +
                $"{obstacleData.Length} obstacles and {attractorData.Length} attractors.",
                this);
        }

        private FlockEnvironmentData BuildEnvironmentData() {
            FlockEnvironmentData environmentData;

            environmentData.BoundsType = boundsType;
            environmentData.BoundsCenter = boundsCenter;

            if (boundsType == FlockBoundsType.Box) {
                // Classic AABB bounds
                environmentData.BoundsExtents = boundsExtents;
                environmentData.BoundsRadius = math.length(boundsExtents);
            } else {
                // Spherical bounds: radius drives clamp; extents define grid volume (cube around sphere)
                float radius = math.max(boundsSphereRadius, 0.1f);
                environmentData.BoundsRadius = radius;
                environmentData.BoundsExtents = new float3(radius, radius, radius);
            }

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

            if (groupNoisePattern != null) {
                environmentData.GroupNoisePattern = groupNoisePattern.ToSettings();
            } else {
                environmentData.GroupNoisePattern = FlockGroupNoisePatternSettings.Default;
            }

            return environmentData;
        }

        private void SpawnAgents() {
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

        private void ApplySimulationToTransforms() {
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
    }
}
