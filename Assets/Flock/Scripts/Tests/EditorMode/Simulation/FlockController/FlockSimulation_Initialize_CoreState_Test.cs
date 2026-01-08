#if UNITY_EDITOR
using System;
using System.Reflection;
using Flock.Scripts.Build.Agents.Fish.Data;
using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockController {
    public sealed class FlockSimulation_Initialize_CoreState_Test {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void Initialize_AllocatesCoreArrays_InitializesGridDefaults_AndComputesCellSearchRadius() {
            Build.Core.Simulation.Runtime.PartialFlockSimulation.FlockSimulation sim = new Build.Core.Simulation.Runtime.PartialFlockSimulation.FlockSimulation();

            FlockEnvironmentData env = new FlockEnvironmentData {
                GridOrigin = new float3(0f, 0f, 0f),
                GridResolution = new int3(2, 2, 2), // rgridCellCount=8
                CellSize = 2.0f,
                BoundsCenter = new float3(0f, 0f, 0f),
                BoundsExtents = new float3(10f, 10f, 10f),
            };

            NativeArray<FlockBehaviourSettings> settings = new NativeArray<FlockBehaviourSettings>(2, Allocator.Persistent);
            settings[0] = new FlockBehaviourSettings {
                MaxSpeed = 1.0f,
                MaxAcceleration = 1.0f,
                NeighbourRadius = 0.0f, // => viewRadius=0 => cellRange => 1
                BodyRadius = 0.0f
            };
            settings[1] = new FlockBehaviourSettings {
                MaxSpeed = 1.0f,
                MaxAcceleration = 1.0f,
                NeighbourRadius = 3.1f, // => ceil(3.1/2)=2
                BodyRadius = 0.0f
            };

            try {
                // Act
                sim.Initialize(
                    agentCount: 4,
                    environment: env,
                    behaviourSettings: settings,
                    obstaclesSource: Array.Empty<FlockObstacleData>(),
                    attractorsSource: Array.Empty<FlockAttractorData>(),
                    allocator: Allocator.Persistent,
                    logger: null);

                // Assert: core created
                Assert.That(sim.IsCreated, Is.True);
                Assert.That(sim.AgentCount, Is.EqualTo(4));
                Assert.That(sim.Positions.IsCreated, Is.True);
                Assert.That(sim.Positions.Length, Is.EqualTo(4));
                Assert.That(sim.Velocities.IsCreated, Is.True);
                Assert.That(sim.Velocities.Length, Is.EqualTo(4));

                // Assert: behaviourIds initialized to zero
                NativeArray<int> behaviourIds = GetPrivateField<NativeArray<int>>(sim, "behaviourIds");
                Assert.That(behaviourIds.IsCreated, Is.True);
                Assert.That(behaviourIds.Length, Is.EqualTo(4));
                for (int i = 0; i < behaviourIds.Length; i += 1) {
                    Assert.That(behaviourIds[i], Is.EqualTo(0), $"behaviourIds[{i}] must start at 0");
                }

                // Assert: behaviourCellSearchRadius computed correctly
                NativeArray<int> cellRanges = GetPrivateField<NativeArray<int>>(sim, "behaviourCellSearchRadius");
                Assert.That(cellRanges.IsCreated, Is.True);
                Assert.That(cellRanges.Length, Is.EqualTo(2));
                Assert.That(cellRanges[0], Is.EqualTo(1), "cellRange[0] expected 1 (NeighbourRadius=0)");
                Assert.That(cellRanges[1], Is.EqualTo(2), "cellRange[1] expected 2 (NeighbourRadius=3.1, cell=2)");

                // Assert: grid defaults
                NativeArray<int> cellAgentStarts = GetPrivateField<NativeArray<int>>(sim, "cellAgentStarts");
                NativeArray<int> cellAgentCounts = GetPrivateField<NativeArray<int>>(sim, "cellAgentCounts");

                Assert.That(cellAgentStarts.IsCreated, Is.True);
                Assert.That(cellAgentCounts.IsCreated, Is.True);
                Assert.That(cellAgentStarts.Length, Is.EqualTo(8));
                Assert.That(cellAgentCounts.Length, Is.EqualTo(8));

                for (int c = 0; c < 8; c += 1) {
                    Assert.That(cellAgentStarts[c], Is.EqualTo(-1), $"cellAgentStarts[{c}] must be -1");
                    Assert.That(cellAgentCounts[c], Is.EqualTo(0), $"cellAgentCounts[{c}] must start at 0");
                }

                // Assert: attractor grid defaults when gridCellCount > 0
                NativeArray<int> cellToIndividual = GetPrivateField<NativeArray<int>>(sim, "cellToIndividualAttractor");
                NativeArray<int> cellToGroup = GetPrivateField<NativeArray<int>>(sim, "cellToGroupAttractor");
                NativeArray<float> prioInd = GetPrivateField<NativeArray<float>>(sim, "cellIndividualPriority");
                NativeArray<float> prioGroup = GetPrivateField<NativeArray<float>>(sim, "cellGroupPriority");

                Assert.That(cellToIndividual.IsCreated, Is.True);
                Assert.That(cellToGroup.IsCreated, Is.True);
                Assert.That(prioInd.IsCreated, Is.True);
                Assert.That(prioGroup.IsCreated, Is.True);

                for (int c = 0; c < 8; c += 1) {
                    Assert.That(cellToIndividual[c], Is.EqualTo(-1), $"cellToIndividualAttractor[{c}] must start at -1");
                    Assert.That(cellToGroup[c], Is.EqualTo(-1), $"cellToGroupAttractor[{c}] must start at -1");
                    Assert.That(prioInd[c], Is.EqualTo(float.NegativeInfinity), $"cellIndividualPriority[{c}] must start at -Inf");
                    Assert.That(prioGroup[c], Is.EqualTo(float.NegativeInfinity), $"cellGroupPriority[{c}] must start at -Inf");
                }
            } finally {
                if (settings.IsCreated) settings.Dispose();
                sim.Dispose();
            }
        }

        // ---------------- reflection helpers ----------------

        private static T GetPrivateField<T>(object target, string fieldName) {
            FieldInfo fi = target.GetType().GetField(fieldName, BF);
            Assert.That(fi, Is.Not.Null, $"Missing field {target.GetType().Name}.{fieldName}");
            return (T)fi.GetValue(target);
        }
    }
}
#endif
