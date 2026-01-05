#if UNITY_EDITOR
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

using Flock.Runtime.Data;
using Flock.Runtime.Jobs;
namespace Flock.Scripts.Tests.EditorMode.Jobs.NeighbourAggregate {
    public sealed class NeighbourAggregateJob_HardSeparationTests {
        [Test]
        public void Execute_HardSeparation_UsesMaxOfSeparationAndBodySum_AndRepulsesOutsideNeighbourRadius() {
            const int agentCount = 2;

            var positions = new NativeArray<float3>(agentCount, Allocator.Temp);
            var prevVel = new NativeArray<float3>(agentCount, Allocator.Temp);
            var behaviourIds = new NativeArray<int>(agentCount, Allocator.Temp);
            var behaviourSettings = new NativeArray<FlockBehaviourSettings>(1, Allocator.Temp);
            var behaviourCellRadius = new NativeArray<int>(1, Allocator.Temp);

            var cellStarts = new NativeArray<int>(1, Allocator.Temp);
            var cellCounts = new NativeArray<int>(1, Allocator.Temp);
            var cellPairs = new NativeArray<CellAgentPair>(agentCount, Allocator.Temp);

            var outAgg = new NativeArray<Flock.Runtime.Data.NeighbourAggregate>(agentCount, Allocator.Temp);

            try {
                // Agent0 at origin, neighbour at +1.5 X
                positions[0] = new float3(0f, 0f, 0f);
                positions[1] = new float3(1.5f, 0f, 0f);

                prevVel[0] = float3.zero;
                prevVel[1] = float3.zero;

                behaviourIds[0] = 0;
                behaviourIds[1] = 0;

                // SeparationRadius is tiny, but BodyRadiusSum = 2.0 -> hard separation radius must be 2.0.
                // NeighbourRadius is 0.5 so neighbour is outside neighbour radius; only hard separation should apply.
                behaviourSettings[0] = new FlockBehaviourSettings {
                    NeighbourRadius = 0.5f,
                    SeparationRadius = 0.1f,
                    BodyRadius = 1.0f,

                    GroupMask = 0u,
                    AvoidMask = 0u,
                    NeutralMask = 0u,

                    AvoidResponse = 0f,
                    SchoolingRadialDamping = 0f,
                    SchoolingStrength = 0f,

                    MaxNeighbourChecks = 0,
                    MaxFriendlySamples = 0,
                    MaxSeparationSamples = 0
                };

                behaviourCellRadius[0] = 1;

                // Single cell containing both agents
                cellStarts[0] = 0;
                cellCounts[0] = agentCount;
                cellPairs[0] = new CellAgentPair { AgentIndex = 0 };
                cellPairs[1] = new CellAgentPair { AgentIndex = 1 };

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

                Assert.That(a0.SeparationCount, Is.EqualTo(1), "Hard separation should add exactly one separation sample.");

                // hardRadius = 2.0, distance = 1.5 => penetration = 0.5 => penetrationStrength = 0.25
                // SeparationSum -= dir * (1 + 0.25). dir is +X => SeparationSum.x = -1.25
                Assert.That(a0.SeparationSum.x, Is.EqualTo(-1.25f).Within(1e-3f));
                Assert.That(a0.SeparationSum.y, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(a0.SeparationSum.z, Is.EqualTo(0f).Within(1e-6f));

                Assert.That(a0.FriendlyNeighbourCount, Is.EqualTo(0));
                Assert.That(a0.AvoidDanger, Is.EqualTo(0f).Within(1e-6f));
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
