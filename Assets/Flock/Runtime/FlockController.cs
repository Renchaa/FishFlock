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

    public sealed class FlockController : MonoBehaviour, IFlockLogger {


        [Header("Fish Types"), HideInInspector]
        [SerializeField] FishTypePreset[] fishTypes;

        [Header("Spawning")]
        [SerializeField] FlockMainSpawner mainSpawner;

        [Header("Group Noise Pattern")]
        [SerializeField] GroupNoisePatternProfile groupNoisePattern;

        [Header("Bounds")]
        [SerializeField] FlockBoundsType boundsType = FlockBoundsType.Box;
        [SerializeField] Vector3 boundsCenter = Vector3.zero;
        [SerializeField] Vector3 boundsExtents = new Vector3(10.0f, 10.0f, 10.0f);

        // Used when boundsType == Sphere
        [SerializeField, Min(0f)] float boundsSphereRadius = 10.0f;

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

        // NEW: per-agent radius debug toggles
        [SerializeField] bool debugDrawBodyRadius = true;
        [SerializeField] bool debugDrawSeparationRadius = true;
        [SerializeField] bool debugDrawNeighbourRadiusSphere = true;
        [SerializeField] bool debugDrawGridSearchRadiusSphere = false;

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

        [Header("Layer-3 Patterns")]
        [SerializeField] FlockLayer3PatternProfile[] layer3Patterns;

        int[] dynamicAttractorIndices;

        public FlockLogLevel EnabledLevels => enabledLogLevels;
        public FlockLogCategory EnabledCategories => enabledLogCategories;
        public FishTypePreset[] FishTypes => fishTypes;
        public FlockMainSpawner MainSpawner => mainSpawner;
        public FlockLayer3PatternProfile[] Layer3Patterns => layer3Patterns;

        FlockSimulation simulation;
        NativeArray<FlockBehaviourSettings> behaviourSettingsArray;
        int[] agentBehaviourIds;
        int totalAgentCount;

        readonly List<Transform> agentTransforms = new List<Transform>();
        // Runtime Layer-3 start/update scratch (reused to avoid allocations)
        readonly List<FlockLayer3PatternCommand> runtimeL3CmdScratch = new List<FlockLayer3PatternCommand>(4);
        readonly List<FlockLayer3PatternSphereShell> runtimeL3SphereScratch = new List<FlockLayer3PatternSphereShell>(4);
        readonly List<FlockLayer3PatternBoxShell> runtimeL3BoxShellScratch = new List<FlockLayer3PatternBoxShell>(4);
        readonly List<FlockLayer3PatternHandle> runtimeL3HandleScratch = new List<FlockLayer3PatternHandle>(4);

        void Awake() {
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

        /// <summary>
        /// Starts a runtime Layer-3 pattern instance from a ScriptableObject profile.
        /// Returns a ScriptableObject token you keep to Update/Stop later.
        /// </summary>
        public FlockLayer3PatternToken StartLayer3Pattern(FlockLayer3PatternProfile profile) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return null;
            }

            if (profile == null) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' with null profile.",
                    this);
                return null;
            }

            runtimeL3HandleScratch.Clear();

            if (!TryBuildRuntimeLayer3FromProfile(profile, runtimeL3HandleScratch)) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern on '{name}' for profile '{profile.name}' produced no valid runtime commands.",
                    this);
                return null;
            }

            var token = ScriptableObject.CreateInstance<FlockLayer3PatternToken>();
            token.hideFlags = HideFlags.DontSave;
            token.SetHandles(runtimeL3HandleScratch);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Started Layer-3 pattern '{profile.name}' with {runtimeL3HandleScratch.Count} runtime handles on '{name}'.",
                this);

            return token;
        }

        /// <summary>
        /// Updates a previously started runtime Layer-3 pattern token from a ScriptableObject profile.
        /// If handle count mismatches (profile bakes different commands), this will Stop+Recreate into the same token.
        /// </summary>
        public bool UpdateLayer3Pattern(FlockLayer3PatternToken token, FlockLayer3PatternProfile profile) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (token == null) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' with null token.",
                    this);
                return false;
            }

            if (profile == null) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' with null profile.",
                    this);
                return false;
            }

            // Bake fresh commands/payloads from the profile
            FlockEnvironmentData env = BuildEnvironmentData();

            runtimeL3CmdScratch.Clear();
            runtimeL3SphereScratch.Clear();
            runtimeL3BoxShellScratch.Clear();

            profile.Bake(env, fishTypes, runtimeL3CmdScratch, runtimeL3SphereScratch, runtimeL3BoxShellScratch);

            // If profile currently bakes nothing -> stop whatever was running
            if (runtimeL3CmdScratch.Count == 0) {
                StopLayer3Pattern(token);
                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' stopped token because profile '{profile.name}' baked no commands.",
                    this);
                return true;
            }

            // If the token doesn't match the baked command count, rebuild the token in-place
            if (token.HandleCount != runtimeL3CmdScratch.Count) {
                StopLayer3Pattern(token);

                runtimeL3HandleScratch.Clear();
                if (!TryBuildRuntimeLayer3FromProfile(profile, runtimeL3HandleScratch)) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"UpdateLayer3Pattern on '{name}' failed to rebuild token for profile '{profile.name}'.",
                        this);
                    return false;
                }

                token.SetHandles(runtimeL3HandleScratch);

                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' rebuilt token for profile '{profile.name}' with {runtimeL3HandleScratch.Count} handles.",
                    this);

                return true;
            }

            bool allOk = true;

            // Update each handle using the baked command list order
            for (int i = 0; i < runtimeL3CmdScratch.Count; i += 1) {
                FlockLayer3PatternCommand cmd = runtimeL3CmdScratch[i];
                FlockLayer3PatternHandle handle = token.GetHandle(i);

                if (cmd.Strength <= 0f) {
                    // If command bakes but strength is 0, stop this one and invalidate via restart
                    simulation.StopLayer3Pattern(handle);
                    allOk = false;
                    continue;
                }

                switch (cmd.Kind) {
                    case FlockLayer3PatternKind.SphereShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3SphereScratch.Count) {
                                allOk = false;
                                continue;
                            }

                            FlockLayer3PatternSphereShell s = runtimeL3SphereScratch[cmd.PayloadIndex];

                            bool ok = simulation.UpdatePatternSphereShell(
                                handle,
                                s.Center,
                                s.Radius,
                                s.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (!ok) {
                                // stale/invalid handle -> restart just this entry
                                simulation.StopLayer3Pattern(handle);

                                FlockLayer3PatternHandle newHandle = simulation.StartPatternSphereShell(
                                    s.Center,
                                    s.Radius,
                                    s.Thickness,
                                    cmd.Strength,
                                    cmd.BehaviourMask);

                                if (!newHandle.IsValid) {
                                    allOk = false;
                                    continue;
                                }

                                token.ReplaceHandle(i, newHandle);
                            }

                            break;
                        }
                    case FlockLayer3PatternKind.BoxShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3BoxShellScratch.Count) {
                                allOk = false;
                                continue;
                            }

                            FlockLayer3PatternBoxShell b = runtimeL3BoxShellScratch[cmd.PayloadIndex];

                            bool ok = simulation.UpdatePatternBoxShell(
                                handle,
                                b.Center,
                                b.HalfExtents,
                                b.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (!ok) {
                                // stale/invalid handle -> restart just this entry
                                simulation.StopLayer3Pattern(handle);

                                FlockLayer3PatternBoxShell rb = b;

                                FlockLayer3PatternHandle newHandle = simulation.StartPatternBoxShell(
                                    rb.Center,
                                    rb.HalfExtents,
                                    rb.Thickness,
                                    cmd.Strength,
                                    cmd.BehaviourMask);

                                if (!newHandle.IsValid) {
                                    allOk = false;
                                    continue;
                                }

                                token.ReplaceHandle(i, newHandle);
                            }

                            break;
                        }

                    default: {
                            // Unsupported runtime kind -> treat as failure
                            allOk = false;
                            break;
                        }
                }
            }

            if (!allOk) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' for profile '{profile.name}' had to restart or skip one or more runtime handles.",
                    this);
            }

            return allOk;
        }

        /// <summary>
        /// Stops all runtime handles owned by this token (ScriptableObject-only control).
        /// </summary>
        public bool StopLayer3Pattern(FlockLayer3PatternToken token) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (token == null || !token.IsValid) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopLayer3Pattern called on '{name}' with null or invalid token.",
                    this);
                return false;
            }

            bool anyStopped = false;

            int count = token.HandleCount;
            for (int i = 0; i < count; i += 1) {
                anyStopped |= simulation.StopLayer3Pattern(token.GetHandle(i));
            }

            token.Invalidate();

            if (anyStopped) {
                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"Stopped Layer-3 pattern token with {count} handles on '{name}'.",
                    this);
            }

            return anyStopped;
        }

        public FlockLayer3PatternHandle StartRuntimeSphereShell(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartRuntimeSphereShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return FlockLayer3PatternHandle.Invalid;
            }

            return simulation.StartPatternSphereShell(
                center,
                radius,
                thickness,
                strength,
                behaviourMask);
        }

        public bool UpdateRuntimeSphereShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateRuntimeSphereShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            // We deliberately do NOT log if the underlying simulation returns false –
            // that can be per-frame and the caller already gets the bool result.
            return simulation.UpdatePatternSphereShell(
                handle,
                center,
                radius,
                thickness,
                strength,
                behaviourMask);
        }

        public FlockLayer3PatternHandle StartRuntimeBoxShell(
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartRuntimeBoxShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return FlockLayer3PatternHandle.Invalid;
            }

            return simulation.StartPatternBoxShell(
                center,
                halfExtents,
                thickness,
                strength,
                behaviourMask);
        }

        public bool UpdateRuntimeBoxShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateRuntimeBoxShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            // Same here – caller gets the bool, we don't spam logs from per-frame updates.
            return simulation.UpdatePatternBoxShell(
                handle,
                center,
                halfExtents,
                thickness,
                strength,
                behaviourMask);
        }

        public bool StopRuntimePattern(FlockLayer3PatternHandle handle) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopRuntimePattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            return simulation.StopLayer3Pattern(handle);
        }

        // ----------------------------------------------------------------------
        // Private helper: bakes a profile and starts the corresponding runtime handles.
        // This is where you add new kinds later (switch on cmd.Kind).
        // ----------------------------------------------------------------------
        bool TryBuildRuntimeLayer3FromProfile(
            FlockLayer3PatternProfile profile,
            List<FlockLayer3PatternHandle> outHandles) {

            outHandles.Clear();

            FlockEnvironmentData env = BuildEnvironmentData();

            runtimeL3CmdScratch.Clear();
            runtimeL3SphereScratch.Clear();
            runtimeL3BoxShellScratch.Clear();

            profile.Bake(env, fishTypes, runtimeL3CmdScratch, runtimeL3SphereScratch, runtimeL3BoxShellScratch);

            if (runtimeL3CmdScratch.Count == 0) {
                return false;
            }

            for (int i = 0; i < runtimeL3CmdScratch.Count; i += 1) {
                FlockLayer3PatternCommand cmd = runtimeL3CmdScratch[i];

                if (cmd.Strength <= 0f) {
                    continue;
                }

                switch (cmd.Kind) {
                    case FlockLayer3PatternKind.SphereShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3SphereScratch.Count) {
                                continue;
                            }

                            FlockLayer3PatternSphereShell s = runtimeL3SphereScratch[cmd.PayloadIndex];

                            FlockLayer3PatternHandle handle = simulation.StartPatternSphereShell(
                                s.Center,
                                s.Radius,
                                s.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (handle.IsValid) {
                                outHandles.Add(handle);
                            }

                            break;
                        }

                    case FlockLayer3PatternKind.BoxShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3BoxShellScratch.Count) {
                                continue;
                            }

                            FlockLayer3PatternBoxShell b = runtimeL3BoxShellScratch[cmd.PayloadIndex];

                            FlockLayer3PatternHandle handle = simulation.StartPatternBoxShell(
                                b.Center,
                                b.HalfExtents,
                                b.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (handle.IsValid) {
                                outHandles.Add(handle);
                            }

                            break;
                        }

                    default: {
                            // Not supported at runtime yet
                            FlockLog.Warning(
                                this,
                                FlockLogCategory.Controller,
                                $"StartLayer3Pattern: runtime kind '{cmd.Kind}' is not implemented in controller yet.",
                                this);
                            break;
                        }
                }
            }

            return outHandles.Count > 0;
        }

        void ApplyLayer2GroupNoisePatternToSimulation() {
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
            }

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Created behaviour settings for {behaviourCount} fish types on '{name}'.",
                this);
        }

        // REPLACE CreateSimulation IN FlockController.cs
        // REPLACE CreateSimulation IN FlockController.cs
        void CreateSimulation() {
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

        void BuildLayer3PatternRuntime(
            in FlockEnvironmentData env,
            out FlockLayer3PatternCommand[] commands,
            out FlockLayer3PatternSphereShell[] sphereShells,
            out FlockLayer3PatternBoxShell[] boxShells) {


            if (layer3Patterns == null || layer3Patterns.Length == 0) {
                commands = Array.Empty<FlockLayer3PatternCommand>();
                sphereShells = Array.Empty<FlockLayer3PatternSphereShell>();
                boxShells = Array.Empty<FlockLayer3PatternBoxShell>();
                return;
            }

            var cmdList = new List<FlockLayer3PatternCommand>(layer3Patterns.Length);
            var sphereList = new List<FlockLayer3PatternSphereShell>(layer3Patterns.Length);
            var boxList = new List<FlockLayer3PatternBoxShell>(layer3Patterns.Length);

            for (int i = 0; i < layer3Patterns.Length; i += 1) {
                var profile = layer3Patterns[i];
                if (profile == null) {
                    continue;
                }

                profile.Bake(
                    env,
                    fishTypes,
                    cmdList,
                    sphereList,
                    boxList);
            }

            commands = cmdList.Count > 0 ? cmdList.ToArray() : Array.Empty<FlockLayer3PatternCommand>();
            sphereShells = sphereList.Count > 0 ? sphereList.ToArray() : Array.Empty<FlockLayer3PatternSphereShell>();
            boxShells = boxList.Count > 0 ? boxList.ToArray() : Array.Empty<FlockLayer3PatternBoxShell>();
        }


        #region Debug

        // ===== FlockController.cs – ONLY REPLACE THESE TWO METHODS =====

        // ============================
        // FlockController.cs – REPLACE OnDrawGizmosSelected
        // ============================
        // ---------------- FlockController.cs ----------------
        // REPLACE THIS METHOD
        void OnDrawGizmosSelected() {
            // Rebuild environment data from current inspector values.
            FlockEnvironmentData environmentData = BuildEnvironmentData();

            if (debugDrawBounds) {
                DrawBoundsGizmos(environmentData);
            }

            if (!Application.isPlaying || simulation == null || !simulation.IsCreated) {
                return;
            }

            NativeArray<float3> positions = simulation.Positions;
            NativeArray<float3> velocities = simulation.Velocities;

            if (debugDrawGrid) {
                DrawGridGizmos(environmentData);
            }

            // Per-fish radii for ALL agents (uses shared debug toggles)
            if (debugDrawAgents) {
                DrawAgentsGizmos(positions);
            }

            // Single-agent detailed view – shows neighbour radius,
            // search radius (cellRange), bounds look-ahead, etc.
            if (debugDrawNeighbourhood) {
                DrawNeighbourhoodGizmos(positions, velocities, environmentData);
            }
        }

        // ADD THIS NEW METHOD
        void DrawBoundsGizmos(FlockEnvironmentData environmentData) {
            float3 center = environmentData.BoundsCenter;
            Gizmos.color = Color.green;

            if (environmentData.BoundsType == FlockBoundsType.Sphere && environmentData.BoundsRadius > 0f) {
                Gizmos.DrawWireSphere(
                    (Vector3)center,
                    environmentData.BoundsRadius);
            } else {
                float3 extents = environmentData.BoundsExtents;
                Gizmos.DrawWireCube(
                    (Vector3)center,
                    (Vector3)(extents * 2f));
            }
        }

        // REPLACE THIS METHOD SIGNATURE + BODY
        void DrawNeighbourhoodGizmos(
            NativeArray<float3> positions,
            NativeArray<float3> velocities,
            FlockEnvironmentData environmentData) {

            int length = positions.Length;
            if (length == 0
                || !behaviourSettingsArray.IsCreated
                || behaviourSettingsArray.Length == 0) {
                return;
            }

            int index = math.clamp(
                debugAgentIndex,
                0,
                length - 1);

            float3 agentPosition = positions[index];

            float3 agentVelocity = float3.zero;
            if (velocities.IsCreated
                && index >= 0
                && index < velocities.Length) {
                agentVelocity = velocities[index];
            }

            // Resolve this agent's behaviour index
            int behaviourIndex = 0;
            if (agentBehaviourIds != null
                && index >= 0
                && index < agentBehaviourIds.Length) {
                behaviourIndex = math.clamp(
                    agentBehaviourIds[index],
                    0,
                    behaviourSettingsArray.Length - 1);
            }

            FlockBehaviourSettings settings = behaviourSettingsArray[behaviourIndex];

            float neighbourRadius = settings.NeighbourRadius;
            float separationRadius = settings.SeparationRadius;
            float bodyRadius = settings.BodyRadius;

            if (neighbourRadius <= 0.0f
                && separationRadius <= 0.0f
                && bodyRadius <= 0.0f) {
                return;
            }

            // Highlight the selected agent itself
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere((Vector3)agentPosition, 0.2f);

            // Body radius (physical size)
            if (debugDrawBodyRadius && bodyRadius > 0f) {
                Gizmos.color = new Color(0f, 1f, 1f, 0.7f); // cyan
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    bodyRadius);
            }

            // Separation radius (hard "back off" bubble)
            if (debugDrawSeparationRadius && separationRadius > 0f) {
                Gizmos.color = new Color(1f, 0f, 0f, 0.5f); // red
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    separationRadius);
            }

            // Neighbour perception radius (logical view distance)
            if (debugDrawNeighbourRadiusSphere && neighbourRadius > 0f) {
                Gizmos.color = new Color(1f, 1f, 0f, 0.35f); // yellow
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    neighbourRadius);
            }

            // Grid search radius in world units (how far in cells this type actually scans)
            if (debugDrawGridSearchRadiusSphere
                && neighbourRadius > 0f
                && environmentData.CellSize > 0.0001f) {

                float cellSize = environmentData.CellSize;

                int cellRange = Mathf.Max(
                    1,
                    Mathf.CeilToInt(neighbourRadius / cellSize));

                float gridSearchWorldRadius = cellRange * cellSize;

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f); // orange
                Gizmos.DrawWireSphere(
                    (Vector3)agentPosition,
                    gridSearchWorldRadius);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(
                    (Vector3)agentPosition + Vector3.up * (gridSearchWorldRadius + 0.25f),
                    $"beh={behaviourIndex}, cellRange={cellRange}, neighR={neighbourRadius:0.##}");
#endif
            }

            // Highlight its grid cell
            int3 cell = GetCell(agentPosition, environmentData);
            float cellSizeG = environmentData.CellSize;
            float3 origin = environmentData.GridOrigin;
            float3 cellCenter = origin + new float3(
                (cell.x + 0.5f) * cellSizeG,
                (cell.y + 0.5f) * cellSizeG,
                (cell.z + 0.5f) * cellSizeG);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(
                (Vector3)cellCenter,
                new float3(cellSizeG, cellSizeG, cellSizeG));

            // Draw neighbours inside logical neighbour radius
            if (neighbourRadius > 0f) {
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
                        Gizmos.DrawSphere((Vector3)other, 0.12f);
                    }
                }
            }
        }

        // ============================
        // FlockController.cs – REPLACE DrawAgentsGizmos
        // ============================
        void DrawAgentsGizmos(NativeArray<float3> positions) {
            int length = positions.Length;
            if (length == 0
                || !behaviourSettingsArray.IsCreated
                || behaviourSettingsArray.Length == 0
                || agentBehaviourIds == null
                || agentBehaviourIds.Length < length) {
                return;
            }

            for (int index = 0; index < length; index += 1) {
                float3 pos = positions[index];

                int behaviourIndex = agentBehaviourIds[index];
                if ((uint)behaviourIndex >= (uint)behaviourSettingsArray.Length) {
                    continue;
                }

                FlockBehaviourSettings settings = behaviourSettingsArray[behaviourIndex];

                float bodyRadius = settings.BodyRadius;
                float separationRadius = settings.SeparationRadius;
                float neighbourRadius = settings.NeighbourRadius;

                // Small center marker so you still see the fish itself
                Gizmos.color = new Color(1f, 1f, 1f, 0.75f);
                Gizmos.DrawSphere(pos, 0.05f);

                // Body radius (physical size)
                if (debugDrawBodyRadius && bodyRadius > 0f) {
                    Gizmos.color = new Color(0f, 1f, 1f, 0.6f);      // cyan
                    Gizmos.DrawWireSphere((Vector3)pos, bodyRadius);
                }

                // Separation radius (hard "back off" bubble)
                if (debugDrawSeparationRadius && separationRadius > 0f) {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.4f);      // red
                    Gizmos.DrawWireSphere((Vector3)pos, separationRadius);
                }

                // Logical neighbour radius (who this fish can see)
                if (debugDrawNeighbourRadiusSphere && neighbourRadius > 0f) {
                    Gizmos.color = new Color(1f, 1f, 0f, 0.25f);     // yellow
                    Gizmos.DrawWireSphere((Vector3)pos, neighbourRadius);
                }

                // NOTE: grid search radius sphere is drawn only in DrawNeighbourhoodGizmos
                // for the selected debugAgentIndex, to avoid insane clutter.
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
