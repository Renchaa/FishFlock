namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using Unity.Collections;
    using Unity.Mathematics;

    public sealed partial class FlockSimulation {
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

        void AllocateBehaviourArrays(NativeArray<FlockBehaviourSettings> settings, Allocator allocator) {
            int behaviourCount = settings.Length;

            behaviourSettings = new NativeArray<FlockBehaviourSettings>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            behaviourCellSearchRadius = new NativeArray<int>(
                behaviourCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            float cellSize = math.max(environmentData.CellSize, 0.0001f);

            for (int index = 0; index < behaviourCount; index += 1) {
                // Copy authored snapshot
                FlockBehaviourSettings b = settings[index];

                // Preserve the exact clamps you were applying via the SoA arrays
                b.BodyRadius = math.max(0f, b.BodyRadius);

                b.SchoolingSpacingFactor = math.max(0.5f, b.SchoolingSpacingFactor);
                b.SchoolingOuterFactor = math.max(1f, b.SchoolingOuterFactor);
                b.SchoolingStrength = math.max(0f, b.SchoolingStrength);
                b.SchoolingInnerSoftness = math.clamp(b.SchoolingInnerSoftness, 0f, 1f);
                b.SchoolingDeadzoneFraction = math.clamp(b.SchoolingDeadzoneFraction, 0f, 0.5f);
                b.SchoolingRadialDamping = math.max(0f, b.SchoolingRadialDamping);

                b.BoundsWeight = math.max(0f, b.BoundsWeight);
                b.BoundsTangentialDamping = math.max(0f, b.BoundsTangentialDamping);
                b.BoundsInfluenceSuppression = math.max(0f, b.BoundsInfluenceSuppression);

                b.WanderStrength = math.max(0f, b.WanderStrength);
                b.WanderFrequency = math.max(0f, b.WanderFrequency);

                b.GroupNoiseStrength = math.max(0f, b.GroupNoiseStrength);
                b.GroupNoiseDirectionRate = math.max(0f, b.GroupNoiseDirectionRate);
                b.GroupNoiseSpeedWeight = math.max(0f, b.GroupNoiseSpeedWeight);

                b.PatternWeight = math.max(0f, b.PatternWeight);

                b.GroupFlowWeight = math.max(0f, b.GroupFlowWeight);

                b.UsePreferredDepth = b.UsePreferredDepth != 0 ? (byte)1 : (byte)0;
                b.DepthWinsOverAttractor = b.DepthWinsOverAttractor != 0 ? (byte)1 : (byte)0;

                // Store
                behaviourSettings[index] = b;

                // Derived runtime param (avoid recomputing per-frame)
                float viewRadius = math.max(0f, b.NeighbourRadius);

                int cellRange = (int)math.ceil(viewRadius / cellSize);
                if (cellRange < 1) {
                    cellRange = 1;
                }

                behaviourCellSearchRadius[index] = cellRange;
            }
        }

        void AllocateGrid(Allocator allocator) {
            gridCellCount = environmentData.GridResolution.x
                            * environmentData.GridResolution.y
                            * environmentData.GridResolution.z;

            if (gridCellCount > 0) {
                cellGroupNoise = new NativeArray<float3>(
                    gridCellCount,
                    allocator,
                    NativeArrayOptions.ClearMemory);
            } else {
                cellGroupNoise = default;
            }

            if (gridCellCount > 0) {
                cellAgentStarts = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
                cellAgentCounts = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.ClearMemory);

                for (int i = 0; i < gridCellCount; i += 1) {
                    cellAgentStarts[i] = -1;
                }

                float cellSizeSafe = math.max(environmentData.CellSize, 0.0001f);

                float maxBodyRadius = 0f;
                if (behaviourSettings.IsCreated && behaviourSettings.Length > 0) {
                    for (int i = 0; i < behaviourSettings.Length; i += 1) {
                        maxBodyRadius = math.max(maxBodyRadius, behaviourSettings[i].BodyRadius);
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

            cellToIndividualAttractor = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
            cellToGroupAttractor = new NativeArray<int>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
            cellIndividualPriority = new NativeArray<float>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);
            cellGroupPriority = new NativeArray<float>(gridCellCount, allocator, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < gridCellCount; i += 1) {
                cellToIndividualAttractor[i] = -1;
                cellToGroupAttractor[i] = -1;
                cellIndividualPriority[i] = float.NegativeInfinity;
                cellGroupPriority[i] = float.NegativeInfinity;
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
            if (behaviourSettings.IsCreated && behaviourSettings.Length > 0) {
                baseSpeed = math.max(0.1f, behaviourSettings[0].MaxSpeed * 0.5f);
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
    }
}
