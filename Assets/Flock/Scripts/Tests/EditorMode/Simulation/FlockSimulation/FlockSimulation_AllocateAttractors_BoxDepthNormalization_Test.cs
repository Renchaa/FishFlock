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
namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockSimulation {
    public sealed class FlockSimulation_AllocateAttractors_BoxDepthNormalization_Test {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void AllocateAttractors_ComputesBoxDepthMinMaxNorm_RespectsRotation() {
            // Environment Y range: [-5..+5] => height=10
            FlockEnvironmentData env = new FlockEnvironmentData {
                GridOrigin = new float3(0f, 0f, 0f),
                GridResolution = new int3(1, 1, 1),
                CellSize = 1.0f,
                BoundsCenter = new float3(0f, 0f, 0f),
                BoundsExtents = new float3(1f, 5f, 1f),
            };

            var settings = new NativeArray<FlockBehaviourSettings>(1, Allocator.Persistent);
            settings[0] = new FlockBehaviourSettings {
                MaxSpeed = 1f,
                MaxAcceleration = 1f,
                NeighbourRadius = 1f,
                BodyRadius = 0f
            };

            // Box half-extents: (3,1,2)
            // Case A0: identity rotation => extentY = 1
            // world Y: [-1..+1] => norm [0.4..0.6]
            //
            // Case A1: rotate 90deg around Z => right=(0,1,0) so abs(right.y)=1 contributes *halfExtents.x (=3)
            // up.y becomes 0 => no *halfExtents.y
            // forward.y stays 0 => no *halfExtents.z
            // extentY = 3
            // world Y: [-3..+3] => norm [0.2..0.8]
            FlockAttractorData[] src = new FlockAttractorData[2];

            src[0] = new FlockAttractorData {
                Shape = FlockAttractorShape.Box,
                Position = new float3(0f, 0f, 0f),
                BoxHalfExtents = new float3(3f, 1f, 2f),
                BoxRotation = quaternion.identity
            };

            src[1] = new FlockAttractorData {
                Shape = FlockAttractorShape.Box,
                Position = new float3(0f, 0f, 0f),
                BoxHalfExtents = new float3(3f, 1f, 2f),
                BoxRotation = quaternion.EulerXYZ(0f, 0f, math.radians(90f))
            };

            var sim = new Build.Core.Simulation.Runtime.PartialFlockSimulation.FlockSimulation();

            try {
                sim.Initialize(
                    agentCount: 1,
                    environment: env,
                    behaviourSettings: settings,
                    obstaclesSource: Array.Empty<FlockObstacleData>(),
                    attractorsSource: src,
                    allocator: Allocator.Persistent,
                    logger: null);

                NativeArray<FlockAttractorData> attractors = GetPrivateField<NativeArray<FlockAttractorData>>(sim, "attractors");
                Assert.That(attractors.IsCreated, Is.True);
                Assert.That(attractors.Length, Is.EqualTo(2));

                // A0 identity => [0.4..0.6]
                Assert.That(attractors[0].DepthMinNorm, Is.EqualTo(0.4f).Within(1e-6f));
                Assert.That(attractors[0].DepthMaxNorm, Is.EqualTo(0.6f).Within(1e-6f));

                // A1 rotated => [0.2..0.8]
                Assert.That(attractors[1].DepthMinNorm, Is.EqualTo(0.2f).Within(1e-6f));
                Assert.That(attractors[1].DepthMaxNorm, Is.EqualTo(0.8f).Within(1e-6f));
            } finally {
                if (settings.IsCreated) settings.Dispose();
                sim.Dispose();
            }
        }

        private static T GetPrivateField<T>(object target, string fieldName) {
            FieldInfo fi = target.GetType().GetField(fieldName, BF);
            Assert.That(fi, Is.Not.Null, $"Missing field {target.GetType().Name}.{fieldName}");
            return (T)fi.GetValue(target);
        }
    }
}
#endif
