using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Scripts.Build.Agents.Fish.Data;

using System;
using NUnit.Framework;
using Unity.Collections;
using System.Reflection;
using Unity.Mathematics;

namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockSimulation
{
    public sealed class FlockSimulation_AllocateAttractors_SphereDepthNormalization_Test
    {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void AllocateAttractors_ComputesSphereDepthMinMaxNorm_WithSwapAndSaturate()
        {
            // Environment Y range: [-5..+5] => height=10
            FlockEnvironmentData env = new FlockEnvironmentData
            {
                GridOrigin = new float3(0f, 0f, 0f),
                GridResolution = new int3(1, 1, 1),
                CellSize = 1.0f,
                BoundsCenter = new float3(0f, 0f, 0f),
                BoundsExtents = new float3(1f, 5f, 1f),
            };

            var settings = new NativeArray<FlockBehaviourSettings>(1, Allocator.Persistent);
            settings[0] = new FlockBehaviourSettings
            {
                MaxSpeed = 1f,
                MaxAcceleration = 1f,
                NeighbourRadius = 1f,
                BodyRadius = 0f
            };

            // 3 sphere attractors:
            // A0: y=0, radius=2 => world [-2..2] => norm [0.3..0.7]
            // A1: y=0, radius=-2 => world [2..-2] => norm [0.7..0.3] => swap => [0.3..0.7]
            // A2: y=10, radius=2 => world [8..12] => norm [>1 .. >1] => saturate => [1..1]
            FlockAttractorData[] src = new FlockAttractorData[3];

            src[0] = new FlockAttractorData
            {
                Shape = FlockAttractorShape.Sphere,
                Position = new float3(0f, 0f, 0f),
                Radius = 2f
            };

            src[1] = new FlockAttractorData
            {
                Shape = FlockAttractorShape.Sphere,
                Position = new float3(0f, 0f, 0f),
                Radius = -2f
            };

            src[2] = new FlockAttractorData
            {
                Shape = FlockAttractorShape.Sphere,
                Position = new float3(0f, 10f, 0f),
                Radius = 2f
            };

            var sim = new Build.Core.Simulation.Runtime.PartialFlockSimulation.FlockSimulation();

            try
            {
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
                Assert.That(attractors.Length, Is.EqualTo(3));

                // A0 expected [0.3..0.7]
                Assert.That(attractors[0].DepthMinNorm, Is.EqualTo(0.3f).Within(1e-6f));
                Assert.That(attractors[0].DepthMaxNorm, Is.EqualTo(0.7f).Within(1e-6f));

                // A1 negative radius must still end up ordered [0.3..0.7]
                Assert.That(attractors[1].DepthMinNorm, Is.EqualTo(0.3f).Within(1e-6f));
                Assert.That(attractors[1].DepthMaxNorm, Is.EqualTo(0.7f).Within(1e-6f));

                // A2 saturate to [1..1]
                Assert.That(attractors[2].DepthMinNorm, Is.EqualTo(1.0f).Within(1e-6f));
                Assert.That(attractors[2].DepthMaxNorm, Is.EqualTo(1.0f).Within(1e-6f));
            }
            finally
            {
                if (settings.IsCreated) settings.Dispose();
                sim.Dispose();
            }
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo fi = target.GetType().GetField(fieldName, BF);
            Assert.That(fi, Is.Not.Null, $"Missing field {target.GetType().Name}.{fieldName}");
            return (T)fi.GetValue(target);
        }
    }
}
