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

            behaviourAvoidanceWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourNeutralWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourAttractionWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourAvoidResponse = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourAvoidMask = new NativeArray<uint>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourNeutralMask = new NativeArray<uint>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourSplitPanicThreshold = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSplitLateralWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourSplitAccelBoost = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourMinGroupSize = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMaxGroupSize = new NativeArray<int>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupRadiusMultiplier = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourLonerRadiusMultiplier = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourLonerCohesionBoost = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMinGroupSizeWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourMaxGroupSizeWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

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
            behaviourSchoolRadialDamping = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourWanderStrength = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourWanderFrequency = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupNoiseStrength = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourPatternWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

            behaviourGroupNoiseDirectionRate = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);
            behaviourGroupNoiseSpeedWeight = new NativeArray<float>(behaviourCount, allocator, NativeArrayOptions.UninitializedMemory);

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

                behaviourSchoolSpacingFactor[index] = math.max(0.5f, behaviour.SchoolingSpacingFactor);
                behaviourSchoolOuterFactor[index] = math.max(1f, behaviour.SchoolingOuterFactor);
                behaviourSchoolStrength[index] = math.max(0f, behaviour.SchoolingStrength);
                behaviourSchoolInnerSoftness[index] = math.clamp(behaviour.SchoolingInnerSoftness, 0f, 1f);
                behaviourSchoolDeadzoneFraction[index] = math.clamp(behaviour.SchoolingDeadzoneFraction, 0f, 0.5f);
                behaviourSchoolRadialDamping[index] = math.max(0f, behaviour.SchoolingRadialDamping);

                behaviourBoundsWeight[index] = math.max(0f, behaviour.BoundsWeight);
                behaviourBoundsTangentialDamping[index] = math.max(0f, behaviour.BoundsTangentialDamping);
                behaviourBoundsInfluenceSuppression[index] = math.max(0f, behaviour.BoundsInfluenceSuppression);

                behaviourWanderStrength[index] = math.max(0f, behaviour.WanderStrength);
                behaviourWanderFrequency[index] = math.max(0f, behaviour.WanderFrequency);

                behaviourGroupNoiseStrength[index] = math.max(0f, behaviour.GroupNoiseStrength);
                behaviourGroupNoiseDirectionRate[index] = math.max(0f, behaviour.GroupNoiseDirectionRate);
                behaviourGroupNoiseSpeedWeight[index] = math.max(0f, behaviour.GroupNoiseSpeedWeight);

                behaviourPatternWeight[index] = math.max(0f, behaviour.PatternWeight);

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

                behaviourSplitPanicThreshold[index] = behaviour.SplitPanicThreshold;
                behaviourSplitLateralWeight[index] = behaviour.SplitLateralWeight;
                behaviourSplitAccelBoost[index] = behaviour.SplitAccelBoost;

                behaviourMinGroupSize[index] = behaviour.MinGroupSize;
                behaviourMaxGroupSize[index] = behaviour.MaxGroupSize;
                behaviourGroupRadiusMultiplier[index] = behaviour.GroupRadiusMultiplier;
                behaviourLonerRadiusMultiplier[index] = behaviour.LonerRadiusMultiplier;
                behaviourLonerCohesionBoost[index] = behaviour.LonerCohesionBoost;
                behaviourMinGroupSizeWeight[index] = behaviour.MinGroupSizeWeight;
                behaviourMaxGroupSizeWeight[index] = behaviour.MaxGroupSizeWeight;

                behaviourUsePreferredDepth[index] = behaviour.UsePreferredDepth != 0 ? (byte)1 : (byte)0;
                behaviourDepthBiasStrength[index] = behaviour.DepthBiasStrength;
                behaviourDepthWinsOverAttractor[index] = behaviour.DepthWinsOverAttractor != 0 ? (byte)1 : (byte)0;

                behaviourPreferredDepthMinNorm[index] = behaviour.PreferredDepthMinNorm;
                behaviourPreferredDepthMaxNorm[index] = behaviour.PreferredDepthMaxNorm;
                behaviourPreferredDepthWeight[index] = behaviour.PreferredDepthWeight;
                behaviourPreferredDepthEdgeFraction[index] = behaviour.PreferredDepthEdgeFraction;

                behaviourMaxNeighbourChecks[index] = behaviour.MaxNeighbourChecks;
                behaviourMaxFriendlySamples[index] = behaviour.MaxFriendlySamples;
                behaviourMaxSeparationSamples[index] = behaviour.MaxSeparationSamples;
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
    }
}
