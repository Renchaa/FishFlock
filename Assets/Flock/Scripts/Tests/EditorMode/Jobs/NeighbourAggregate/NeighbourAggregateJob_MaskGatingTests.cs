#if UNITY_EDITOR
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

using Flock.Runtime.Data;
using Flock.Runtime.Jobs;
namespace Flock.Scripts.Tests.EditorMode.Jobs.NeighbourAggregate {
    public sealed class NeighbourAggregateJob_MaskGatingTests {
        [Test]
        public void Execute_MaskGating_AndAvoidDanger_MatchesExpectedIntensity() {
            // agent0 observes 3 neighbours:
            // - behaviour 1 -> Friendly
            // - behaviour 2 -> Avoid
            // - behaviour 3 -> Neutral
            const int agentCount = 4;

            const float neighbourRadius = 2.0f;
            const float distance = 0.5f; // inside neighbour radius

            var positions = new NativeArray<float3>(agentCount, Allocator.Temp);
            var prevVel = new NativeArray<float3>(agentCount, Allocator.Temp);
            var behaviourIds = new NativeArray<int>(agentCount, Allocator.Temp);

            var behaviourSettings = new NativeArray<FlockBehaviourSettings>(4, Allocator.Temp);
            var behaviourCellRadius = new NativeArray<int>(4, Allocator.Temp);

            var cellStarts = new NativeArray<int>(1, Allocator.Temp);
            var cellCounts = new NativeArray<int>(1, Allocator.Temp);
            var cellPairs = new NativeArray<CellAgentPair>(agentCount, Allocator.Temp);

            var outAgg = new NativeArray<Runtime.Data.NeighbourAggregate>(agentCount, Allocator.Temp);

            try {
                positions[0] = new float3(0f, 0f, 0f);
                positions[1] = new float3(distance, 0f, 0f);
                positions[2] = new float3(distance, 0f, 0f);
                positions[3] = new float3(distance, 0f, 0f);

                for (int i = 0; i < agentCount; i++)
                    prevVel[i] = float3.zero;

                behaviourIds[0] = 0;
                behaviourIds[1] = 1;
                behaviourIds[2] = 2;
                behaviourIds[3] = 3;

                // Observer behaviour 0
                behaviourSettings[0] = new FlockBehaviourSettings {
                    NeighbourRadius = neighbourRadius,
                    SeparationRadius = 0.1f,
                    BodyRadius = 0f, // prevent hard separation from interfering

                    GroupMask = (1u << 1),
                    AvoidMask = (1u << 2),
                    NeutralMask = (1u << 3),

                    AvoidanceWeight = 1.0f,
                    NeutralWeight = 1.0f,
                    AvoidResponse = 1.0f,

                    SchoolingStrength = 0f,
                    SchoolingRadialDamping = 0f,
                    LeadershipWeight = -1f,

                    MaxNeighbourChecks = 0,
                    MaxFriendlySamples = 0,
                    MaxSeparationSamples = 0
                };

                // Friendly neighbour behaviour 1 (make leadership inert)
                behaviourSettings[1] = new FlockBehaviourSettings {
                    LeadershipWeight = -1f,
                    SchoolingStrength = 0f,
                    BodyRadius = 0f
                };

                // Avoid neighbour behaviour 2 (higher avoidance triggers avoid danger)
                behaviourSettings[2] = new FlockBehaviourSettings {
                    AvoidanceWeight = 5.0f,
                    SchoolingStrength = 0f,
                    BodyRadius = 0f
                };

                // Neutral neighbour behaviour 3 (higher neutral triggers neutral repulse)
                behaviourSettings[3] = new FlockBehaviourSettings {
                    NeutralWeight = 3.0f,
                    SchoolingStrength = 0f,
                    BodyRadius = 0f
                };

                for (int i = 0; i < 4; i++)
                    behaviourCellRadius[i] = 1;

                cellStarts[0] = 0;
                cellCounts[0] = agentCount;
                for (int i = 0; i < agentCount; i++)
                    cellPairs[i] = new CellAgentPair { AgentIndex = i };

                var job = new NeighbourAggregateJob {
                    Positions = positions,
                    PrevVelocities = prevVel,

                    BehaviourIds = behaviourIds,
                    BehaviourSettings = behaviourSettings,
                    BehaviourCellSearchRadius = behaviourCellRadius,

                    CellAgentStarts = cellStarts,
                    CellAgentCounts = cellCounts,
                    CellAgentPairs = cellPairs,

                    GridOrigin = new float3(-10f, -10f, -10f),
                    GridResolution = new int3(1, 1, 1),
                    CellSize = 100f,

                    OutAggregates = outAgg
                };

                job.Execute(0);
                Runtime.Data.NeighbourAggregate a0 = outAgg[0];

                Assert.That(a0.FriendlyNeighbourCount, Is.EqualTo(1), "Exactly one neighbour should be classified as friendly.");

                // expected avoid danger:
                // proximityWeight = 1 - saturate(distance / neighbourRadius) = 1 - 0.25 = 0.75
                // normalised = (5 - 1) / 5 = 0.8
                // intensity = 0.75 * 0.8 * AvoidResponse(1) = 0.6
                const float expectedAvoidDanger = 0.6f;
                Assert.That(a0.AvoidDanger, Is.EqualTo(expectedAvoidDanger).Within(1e-4f));

                // Avoid + Neutral should add repulsion samples
                Assert.That(a0.SeparationCount, Is.GreaterThanOrEqualTo(2));
            } finally {
                positions.Dispose();
                prevVel.Dispose();
                behaviourIds.Dispose();
                behaviourSettings.Dispose();
                behaviourCellRadius.Dispose();
                cellStarts.Dispose();
                cellCounts.Dispose();
                cellPairs.Dispose();
                outAgg.Dispose();
            }
        }
    }
}
#endif
