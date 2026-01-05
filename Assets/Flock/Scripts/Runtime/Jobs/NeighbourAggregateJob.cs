// File: Assets/Flock/Runtime/Jobs/NeighbourAggregateJob.cs

using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Runtime.Jobs {
    /**
    * <summary>
    * Produces stable per-agent aggregate buffers.
    * No steering integration happens here.
    * </summary>
    */
    [BurstCompile]
    public struct NeighbourAggregateJob : IJobParallelFor {
        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<float3> PrevVelocities;

        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public NativeArray<FlockBehaviourSettings> BehaviourSettings;

        [ReadOnly]
        public NativeArray<int> BehaviourCellSearchRadius;

        [ReadOnly]
        public NativeArray<int> CellAgentStarts;

        [ReadOnly]
        public NativeArray<int> CellAgentCounts;

        [ReadOnly]
        public NativeArray<CellAgentPair> CellAgentPairs;

        [ReadOnly]
        public float3 GridOrigin;

        [ReadOnly]
        public int3 GridResolution;

        [ReadOnly]
        public float CellSize;

        [NativeDisableParallelForRestriction]
        public NativeArray<NeighbourAggregate> OutAggregates;


        /**
        * <summary>
        * Executes neighbour scanning for a single agent index and writes an aggregate record to <see cref="OutAggregates"/>.
        * </summary>
        * <param name="agentIndex">The agent index to process.</param>
        */
        public void Execute(int agentIndex) {
            if (!TryGetBehaviourSettings(agentIndex, out int behaviourIndex, out FlockBehaviourSettings behaviourSettings)) {
                OutAggregates[agentIndex] = default;
                return;
            }

            NeighbourAggregateAccumulator accumulator = default;
            NeighbourDeduplication deduplication = default;

            AccumulateNeighbourForces(agentIndex, behaviourIndex, behaviourSettings, ref accumulator, ref deduplication);

            OutAggregates[agentIndex] = accumulator.ToAggregate();
        }

        private static uint ComputeHash32(uint value) {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        private bool TryGetBehaviourSettings(int agentIndex, out int behaviourIndex, out FlockBehaviourSettings behaviourSettings) {
            behaviourIndex = BehaviourIds[agentIndex];

            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                behaviourSettings = default;
                return false;
            }

            behaviourSettings = BehaviourSettings[behaviourIndex];
            return true;
        }

        private void AccumulateNeighbourForces(
            int agentIndex,
            int agentBehaviourIndex,
            FlockBehaviourSettings agentBehaviourSettings,
            ref NeighbourAggregateAccumulator accumulator,
            ref NeighbourDeduplication deduplication) {
            NeighbourScanState scanState = CreateScanState(agentIndex, agentBehaviourIndex, agentBehaviourSettings);
            ScanNeighbourCells(ref scanState, ref accumulator, ref deduplication);
        }

        private NeighbourScanState CreateScanState(int agentIndex, int agentBehaviourIndex, FlockBehaviourSettings agentBehaviourSettings) {
            float neighbourRadius = agentBehaviourSettings.NeighbourRadius;
            float separationRadius = agentBehaviourSettings.SeparationRadius;

            NeighbourScanState scanState = default;

            scanState.AgentIndex = agentIndex;
            scanState.AgentBehaviourIndex = agentBehaviourIndex;
            scanState.AgentBehaviourSettings = agentBehaviourSettings;

            scanState.AgentPosition = Positions[agentIndex];
            scanState.NeighbourRadius = neighbourRadius;
            scanState.NeighbourRadiusSquared = neighbourRadius * neighbourRadius;
            scanState.SeparationRadiusSquared = separationRadius * separationRadius;

            scanState.FriendlyMask = agentBehaviourSettings.GroupMask;
            scanState.AvoidMask = agentBehaviourSettings.AvoidMask;
            scanState.NeutralMask = agentBehaviourSettings.NeutralMask;

            scanState.AvoidanceWeight = agentBehaviourSettings.AvoidanceWeight;
            scanState.NeutralWeight = agentBehaviourSettings.NeutralWeight;
            scanState.AvoidResponse = math.max(0f, agentBehaviourSettings.AvoidResponse);

            scanState.SchoolingRadialDamping = agentBehaviourSettings.SchoolingRadialDamping;
            scanState.SelfPreviousVelocity = PrevVelocities[agentIndex];

            scanState.BaseCell = GetCell(scanState.AgentPosition);
            scanState.CellRange = GetCellRange(agentBehaviourIndex);

            scanState.MaximumNeighbourChecks = math.max(0, agentBehaviourSettings.MaxNeighbourChecks);
            scanState.MaximumFriendlySamples = math.max(0, agentBehaviourSettings.MaxFriendlySamples);
            scanState.MaximumSeparationSamples = math.max(0, agentBehaviourSettings.MaxSeparationSamples);

            scanState.MaximumLeaderWeight = -1f;
            scanState.LeaderTieSamples = 0;
            scanState.NeighbourChecks = 0;

            scanState.ShouldStopAll = false;

            return scanState;
        }

        private int GetCellRange(int agentBehaviourIndex) {
            if (BehaviourCellSearchRadius.IsCreated && (uint)agentBehaviourIndex < (uint)BehaviourCellSearchRadius.Length) {
                return math.max(BehaviourCellSearchRadius[agentBehaviourIndex], 1);
            }

            return 1;
        }

        private void ScanNeighbourCells(
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator,
            ref NeighbourDeduplication deduplication) {
            for (int xIndex = -scanState.CellRange; xIndex <= scanState.CellRange && !scanState.ShouldStopAll; xIndex++)
                for (int yIndex = -scanState.CellRange; yIndex <= scanState.CellRange && !scanState.ShouldStopAll; yIndex++)
                    for (int zIndex = -scanState.CellRange; zIndex <= scanState.CellRange && !scanState.ShouldStopAll; zIndex++)
                        ScanNeighbourCell(new int3(xIndex, yIndex, zIndex), ref scanState, ref accumulator, ref deduplication);
        }

        private void ScanNeighbourCell(
            int3 cellOffset,
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator,
            ref NeighbourDeduplication deduplication) {
            int3 neighbourCell = scanState.BaseCell + cellOffset;
            if (!IsCellInsideGrid(neighbourCell)) {
                return;
            }

            int cellId = GetCellId(neighbourCell);
            int countInCell = CellAgentCounts[cellId];

            if (countInCell <= 0) {
                return;
            }

            int startInCell = CellAgentStarts[cellId];

            for (int pairIndex = 0; pairIndex < countInCell && !scanState.ShouldStopAll; pairIndex++) {
                int neighbourIndex = CellAgentPairs[startInCell + pairIndex].AgentIndex;
                TryAccumulateNeighbour(neighbourIndex, ref scanState, ref accumulator, ref deduplication);
            }
        }

        private void TryAccumulateNeighbour(
            int neighbourIndex,
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator,
            ref NeighbourDeduplication deduplication) {
            if (neighbourIndex == scanState.AgentIndex) {
                return;
            }

            if (deduplication.IsVisitedOrMark(neighbourIndex)) {
                return;
            }

            if (scanState.MaximumNeighbourChecks > 0 && scanState.NeighbourChecks >= scanState.MaximumNeighbourChecks) {
                scanState.ShouldStopAll = true;
                return;
            }

            scanState.NeighbourChecks += 1;

            float3 neighbourPosition = Positions[neighbourIndex];
            float3 offset = neighbourPosition - scanState.AgentPosition;

            float distanceSquared = math.lengthsq(offset);
            if (distanceSquared < 1e-6f) {
                return;
            }

            int neighbourBehaviourIndex = BehaviourIds[neighbourIndex];
            if ((uint)neighbourBehaviourIndex >= (uint)BehaviourSettings.Length) {
                return;
            }

            FlockBehaviourSettings neighbourBehaviourSettings = BehaviourSettings[neighbourBehaviourIndex];

            float hardRadiusSquared = ComputeHardRadiusSquared(scanState, neighbourBehaviourSettings);
            bool withinHardRadius = hardRadiusSquared > 0f && distanceSquared < hardRadiusSquared;
            bool withinNeighbourRadius = distanceSquared <= scanState.NeighbourRadiusSquared;

            if (!withinHardRadius && !withinNeighbourRadius) {
                return;
            }

            float inverseDistance = math.rsqrt(distanceSquared);
            float distance = distanceSquared * inverseDistance;
            float3 directionUnit = offset * inverseDistance;

            if (withinHardRadius) {
                ApplyHardSeparation(ref accumulator, scanState.MaximumSeparationSamples, hardRadiusSquared, distance, directionUnit);
            }

            if (!withinNeighbourRadius || scanState.NeighbourRadius <= 1e-6f) {
                return;
            }

            uint behaviourBit = neighbourBehaviourIndex < 32 ? (1u << neighbourBehaviourIndex) : 0u;

            bool isFriendly = behaviourBit != 0u && (scanState.FriendlyMask & behaviourBit) != 0u;
            bool isAvoid = behaviourBit != 0u && (scanState.AvoidMask & behaviourBit) != 0u;
            bool isNeutral = behaviourBit != 0u && (scanState.NeutralMask & behaviourBit) != 0u;

            if (!isFriendly && !isAvoid && !isNeutral) {
                return;
            }

            float inverseNeighbourRadius = 1f / scanState.NeighbourRadius;
            float proximityWeight = 1f - math.saturate(distance * inverseNeighbourRadius);

            if (isFriendly) {
                AccumulateFriendlyNeighbour(
                    neighbourIndex,
                    neighbourBehaviourIndex,
                    neighbourPosition,
                    neighbourBehaviourSettings,
                    distance,
                    directionUnit,
                    proximityWeight,
                    ref scanState,
                    ref accumulator);
            }

            if (isAvoid) {
                AccumulateAvoidNeighbour(neighbourBehaviourSettings, directionUnit, proximityWeight, ref scanState, ref accumulator);
            }

            if (isNeutral) {
                AccumulateNeutralNeighbour(neighbourBehaviourSettings, directionUnit, proximityWeight, ref scanState, ref accumulator);
            }
        }

        private static float ComputeHardRadiusSquared(NeighbourScanState scanState, FlockBehaviourSettings neighbourBehaviourSettings) {
            float hardRadiusSquared = scanState.SeparationRadiusSquared;

            float collisionDistanceBody = scanState.AgentBehaviourSettings.BodyRadius + neighbourBehaviourSettings.BodyRadius;
            if (collisionDistanceBody > 0f) {
                float collisionDistanceSquared = collisionDistanceBody * collisionDistanceBody;
                if (collisionDistanceSquared > hardRadiusSquared) {
                    hardRadiusSquared = collisionDistanceSquared;
                }
            }

            return hardRadiusSquared;
        }

        private static void ApplyHardSeparation(
            ref NeighbourAggregateAccumulator accumulator,
            int maximumSeparationSamples,
            float hardRadiusSquared,
            float distance,
            float3 directionUnit) {
            bool canAddSeparation = maximumSeparationSamples == 0 || accumulator.SeparationCount < maximumSeparationSamples;
            if (!canAddSeparation) {
                return;
            }

            float hardInverse = math.rsqrt(math.max(hardRadiusSquared, 1e-12f));
            float hardRadius = hardRadiusSquared * hardInverse;

            float penetration = hardRadius - distance;
            float penetrationStrength = penetration / math.max(hardRadius, 1e-3f);

            accumulator.SeparationSum -= directionUnit * (1f + penetrationStrength);
            accumulator.SeparationCount += 1;
        }

        private void AccumulateFriendlyNeighbour(
            int neighbourIndex,
            int neighbourBehaviourIndex,
            float3 neighbourPosition,
            FlockBehaviourSettings neighbourBehaviourSettings,
            float distance,
            float3 directionUnit,
            float proximityWeight,
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator) {
            accumulator.FriendlyNeighbourCount += 1;

            bool considerLeadership = ShouldConsiderLeadership(neighbourBehaviourSettings.LeadershipWeight, ref scanState);
            bool canAddSeparation = scanState.MaximumSeparationSamples == 0 || accumulator.SeparationCount < scanState.MaximumSeparationSamples;

            bool requiresBandEvaluation = scanState.SchoolingRadialDamping > 0f || considerLeadership || canAddSeparation;

            SchoolingBandDistances bandDistances = default;
            float3 bandForce = requiresBandEvaluation
                ? ComputeSchoolingBandForce(scanState.AgentBehaviourIndex, neighbourBehaviourIndex, distance, directionUnit, out bandDistances)
                : float3.zero;

            if (math.lengthsq(bandForce) > 0f && canAddSeparation) {
                accumulator.SeparationSum += bandForce;
                accumulator.SeparationCount += 1;
            }

            bool isFarForCohesion = IsFarForCohesion(distance, bandDistances);
            AccumulateRadialDamping(neighbourIndex, distance, directionUnit, bandDistances, ref scanState, ref accumulator);

            if (considerLeadership) {
                ApplyLeadershipContribution(neighbourIndex, neighbourPosition, neighbourBehaviourSettings.LeadershipWeight, proximityWeight, isFarForCohesion, ref scanState, ref accumulator);
            }
        }

        private static bool ShouldConsiderLeadership(float neighbourLeaderWeight, ref NeighbourScanState scanState) {
            const float leadershipEpsilon = 1e-4f;

            bool isLeaderUpgrade = neighbourLeaderWeight > scanState.MaximumLeaderWeight + leadershipEpsilon;

            bool isLeaderTie =
                math.abs(neighbourLeaderWeight - scanState.MaximumLeaderWeight) <= leadershipEpsilon
                && scanState.MaximumLeaderWeight > -1f;

            bool canTakeTieSample = isLeaderTie && (scanState.MaximumFriendlySamples == 0 || scanState.LeaderTieSamples < scanState.MaximumFriendlySamples);

            scanState.IsLeaderUpgrade = isLeaderUpgrade;
            scanState.CanTakeTieSample = canTakeTieSample;

            return isLeaderUpgrade || canTakeTieSample;
        }

        private static bool IsFarForCohesion(float distance, SchoolingBandDistances bandDistances) {
            bool haveBand =
                bandDistances.FarDistance > 0f &&
                bandDistances.CollisionDistance > 0f &&
                bandDistances.TargetDistance > bandDistances.CollisionDistance;

            if (!haveBand || distance >= bandDistances.FarDistance) {
                return false;
            }

            float threshold = bandDistances.DeadZoneUpper > 0f ? bandDistances.DeadZoneUpper : bandDistances.TargetDistance;
            return distance > threshold;
        }

        private void AccumulateRadialDamping(
            int neighbourIndex,
            float distance,
            float3 directionUnit,
            SchoolingBandDistances bandDistances,
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator) {
            if (scanState.SchoolingRadialDamping <= 0f) {
                return;
            }

            if (bandDistances.TargetDistance <= bandDistances.CollisionDistance || distance >= bandDistances.TargetDistance) {
                return;
            }

            float3 neighbourPreviousVelocity = PrevVelocities[neighbourIndex];
            float relativeSpeed = math.dot(scanState.SelfPreviousVelocity - neighbourPreviousVelocity, directionUnit);

            if (relativeSpeed <= 0f) {
                return;
            }

            float innerSpan = math.max(bandDistances.TargetDistance - bandDistances.CollisionDistance, 1e-3f);
            float proximity = math.saturate((bandDistances.TargetDistance - distance) / innerSpan);

            float dampingStrength = scanState.SchoolingRadialDamping * proximity;
            float damping = relativeSpeed * dampingStrength;

            accumulator.RadialDampingSum -= directionUnit * damping;
        }

        private void ApplyLeadershipContribution(
            int neighbourIndex,
            float3 neighbourPosition,
            float neighbourLeaderWeight,
            float proximityWeight,
            bool isFarForCohesion,
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator) {
            float3 neighbourVelocity = PrevVelocities[neighbourIndex];

            if (scanState.IsLeaderUpgrade) {
                scanState.MaximumLeaderWeight = neighbourLeaderWeight;
                accumulator.LeaderNeighbourCount = 1;

                accumulator.AlignmentSum = neighbourVelocity * proximityWeight;
                accumulator.AlignmentWeightSum = proximityWeight;

                if (isFarForCohesion) {
                    accumulator.CohesionSum = neighbourPosition * proximityWeight;
                    accumulator.CohesionWeightSum = proximityWeight;
                } else {
                    accumulator.CohesionSum = float3.zero;
                    accumulator.CohesionWeightSum = 0f;
                }

                scanState.LeaderTieSamples = 1;
                return;
            }

            if (!scanState.CanTakeTieSample) {
                return;
            }

            accumulator.LeaderNeighbourCount += 1;

            accumulator.AlignmentSum += neighbourVelocity * proximityWeight;
            accumulator.AlignmentWeightSum += proximityWeight;

            if (isFarForCohesion) {
                accumulator.CohesionSum += neighbourPosition * proximityWeight;
                accumulator.CohesionWeightSum += proximityWeight;
            }

            scanState.LeaderTieSamples += 1;
        }

        private static void AccumulateAvoidNeighbour(
            FlockBehaviourSettings neighbourBehaviourSettings,
            float3 directionUnit,
            float proximityWeight,
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator) {
            if (scanState.AvoidResponse <= 0f) {
                return;
            }

            float neighbourAvoidWeight = neighbourBehaviourSettings.AvoidanceWeight;
            if (scanState.AvoidanceWeight >= neighbourAvoidWeight) {
                return;
            }

            float weightDelta = neighbourAvoidWeight - scanState.AvoidanceWeight;
            float normalised = weightDelta / math.max(neighbourAvoidWeight, 1e-3f);

            float localIntensity = proximityWeight * normalised * scanState.AvoidResponse;

            if (localIntensity > accumulator.AvoidDanger) {
                accumulator.AvoidDanger = localIntensity;
            }

            bool canAddSeparation = scanState.MaximumSeparationSamples == 0 || accumulator.SeparationCount < scanState.MaximumSeparationSamples;
            if (!canAddSeparation) {
                return;
            }

            float3 repulse = -directionUnit * localIntensity;

            accumulator.SeparationSum += repulse;
            accumulator.AvoidSeparationSum += repulse;
            accumulator.SeparationCount += 1;
        }

        private static void AccumulateNeutralNeighbour(
            FlockBehaviourSettings neighbourBehaviourSettings,
            float3 directionUnit,
            float proximityWeight,
            ref NeighbourScanState scanState,
            ref NeighbourAggregateAccumulator accumulator) {
            float neighbourNeutralWeight = neighbourBehaviourSettings.NeutralWeight;
            if (scanState.NeutralWeight >= neighbourNeutralWeight) {
                return;
            }

            float weightDelta = neighbourNeutralWeight - scanState.NeutralWeight;
            float normalised = weightDelta / math.max(neighbourNeutralWeight, 1e-3f);

            bool canAddSeparation = scanState.MaximumSeparationSamples == 0 || accumulator.SeparationCount < scanState.MaximumSeparationSamples;
            if (!canAddSeparation) {
                return;
            }

            float3 softRepulse = -directionUnit * (proximityWeight * normalised * 0.5f);

            accumulator.SeparationSum += softRepulse;
            accumulator.SeparationCount += 1;
        }

        private float3 ComputeSchoolingBandForce(
            int agentBehaviourIndex,
            int neighbourBehaviourIndex,
            float distance,
            float3 directionUnit,
            out SchoolingBandDistances bandDistances) {
            bandDistances = default;

            if ((uint)agentBehaviourIndex >= (uint)BehaviourSettings.Length || (uint)neighbourBehaviourIndex >= (uint)BehaviourSettings.Length) {
                return float3.zero;
            }

            FlockBehaviourSettings agentSettings = BehaviourSettings[agentBehaviourIndex];
            FlockBehaviourSettings neighbourSettings = BehaviourSettings[neighbourBehaviourIndex];

            float collisionDistance = agentSettings.BodyRadius + neighbourSettings.BodyRadius;
            if (agentSettings.BodyRadius <= 0f && neighbourSettings.BodyRadius <= 0f) {
                return float3.zero;
            }

            if (!TryComputeSchoolingBandParameters(agentSettings, neighbourSettings, collisionDistance, out SchoolingBandParameters bandParameters, out bandDistances)) {
                return float3.zero;
            }

            if (distance <= 0f || distance >= bandDistances.FarDistance) {
                return float3.zero;
            }

            float force = ComputeSchoolingBandForceScalar(distance, bandParameters, ref bandDistances);
            if (math.abs(force) <= 1e-5f) {
                return float3.zero;
            }

            return -directionUnit * (force * bandParameters.Strength);
        }

        private static bool TryComputeSchoolingBandParameters(
            FlockBehaviourSettings agentSettings,
            FlockBehaviourSettings neighbourSettings,
            float collisionDistance,
            out SchoolingBandParameters bandParameters,
            out SchoolingBandDistances bandDistances) {
            bandParameters = default;
            bandDistances = default;

            float spacing = math.max(0.5f, 0.5f * (agentSettings.SchoolingSpacingFactor + neighbourSettings.SchoolingSpacingFactor));
            float outer = math.max(1f, 0.5f * (agentSettings.SchoolingOuterFactor + neighbourSettings.SchoolingOuterFactor));
            float strength = math.max(0f, 0.5f * (agentSettings.SchoolingStrength + neighbourSettings.SchoolingStrength));

            if (strength <= 0f || collisionDistance <= 0f) {
                return false;
            }

            float softness = math.clamp(0.5f * (agentSettings.SchoolingInnerSoftness + neighbourSettings.SchoolingInnerSoftness), 0f, 1f);
            float deadZoneFraction = math.clamp(0.5f * (agentSettings.SchoolingDeadzoneFraction + neighbourSettings.SchoolingDeadzoneFraction), 0f, 0.5f);

            float targetDistance = collisionDistance * spacing;
            float farDistance = targetDistance * outer;

            bandParameters = new SchoolingBandParameters(strength, softness, deadZoneFraction);
            bandDistances = new SchoolingBandDistances(collisionDistance, targetDistance, targetDistance + deadZoneFraction * targetDistance, farDistance);

            return true;
        }

        private static float ComputeSchoolingBandForceScalar(float distance, SchoolingBandParameters bandParameters, ref SchoolingBandDistances bandDistances) {
            float collisionDistance = bandDistances.CollisionDistance;
            float targetDistance = bandDistances.TargetDistance;

            if (distance < collisionDistance) {
                float t = (collisionDistance - distance) / math.max(collisionDistance, 1e-3f);
                return t;
            }

            if (distance < targetDistance) {
                return ComputeInnerRepulsion(distance, collisionDistance, targetDistance, bandParameters.Softness);
            }

            if (distance >= targetDistance && distance <= bandDistances.DeadZoneUpper) {
                return 0f;
            }

            float attractStart = bandDistances.DeadZoneUpper;
            float attractSpan = math.max(bandDistances.FarDistance - attractStart, 1e-3f);

            float tAttract = (distance - attractStart) / attractSpan;
            float falloff = 1f - tAttract;

            return -falloff;
        }

        private static float ComputeInnerRepulsion(float distance, float collisionDistance, float targetDistance, float softness) {
            float innerSpan = math.max(targetDistance - collisionDistance, 1e-3f);
            float t = (targetDistance - distance) / innerSpan;

            float t2 = t * t;

            if (softness <= 0f) {
                return t;
            }

            float t3 = t2 * t;

            if (softness <= 0.5f) {
                float u = softness * 2f;
                return math.lerp(t, t2, u);
            }

            float v = (softness - 0.5f) * 2f;
            return math.lerp(t2, t3, v);
        }

        private int3 GetCell(float3 position) {
            float safeCellSize = math.max(CellSize, 0.0001f);
            float3 localPosition = position - GridOrigin;
            float3 scaledPosition = localPosition / safeCellSize;

            int3 cell = (int3)math.floor(scaledPosition);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                GridResolution - new int3(1, 1, 1));
        }

        private bool IsCellInsideGrid(int3 cell) {
            if (cell.x < 0 || cell.y < 0 || cell.z < 0) {
                return false;
            }

            if (cell.x >= GridResolution.x || cell.y >= GridResolution.y || cell.z >= GridResolution.z) {
                return false;
            }

            return true;
        }

        private int GetCellId(int3 cell) {
            return cell.x + cell.y * GridResolution.x + cell.z * GridResolution.x * GridResolution.y;
        }

        private struct NeighbourAggregateAccumulator {
            public float3 AlignmentSum;
            public float3 CohesionSum;
            public float3 SeparationSum;
            public float3 AvoidSeparationSum;
            public float3 RadialDampingSum;

            public int LeaderNeighbourCount;
            public int SeparationCount;
            public int FriendlyNeighbourCount;

            public float AlignmentWeightSum;
            public float CohesionWeightSum;
            public float AvoidDanger;

            public NeighbourAggregate ToAggregate() {
                NeighbourAggregate neighbourAggregate = default;

                neighbourAggregate.AlignmentSum = AlignmentSum;
                neighbourAggregate.CohesionSum = CohesionSum;
                neighbourAggregate.SeparationSum = SeparationSum;
                neighbourAggregate.AvoidSeparationSum = AvoidSeparationSum;
                neighbourAggregate.RadialDamping = RadialDampingSum;

                neighbourAggregate.LeaderNeighbourCount = LeaderNeighbourCount;
                neighbourAggregate.SeparationCount = SeparationCount;
                neighbourAggregate.FriendlyNeighbourCount = FriendlyNeighbourCount;

                neighbourAggregate.AlignmentWeightSum = AlignmentWeightSum;
                neighbourAggregate.CohesionWeightSum = CohesionWeightSum;
                neighbourAggregate.AvoidDanger = AvoidDanger;

                return neighbourAggregate;
            }
        }

        private struct NeighbourDeduplication {
            private FixedList512Bytes<int> visitedIndices;
            private ulong seenBits0;
            private ulong seenBits1;
            private ulong seenBits2;
            private ulong seenBits3;

            public bool IsVisitedOrMark(int neighbourIndex) {
                uint hashValue = ComputeHash32((uint)neighbourIndex);
                int bitIndex = (int)(hashValue & 255u);
                int wordIndex = bitIndex >> 6;
                ulong mask = 1ul << (bitIndex & 63);

                if (!IsMaybeSeen(wordIndex, mask)) {
                    MarkSeen(wordIndex, mask);
                    TryAddVisited(neighbourIndex);
                    return false;
                }

                if (ContainsVisited(neighbourIndex)) {
                    return true;
                }

                TryAddVisited(neighbourIndex);
                return false;
            }

            private bool IsMaybeSeen(int wordIndex, ulong mask) {
                if (wordIndex == 0) return (seenBits0 & mask) != 0;
                if (wordIndex == 1) return (seenBits1 & mask) != 0;
                if (wordIndex == 2) return (seenBits2 & mask) != 0;
                return (seenBits3 & mask) != 0;
            }

            private void MarkSeen(int wordIndex, ulong mask) {
                if (wordIndex == 0) seenBits0 |= mask;
                else if (wordIndex == 1) seenBits1 |= mask;
                else if (wordIndex == 2) seenBits2 |= mask;
                else seenBits3 |= mask;
            }

            private bool ContainsVisited(int neighbourIndex) {
                for (int visitedIndex = 0; visitedIndex < visitedIndices.Length; visitedIndex++) {
                    if (visitedIndices[visitedIndex] == neighbourIndex) {
                        return true;
                    }
                }

                return false;
            }

            private void TryAddVisited(int neighbourIndex) {
                if (visitedIndices.Length < visitedIndices.Capacity) {
                    visitedIndices.Add(neighbourIndex);
                }
            }
        }

        private struct NeighbourScanState {
            public int AgentIndex;
            public int AgentBehaviourIndex;

            public float3 AgentPosition;
            public float NeighbourRadius;
            public float NeighbourRadiusSquared;
            public float SeparationRadiusSquared;

            public uint FriendlyMask;
            public uint AvoidMask;
            public uint NeutralMask;

            public float AvoidanceWeight;
            public float NeutralWeight;
            public float AvoidResponse;

            public float SchoolingRadialDamping;
            public float3 SelfPreviousVelocity;

            public FlockBehaviourSettings AgentBehaviourSettings;

            public int3 BaseCell;
            public int CellRange;

            public int MaximumNeighbourChecks;
            public int MaximumFriendlySamples;
            public int MaximumSeparationSamples;

            public int NeighbourChecks;
            public int LeaderTieSamples;
            public float MaximumLeaderWeight;

            public bool ShouldStopAll;

            public bool IsLeaderUpgrade;
            public bool CanTakeTieSample;
        }

        private readonly struct SchoolingBandParameters {
            public readonly float Strength;
            public readonly float Softness;
            public readonly float DeadZoneFraction;

            public SchoolingBandParameters(float strength, float softness, float deadZoneFraction) {
                Strength = strength;
                Softness = softness;
                DeadZoneFraction = deadZoneFraction;
            }
        }

        private struct SchoolingBandDistances {
            public float CollisionDistance;
            public float TargetDistance;
            public float DeadZoneUpper;
            public float FarDistance;

            public SchoolingBandDistances(float collisionDistance, float targetDistance, float deadZoneUpper, float farDistance) {
                CollisionDistance = collisionDistance;
                TargetDistance = targetDistance;
                DeadZoneUpper = deadZoneUpper;
                FarDistance = farDistance;
            }
        }
    }
}
