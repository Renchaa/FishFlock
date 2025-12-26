// File: Assets/Flock/Runtime/Jobs/NeighbourAggregateJob.cs
namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /// <summary>
    /// Phase 2: neighbour scan only. Produces stable per-agent aggregate buffers.
    /// No steering integration happens here.
    /// </summary>
    [BurstCompile]
    public struct NeighbourAggregateJob : IJobParallelFor {
        // Core agent data
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> PrevVelocities;

        // Behaviour
        [ReadOnly] public NativeArray<int> BehaviourIds;
        [ReadOnly] public NativeArray<FlockBehaviourSettings> BehaviourSettings;
        [ReadOnly] public NativeArray<int> BehaviourCellSearchRadius;

        // Spatial grid
        [ReadOnly] public NativeArray<int> CellAgentStarts;
        [ReadOnly] public NativeArray<int> CellAgentCounts;
        [ReadOnly] public NativeArray<CellAgentPair> CellAgentPairs;

        [ReadOnly] public float3 GridOrigin;
        [ReadOnly] public int3 GridResolution;
        [ReadOnly] public float CellSize;

        // Outputs
        [NativeDisableParallelForRestriction]
        public NativeArray<NeighbourAggregate> OutAggregates;

        static uint Hash32(uint x) {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        public void Execute(int index) {
            var agg = default(NeighbourAggregate);

            int behaviourIndex = BehaviourIds[index];
            if ((uint)behaviourIndex >= (uint)BehaviourSettings.Length) {
                OutAggregates[index] = agg;
                return;
            }

            FlockBehaviourSettings b = BehaviourSettings[behaviourIndex];

            float neighbourRadius = b.NeighbourRadius;
            float separationRadius = b.SeparationRadius;

            uint friendlyMask = b.GroupMask;
            uint avoidMask = b.AvoidMask;
            uint neutralMask = b.NeutralMask;

            float3 alignment = float3.zero;
            float3 cohesion = float3.zero;
            float3 separation = float3.zero;
            float3 avoidSeparation = float3.zero;
            float3 radialDamping = float3.zero;

            int leaderNeighbourCount = 0;
            int separationCount = 0;
            int friendlyNeighbourCount = 0;
            float alignmentWeightSum = 0f;
            float cohesionWeightSum = 0f;
            float avoidDanger = 0f;

            // Per-agent dedup state
            FixedList512Bytes<int> visited = default;
            ulong seen0 = 0ul, seen1 = 0ul, seen2 = 0ul, seen3 = 0ul;

            AccumulateNeighbourForces(
                agentIndex: index,
                myBehaviourIndex: behaviourIndex,
                friendlyMask: friendlyMask,
                avoidMask: avoidMask,
                neutralMask: neutralMask,
                agentPosition: Positions[index],
                neighbourRadius: neighbourRadius,
                separationRadius: separationRadius,
                alignment: ref alignment,
                cohesion: ref cohesion,
                separation: ref separation,
                leaderNeighbourCount: ref leaderNeighbourCount,
                separationCount: ref separationCount,
                alignmentWeightSum: ref alignmentWeightSum,
                cohesionWeightSum: ref cohesionWeightSum,
                avoidDanger: ref avoidDanger,
                friendlyNeighbourCount: ref friendlyNeighbourCount,
                avoidSeparation: ref avoidSeparation,
                radialDamping: ref radialDamping,
                visited: ref visited,
                seen0: ref seen0,
                seen1: ref seen1,
                seen2: ref seen2,
                seen3: ref seen3);

            agg.AlignmentSum = alignment;
            agg.CohesionSum = cohesion;
            agg.SeparationSum = separation;
            agg.AvoidSeparationSum = avoidSeparation;
            agg.RadialDamping = radialDamping;

            agg.LeaderNeighbourCount = leaderNeighbourCount;
            agg.SeparationCount = separationCount;
            agg.FriendlyNeighbourCount = friendlyNeighbourCount;

            agg.AlignmentWeightSum = alignmentWeightSum;
            agg.CohesionWeightSum = cohesionWeightSum;
            agg.AvoidDanger = avoidDanger;

            OutAggregates[index] = agg;
        }

        static bool IsNeighbourVisitedOrMark(
            int neighbourIndex,
            ref FixedList512Bytes<int> visited,
            ref ulong seen0,
            ref ulong seen1,
            ref ulong seen2,
            ref ulong seen3) {

            uint h = Hash32((uint)neighbourIndex);
            int bitIndex = (int)(h & 255u);     // 0..255
            int word = bitIndex >> 6;           // 0..3
            ulong mask = 1ul << (bitIndex & 63);

            bool maybeSeen;
            if (word == 0) maybeSeen = (seen0 & mask) != 0;
            else if (word == 1) maybeSeen = (seen1 & mask) != 0;
            else if (word == 2) maybeSeen = (seen2 & mask) != 0;
            else maybeSeen = (seen3 & mask) != 0;

            if (!maybeSeen) {
                if (word == 0) seen0 |= mask;
                else if (word == 1) seen1 |= mask;
                else if (word == 2) seen2 |= mask;
                else seen3 |= mask;

                if (visited.Length < visited.Capacity) {
                    visited.Add(neighbourIndex);
                }
                return false;
            }

            for (int i = 0; i < visited.Length; i++) {
                if (visited[i] == neighbourIndex) {
                    return true;
                }
            }

            if (visited.Length < visited.Capacity) {
                visited.Add(neighbourIndex);
            }

            return false;
        }

        float3 ComputeSchoolingBandForce(
            int myBehaviourIndex,
            int neighbourBehaviourIndex,
            float distance,
            float3 dir,
            out float collisionDist,
            out float targetDist,
            out float deadZoneUpper,
            out float farDist) {

            collisionDist = 0f;
            targetDist = 0f;
            deadZoneUpper = 0f;
            farDist = 0f;

            if ((uint)myBehaviourIndex >= (uint)BehaviourSettings.Length
                || (uint)neighbourBehaviourIndex >= (uint)BehaviourSettings.Length) {
                return float3.zero;
            }

            FlockBehaviourSettings a = BehaviourSettings[myBehaviourIndex];
            FlockBehaviourSettings b = BehaviourSettings[neighbourBehaviourIndex];

            float rA = a.BodyRadius;
            float rB = b.BodyRadius;

            if (rA <= 0f && rB <= 0f) {
                return float3.zero;
            }

            float spacing = math.max(0.5f, 0.5f * (a.SchoolingSpacingFactor + b.SchoolingSpacingFactor));
            float outer = math.max(1f, 0.5f * (a.SchoolingOuterFactor + b.SchoolingOuterFactor));
            float strength = math.max(0f, 0.5f * (a.SchoolingStrength + b.SchoolingStrength));
            if (strength <= 0f) {
                return float3.zero;
            }

            float softness = math.clamp(0.5f * (a.SchoolingInnerSoftness + b.SchoolingInnerSoftness), 0f, 1f);
            float deadFrac = math.clamp(0.5f * (a.SchoolingDeadzoneFraction + b.SchoolingDeadzoneFraction), 0f, 0.5f);

            collisionDist = rA + rB;
            targetDist = collisionDist * spacing;
            farDist = targetDist * outer;

            if (distance <= 0f || distance >= farDist) {
                return float3.zero;
            }

            float deadRadius = deadFrac * targetDist;
            float deadLower = targetDist;
            deadZoneUpper = targetDist + deadRadius;

            float force;

            if (distance < collisionDist) {
                float t = (collisionDist - distance) / math.max(collisionDist, 1e-3f);
                force = t;
            } else if (distance < targetDist) {
                float innerSpan = math.max(targetDist - collisionDist, 1e-3f);
                float t = (targetDist - distance) / innerSpan;

                float t2 = t * t;
                float shaped;

                if (softness <= 0f) {
                    shaped = t;
                } else {
                    float t3 = t2 * t;
                    if (softness <= 0.5f) {
                        float u = softness * 2f;
                        shaped = math.lerp(t, t2, u);
                    } else {
                        float u = (softness - 0.5f) * 2f;
                        shaped = math.lerp(t2, t3, u);
                    }
                }

                force = shaped;
            } else {
                if (distance >= deadLower && distance <= deadZoneUpper) {
                    return float3.zero;
                }

                float attractStart = deadZoneUpper;
                float attractSpan = math.max(farDist - attractStart, 1e-3f);
                float t = (distance - attractStart) / attractSpan;

                float falloff = 1f - t;
                force = -falloff;
            }

            if (math.abs(force) <= 1e-5f) {
                return float3.zero;
            }

            return -dir * (force * strength);
        }

        void AccumulateNeighbourForces(
            int agentIndex,
            int myBehaviourIndex,
            uint friendlyMask,
            uint avoidMask,
            uint neutralMask,
            float3 agentPosition,
            float neighbourRadius,
            float separationRadius,
            ref float3 alignment,
            ref float3 cohesion,
            ref float3 separation,
            ref int leaderNeighbourCount,
            ref int separationCount,
            ref float alignmentWeightSum,
            ref float cohesionWeightSum,
            ref float avoidDanger,
            ref int friendlyNeighbourCount,
            ref float3 avoidSeparation,
            ref float3 radialDamping,
            ref FixedList512Bytes<int> visited,
            ref ulong seen0,
            ref ulong seen1,
            ref ulong seen2,
            ref ulong seen3) {

            float neighbourRadiusSquared = neighbourRadius * neighbourRadius;
            float separationRadiusSquared = separationRadius * separationRadius;

            avoidDanger = 0.0f;
            friendlyNeighbourCount = 0;
            avoidSeparation = float3.zero;

            if ((uint)myBehaviourIndex >= (uint)BehaviourSettings.Length) {
                return;
            }

            FlockBehaviourSettings my = BehaviourSettings[myBehaviourIndex];

            float myAvoidWeight = my.AvoidanceWeight;
            float myNeutralWeight = my.NeutralWeight;
            float myAvoidResponse = math.max(0f, my.AvoidResponse);

            float myRadialDamping = my.SchoolingRadialDamping;
            float3 selfPrevVel = PrevVelocities[agentIndex];

            int3 baseCell = GetCell(agentPosition);

            float maxLeaderWeight = -1.0f;
            const float epsilon = 1e-4f;

            int cellRange = 1;
            if (BehaviourCellSearchRadius.IsCreated && (uint)myBehaviourIndex < (uint)BehaviourCellSearchRadius.Length) {
                cellRange = math.max(BehaviourCellSearchRadius[myBehaviourIndex], 1);
            }

            int maxChecks = math.max(0, my.MaxNeighbourChecks);
            int maxFriendlySamples = math.max(0, my.MaxFriendlySamples);
            int maxSeparationSamples = math.max(0, my.MaxSeparationSamples);

            int neighbourChecks = 0;
            int leaderTieSamples = 0;

            bool stopAll = false;

            for (int x = -cellRange; x <= cellRange; x++) {
                if (stopAll) break;

                for (int y = -cellRange; y <= cellRange; y++) {
                    if (stopAll) break;

                    for (int z = -cellRange; z <= cellRange; z++) {
                        if (stopAll) break;

                        int3 neighbourCell = baseCell + new int3(x, y, z);

                        if (!IsCellInsideGrid(neighbourCell)) {
                            continue;
                        }

                        int cellId = GetCellId(neighbourCell);

                        int countInCell = CellAgentCounts[cellId];
                        if (countInCell <= 0) {
                            continue;
                        }

                        int startInCell = CellAgentStarts[cellId];
                        for (int p = 0; p < countInCell; p += 1) {
                            int neighbourIndex = CellAgentPairs[startInCell + p].AgentIndex;
                            if (neighbourIndex == agentIndex) {
                                continue;
                            }

                            if (IsNeighbourVisitedOrMark(neighbourIndex, ref visited, ref seen0, ref seen1, ref seen2, ref seen3)) {
                                continue;
                            }

                            if (maxChecks > 0 && neighbourChecks >= maxChecks) {
                                stopAll = true;
                                break;
                            }
                            neighbourChecks += 1;

                            float3 neighbourPosition = Positions[neighbourIndex];
                            float3 offset = neighbourPosition - agentPosition;
                            float distanceSquared = math.lengthsq(offset);
                            if (distanceSquared < 1e-6f) {
                                continue;
                            }

                            int neighbourBehaviourIndex = BehaviourIds[neighbourIndex];
                            if ((uint)neighbourBehaviourIndex >= (uint)BehaviourSettings.Length) {
                                continue;
                            }

                            FlockBehaviourSettings nb = BehaviourSettings[neighbourBehaviourIndex];

                            float hardRadiusSq = separationRadiusSquared;

                            float myBody = my.BodyRadius;
                            float nbBody = nb.BodyRadius;

                            float collisionDistBody = myBody + nbBody;
                            if (collisionDistBody > 0f) {
                                float collisionDistSq = collisionDistBody * collisionDistBody;
                                if (collisionDistSq > hardRadiusSq) {
                                    hardRadiusSq = collisionDistSq;
                                }
                            }

                            bool withinHard = hardRadiusSq > 0f && distanceSquared < hardRadiusSq;
                            bool withinNeighbour = distanceSquared <= neighbourRadiusSquared;

                            if (!withinHard && !withinNeighbour) {
                                continue;
                            }

                            float invDist = math.rsqrt(distanceSquared);
                            float distance = distanceSquared * invDist;
                            float3 dirUnit = offset * invDist;

                            if (withinHard) {
                                bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                if (canAddSep) {
                                    float hardInv = math.rsqrt(math.max(hardRadiusSq, 1e-12f));
                                    float hardRadius = hardRadiusSq * hardInv;

                                    float penetration = hardRadius - distance;
                                    float strengthPen = penetration / math.max(hardRadius, 1e-3f);

                                    separation -= dirUnit * (1f + strengthPen);
                                    separationCount += 1;
                                }
                            }

                            if (!withinNeighbour || neighbourRadius <= 1e-6f) {
                                continue;
                            }

                            uint bit = neighbourBehaviourIndex < 32 ? (1u << neighbourBehaviourIndex) : 0u;

                            bool isFriendly = bit != 0u && (friendlyMask & bit) != 0u;
                            bool isAvoid = bit != 0u && (avoidMask & bit) != 0u;
                            bool isNeutral = bit != 0u && (neutralMask & bit) != 0u;

                            if (!isFriendly && !isAvoid && !isNeutral) {
                                continue;
                            }

                            float invNeighbourRadius = 1.0f / neighbourRadius;
                            float t = 1.0f - math.saturate(distance * invNeighbourRadius);

                            if (isFriendly) {
                                friendlyNeighbourCount += 1;

                                float neighbourLeaderWeight = nb.LeadershipWeight;
                                bool isLeaderUpgrade = neighbourLeaderWeight > maxLeaderWeight + epsilon;

                                bool isLeaderTie =
                                    math.abs(neighbourLeaderWeight - maxLeaderWeight) <= epsilon
                                    && maxLeaderWeight > -1.0f;

                                bool canTakeTieSample =
                                    isLeaderTie && (maxFriendlySamples == 0 || leaderTieSamples < maxFriendlySamples);

                                bool considerLeadership = isLeaderUpgrade || canTakeTieSample;

                                bool needBand =
                                    myRadialDamping > 0f
                                    || considerLeadership
                                    || (maxSeparationSamples == 0 || separationCount < maxSeparationSamples);

                                float collisionDistBand = 0f;
                                float targetDistBand = 0f;
                                float deadUpperBand = 0f;
                                float farDistBand = 0f;

                                float3 bandForce = float3.zero;

                                if (needBand) {
                                    bandForce = ComputeSchoolingBandForce(
                                        myBehaviourIndex,
                                        neighbourBehaviourIndex,
                                        distance,
                                        dirUnit,
                                        out collisionDistBand,
                                        out targetDistBand,
                                        out deadUpperBand,
                                        out farDistBand);

                                    if (math.lengthsq(bandForce) > 0f) {
                                        bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                        if (canAddSep) {
                                            separation += bandForce;
                                            separationCount += 1;
                                        }
                                    }
                                }

                                bool haveBand =
                                    farDistBand > 0f &&
                                    collisionDistBand > 0f &&
                                    targetDistBand > collisionDistBand;

                                bool isFarForCohesion =
                                    haveBand &&
                                    (deadUpperBand > 0f
                                        ? distance > deadUpperBand
                                        : distance > targetDistBand) &&
                                    distance < farDistBand;

                                if (myRadialDamping > 0f && haveBand && distance < targetDistBand) {
                                    float3 otherPrevVel = PrevVelocities[neighbourIndex];
                                    float vRel = math.dot(selfPrevVel - otherPrevVel, dirUnit);

                                    if (vRel > 0f) {
                                        float innerSpan = math.max(targetDistBand - collisionDistBand, 1e-3f);
                                        float proximity = math.saturate((targetDistBand - distance) / innerSpan);
                                        float dampingStrength = myRadialDamping * proximity;

                                        float damping = vRel * dampingStrength;
                                        radialDamping -= dirUnit * damping;
                                    }
                                }

                                if (considerLeadership) {
                                    float3 neighbourVelocity = PrevVelocities[neighbourIndex];

                                    if (isLeaderUpgrade) {
                                        maxLeaderWeight = neighbourLeaderWeight;
                                        leaderNeighbourCount = 1;

                                        alignment = neighbourVelocity * t;
                                        alignmentWeightSum = t;

                                        if (isFarForCohesion) {
                                            cohesion = neighbourPosition * t;
                                            cohesionWeightSum = t;
                                        } else {
                                            cohesion = float3.zero;
                                            cohesionWeightSum = 0f;
                                        }

                                        leaderTieSamples = 1;
                                    } else if (canTakeTieSample) {
                                        leaderNeighbourCount += 1;

                                        alignment += neighbourVelocity * t;
                                        alignmentWeightSum += t;

                                        if (isFarForCohesion) {
                                            cohesion += neighbourPosition * t;
                                            cohesionWeightSum += t;
                                        }

                                        leaderTieSamples += 1;
                                    }
                                }
                            }

                            if (isAvoid && myAvoidResponse > 0f) {
                                float neighbourAvoidWeight = nb.AvoidanceWeight;

                                if (myAvoidWeight < neighbourAvoidWeight) {
                                    float weightDelta = neighbourAvoidWeight - myAvoidWeight;
                                    float normalised = weightDelta / math.max(neighbourAvoidWeight, 1e-3f);

                                    float localIntensity = t * normalised * myAvoidResponse;

                                    if (localIntensity > avoidDanger) {
                                        avoidDanger = localIntensity;
                                    }

                                    bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                    if (canAddSep) {
                                        float3 repulse = -dirUnit * localIntensity;
                                        separation += repulse;
                                        avoidSeparation += repulse;
                                        separationCount += 1;
                                    }
                                }
                            }

                            if (isNeutral) {
                                float neighbourNeutralWeight = nb.NeutralWeight;

                                if (myNeutralWeight < neighbourNeutralWeight) {
                                    float weightDelta = neighbourNeutralWeight - myNeutralWeight;
                                    float normalised = weightDelta / math.max(neighbourNeutralWeight, 1e-3f);

                                    bool canAddSep = (maxSeparationSamples == 0) || (separationCount < maxSeparationSamples);
                                    if (canAddSep) {
                                        float3 softRepulse = -dirUnit * (t * normalised * 0.5f);
                                        separation += softRepulse;
                                        separationCount += 1;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        int3 GetCell(float3 position) {
            float cellSize = math.max(CellSize, 0.0001f);
            float3 local = position - GridOrigin;
            float3 scaled = local / cellSize;

            int3 cell = (int3)math.floor(scaled);

            cell = math.clamp(
                cell,
                new int3(0, 0, 0),
                GridResolution - new int3(1, 1, 1));

            return cell;
        }

        bool IsCellInsideGrid(int3 cell) {
            if (cell.x < 0 || cell.y < 0 || cell.z < 0) {
                return false;
            }

            if (cell.x >= GridResolution.x
                || cell.y >= GridResolution.y
                || cell.z >= GridResolution.z) {
                return false;
            }

            return true;
        }

        int GetCellId(int3 cell) {
            return cell.x
                   + cell.y * GridResolution.x
                   + cell.z * GridResolution.x * GridResolution.y;
        }
    }
}
