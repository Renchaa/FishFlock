using Flock.Scripts.Build.Infrastructure.Grid.Jobs;
using Flock.Scripts.Build.Infrastructure.Grid.Data;
using Flock.Scripts.Build.Agents.Fish.Data;

using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Flock.Scripts.Tests.EditorMode.Jobs.NeighbourAggregate
{
    public sealed class NeighbourAggregateJob_LeadershipTests
    {
        [Test]
        public void Execute_LeadershipUpgrade_ThenTieSampling_AccumulatesAlignmentFromTopLeadersOnly()
        {
            // agent0 observes 3 friendly neighbours:
            // agent1 leadership 5 (upgrade)
            // agent2 leadership 5 (tie -> included)
            // agent3 leadership 4 (lower -> should not contribute to leader-based alignment)
            const int agentCount = 4;
            const int behaviourCount = 4;

            const float neighbourRadius = 2.0f;
            const float distance = 1.0f; // proximityWeight = 1 - 0.5 = 0.5

            var positions = new NativeArray<float3>(agentCount, Allocator.Temp);
            var prevVel = new NativeArray<float3>(agentCount, Allocator.Temp);
            var behaviourIds = new NativeArray<int>(agentCount, Allocator.Temp);

            var behaviourSettings = new NativeArray<FlockBehaviourSettings>(behaviourCount, Allocator.Temp);
            var behaviourCellRadius = new NativeArray<int>(behaviourCount, Allocator.Temp);

            var cellStarts = new NativeArray<int>(1, Allocator.Temp);
            var cellCounts = new NativeArray<int>(1, Allocator.Temp);
            var cellPairs = new NativeArray<CellAgentPair>(agentCount, Allocator.Temp);

            var outAgg = new NativeArray<Build.Agents.Fish.Data.NeighbourAggregate>(agentCount, Allocator.Temp);

            try
            {
                positions[0] = new float3(0f, 0f, 0f);
                positions[1] = new float3(distance, 0f, 0f);
                positions[2] = new float3(distance, 0f, 0f);
                positions[3] = new float3(distance, 0f, 0f);

                prevVel[0] = float3.zero;
                prevVel[1] = new float3(1f, 0f, 0f);
                prevVel[2] = new float3(0f, 1f, 0f);
                prevVel[3] = new float3(0f, 0f, 1f);

                behaviourIds[0] = 0;
                behaviourIds[1] = 1;
                behaviourIds[2] = 2;
                behaviourIds[3] = 3;

                // Observer: all three neighbour behaviours are friendly
                behaviourSettings[0] = new FlockBehaviourSettings
                {
                    NeighbourRadius = neighbourRadius,
                    SeparationRadius = 0.1f,
                    BodyRadius = 0f,

                    GroupMask = (1u << 1) | (1u << 2) | (1u << 3),
                    AvoidMask = 0u,
                    NeutralMask = 0u,

                    SchoolingStrength = 0f,
                    SchoolingRadialDamping = 0f,

                    // 0 means "no cap" in your code path
                    MaxFriendlySamples = 0,
                    MaxNeighbourChecks = 0,
                    MaxSeparationSamples = 0
                };

                behaviourSettings[1] = new FlockBehaviourSettings { LeadershipWeight = 5f, SchoolingStrength = 0f, BodyRadius = 0f };
                behaviourSettings[2] = new FlockBehaviourSettings { LeadershipWeight = 5f, SchoolingStrength = 0f, BodyRadius = 0f };
                behaviourSettings[3] = new FlockBehaviourSettings { LeadershipWeight = 4f, SchoolingStrength = 0f, BodyRadius = 0f };

                for (int i = 0; i < behaviourCount; i++)
                    behaviourCellRadius[i] = 1;

                cellStarts[0] = 0;
                cellCounts[0] = agentCount;
                for (int i = 0; i < agentCount; i++)
                    cellPairs[i] = new CellAgentPair { AgentIndex = i };

                var job = new NeighbourAggregateJob
                {
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
                Build.Agents.Fish.Data.NeighbourAggregate a0 = outAgg[0];

                Assert.That(a0.FriendlyNeighbourCount, Is.EqualTo(3));
                Assert.That(a0.LeaderNeighbourCount, Is.EqualTo(2), "Only the top leader weight and ties should count as leaders.");

                // Expected: upgrade sets alignment to vel1 * 0.5, tie adds vel2 * 0.5. vel3 should not be included.
                float3 expectedAlignmentSum = new float3(1f, 0f, 0f) * 0.5f + new float3(0f, 1f, 0f) * 0.5f;
                float expectedWeightSum = 1.0f;

                Assert.That(a0.AlignmentSum.x, Is.EqualTo(expectedAlignmentSum.x).Within(1e-4f));
                Assert.That(a0.AlignmentSum.y, Is.EqualTo(expectedAlignmentSum.y).Within(1e-4f));
                Assert.That(a0.AlignmentSum.z, Is.EqualTo(expectedAlignmentSum.z).Within(1e-4f));

                Assert.That(a0.AlignmentWeightSum, Is.EqualTo(expectedWeightSum).Within(1e-4f));
            }
            finally
            {
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
