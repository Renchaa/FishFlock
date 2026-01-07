using System.Collections.Generic;
using Flock.Runtime.Data;
using Flock.Runtime.Logging;
using Unity.Collections;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockSimulation {
    /**
     * <summary>
     * Core simulation runtime that owns agent/state arrays and grid structures.
     * </summary>
     */
    public sealed partial class FlockSimulation {
        public void SetAgentBehaviourIds(int[] behaviourIdsSource) {
            if (behaviourIdsSource == null) {
                return;
            }

            EnsurePendingBehaviourIdBuffer();

            int count = math.min(AgentCount, behaviourIdsSource.Length);
            CopyBehaviourIdsToPending(behaviourIdsSource, count);

            pendingBehaviourIdsCount = count;
            pendingBehaviourIdsDirty = true;
        }

        private void AllocateAgentArrays(Allocator allocator) {
            AllocateAgentSoaArrays(allocator);
            AllocateAgentAuxArrays(allocator);
            InitializeBehaviourIdsToZero();
        }

        private void AllocateBehaviourArrays(NativeArray<FlockBehaviourSettings> settings, Allocator allocator) {
            int behaviourCount = settings.Length;

            AllocateBehaviourNativeArrays(behaviourCount, allocator);

            float cellSize = math.max(environmentData.CellSize, 0.0001f);

            for (int index = 0; index < behaviourCount; index += 1) {
                FlockBehaviourSettings behaviour = ClampBehaviourSettings(settings[index]);
                behaviourSettings[index] = behaviour;

                float viewRadius = math.max(0f, behaviour.NeighbourRadius);
                behaviourCellSearchRadius[index] = ComputeCellRange(viewRadius, cellSize);
            }
        }

        private void AllocateGrid(Allocator allocator) {
            gridCellCount = environmentData.GridResolution.x
                            * environmentData.GridResolution.y
                            * environmentData.GridResolution.z;

            AllocateCellGroupNoise(allocator);

            if (gridCellCount > 0) {
                AllocateAgentGridStructures(allocator);
                AllocateAttractorGridStructures(allocator);
                return;
            }

            ResetAgentGridStructures();
            ResetAttractorGridStructures();
        }

        private void InitializeAgentsRandomInsideBounds() {
            float3 center = environmentData.BoundsCenter;
            float3 extents = environmentData.BoundsExtents;

            Unity.Mathematics.Random random = new Unity.Mathematics.Random(1234567u);
            float baseSpeed = DetermineInitialBaseSpeed();

            InitializeAgentPositionsAndVelocities(center, extents, ref random, baseSpeed);

            FlockLog.Info(
                logger,
                FlockLogCategory.Simulation,
                $"Initialized {AgentCount} agents with random positions and baseSpeed={baseSpeed}.",
                null);
        }

        private static FlockBehaviourSettings ClampBehaviourSettings(FlockBehaviourSettings behaviour) {
            behaviour.BodyRadius = math.max(0f, behaviour.BodyRadius);

            behaviour.SchoolingSpacingFactor = math.max(0.5f, behaviour.SchoolingSpacingFactor);
            behaviour.SchoolingOuterFactor = math.max(1f, behaviour.SchoolingOuterFactor);
            behaviour.SchoolingStrength = math.max(0f, behaviour.SchoolingStrength);
            behaviour.SchoolingInnerSoftness = math.clamp(behaviour.SchoolingInnerSoftness, 0f, 1f);
            behaviour.SchoolingDeadzoneFraction = math.clamp(behaviour.SchoolingDeadzoneFraction, 0f, 0.5f);
            behaviour.SchoolingRadialDamping = math.max(0f, behaviour.SchoolingRadialDamping);

            behaviour.BoundsWeight = math.max(0f, behaviour.BoundsWeight);
            behaviour.BoundsTangentialDamping = math.max(0f, behaviour.BoundsTangentialDamping);
            behaviour.BoundsInfluenceSuppression = math.max(0f, behaviour.BoundsInfluenceSuppression);

            behaviour.WanderStrength = math.max(0f, behaviour.WanderStrength);
            behaviour.WanderFrequency = math.max(0f, behaviour.WanderFrequency);

            behaviour.GroupNoiseStrength = math.max(0f, behaviour.GroupNoiseStrength);
            behaviour.GroupNoiseDirectionRate = math.max(0f, behaviour.GroupNoiseDirectionRate);
            behaviour.GroupNoiseSpeedWeight = math.max(0f, behaviour.GroupNoiseSpeedWeight);

            behaviour.PatternWeight = math.max(0f, behaviour.PatternWeight);
            behaviour.GroupFlowWeight = math.max(0f, behaviour.GroupFlowWeight);

            behaviour.UsePreferredDepth = behaviour.UsePreferredDepth != 0 ? (byte)1 : (byte)0;
            behaviour.DepthWinsOverAttractor = behaviour.DepthWinsOverAttractor != 0 ? (byte)1 : (byte)0;

            return behaviour;
        }

        private static int ComputeCellRange(float viewRadius, float cellSize) {
            int cellRange = (int)math.ceil(viewRadius / cellSize);
            if (cellRange < 1) {
                cellRange = 1;
            }

            return cellRange;
        }

        private void EnsurePendingBehaviourIdBuffer() {
            if (pendingBehaviourIdsManaged == null || pendingBehaviourIdsManaged.Length != AgentCount) {
                pendingBehaviourIdsManaged = new int[AgentCount];
            }
        }

        private void CopyBehaviourIdsToPending(int[] behaviourIdsSource, int count) {
            for (int index = 0; index < count; index += 1) {
                pendingBehaviourIdsManaged[index] = behaviourIdsSource[index];
            }
        }

        private void AllocateAgentSoaArrays(Allocator allocator) {
            positions = new NativeArray<float3>(AgentCount, allocator, NativeArrayOptions.UninitializedMemory);
            velocities = new NativeArray<float3>(AgentCount, allocator, NativeArrayOptions.UninitializedMemory);
            prevVelocities = new NativeArray<float3>(AgentCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourIds = new NativeArray<int>(AgentCount, allocator, NativeArrayOptions.UninitializedMemory);
        }

        private void AllocateAgentAuxArrays(Allocator allocator) {
            patternSteering = new NativeArray<float3>(AgentCount, allocator, NativeArrayOptions.ClearMemory);
            wallDirections = new NativeArray<float3>(AgentCount, allocator, NativeArrayOptions.ClearMemory);
            wallDangers = new NativeArray<float>(AgentCount, allocator, NativeArrayOptions.ClearMemory);
            neighbourAggregates = new NativeArray<NeighbourAggregate>(AgentCount, allocator, NativeArrayOptions.ClearMemory);
        }

        private void InitializeBehaviourIdsToZero() {
            for (int index = 0; index < AgentCount; index += 1) {
                behaviourIds[index] = 0;
            }
        }

        private void AllocateBehaviourNativeArrays(int behaviourCount, Allocator allocator) {
            behaviourSettings = new NativeArray<FlockBehaviourSettings>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourCellSearchRadius = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
        }

        private void AllocateCellGroupNoise(Allocator allocator) {
            if (gridCellCount > 0) {
                cellGroupNoise = new NativeArray<float3>(gridCellCount, allocator, NativeArrayOptions.ClearMemory);
                return;
            }

            cellGroupNoise = default;
        }

        private void AllocateAgentGridStructures(Allocator allocator) {
            cellAgentStarts = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
            cellAgentCounts = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.ClearMemory);
            InitializeCellAgentStartsToMinusOne();

            float cellSizeSafe = math.max(environmentData.CellSize, 0.0001f);
            maxCellsPerAgent = ComputeMaxCellsPerAgent(cellSizeSafe);

            int pairCapacity = math.max(AgentCount * maxCellsPerAgent, AgentCount);

            agentCellCounts = new NativeArray<int>(AgentCount, allocator, NativeArrayOptions.ClearMemory);
            agentCellIds = new NativeArray<int>(AgentCount * maxCellsPerAgent, allocator, NativeArrayOptions.UninitializedMemory);
            agentEntryStarts = new NativeArray<int>(AgentCount, allocator, NativeArrayOptions.UninitializedMemory);

            cellAgentPairs = new NativeArray<Flock.Runtime.Jobs.CellAgentPair>(pairCapacity, allocator, NativeArrayOptions.UninitializedMemory);

            totalAgentPairCount = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);

            int touchedCap = math.min(gridCellCount, pairCapacity);
            touchedAgentCells = new NativeArray<int>(touchedCap, allocator, NativeArrayOptions.UninitializedMemory);
            touchedAgentCellCount = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
        }

        private void ResetAgentGridStructures() {
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

        private void AllocateAttractorGridStructures(Allocator allocator) {
            cellToIndividualAttractor = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
            cellToGroupAttractor = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
            cellIndividualPriority = new NativeArray<float>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
            cellGroupPriority = new NativeArray<float>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);

            InitializeAttractorGridDefaults();
        }

        private void ResetAttractorGridStructures() {
            cellToIndividualAttractor = default;
            cellToGroupAttractor = default;
            cellIndividualPriority = default;
            cellGroupPriority = default;
        }

        private void InitializeCellAgentStartsToMinusOne() {
            for (int index = 0; index < gridCellCount; index += 1) {
                cellAgentStarts[index] = -1;
            }
        }

        private int ComputeMaxCellsPerAgent(float cellSizeSafe) {
            float maxBodyRadius = GetMaxBodyRadiusFromBehaviours();

            if (maxBodyRadius <= 0f) {
                return 1;
            }

            int span = (int)math.ceil((2f * maxBodyRadius) / cellSizeSafe) + 2;
            span = math.max(span, 1);
            return span * span * span;
        }

        private float GetMaxBodyRadiusFromBehaviours() {
            float maxBodyRadius = 0f;

            if (!behaviourSettings.IsCreated || behaviourSettings.Length <= 0) {
                return maxBodyRadius;
            }

            for (int index = 0; index < behaviourSettings.Length; index += 1) {
                maxBodyRadius = math.max(maxBodyRadius, behaviourSettings[index].BodyRadius);
            }

            return maxBodyRadius;
        }

        private void InitializeAttractorGridDefaults() {
            for (int index = 0; index < gridCellCount; index += 1) {
                cellToIndividualAttractor[index] = -1;
                cellToGroupAttractor[index] = -1;
                cellIndividualPriority[index] = float.NegativeInfinity;
                cellGroupPriority[index] = float.NegativeInfinity;
            }
        }

        private float DetermineInitialBaseSpeed() {
            float baseSpeed = 1.0f;

            if (behaviourSettings.IsCreated && behaviourSettings.Length > 0) {
                baseSpeed = math.max(0.1f, behaviourSettings[0].MaxSpeed * 0.5f);
            }

            return baseSpeed;
        }

        private void InitializeAgentPositionsAndVelocities(float3 center, float3 extents, ref Unity.Mathematics.Random random, float baseSpeed) {
            for (int index = 0; index < AgentCount; index += 1) {
                float3 randomOffset = random.NextFloat3(-extents, extents);
                positions[index] = center + randomOffset;

                float3 direction = math.normalize(random.NextFloat3(-1.0f, 1.0f));
                if (math.lengthsq(direction) < 1e-4f) {
                    direction = new float3(0.0f, 0.0f, 1.0f);
                }

                float3 velocity = direction * baseSpeed;
                velocities[index] = velocity;

                if (prevVelocities.IsCreated) {
                    prevVelocities[index] = velocity;
                }
            }
        }
    }
}
