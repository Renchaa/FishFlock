// File: Assets/Tests/EditMode/Data/Environment/FlockController_ApplySimulationToTransforms_PositionAndRotation_Test.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using Flock.Scripts.Build.Agents.Fish.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Flock.Scripts.Build.Influence.Environment.Bounds.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;
using Flock.Scripts.Build.Influence.Environment.Attractors.Data;

namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockController {
    public sealed class FlockController_ApplySimulationToTransforms_PositionAndRotation_Test {
        [Test]
        public void ApplySimulationToTransforms_CopiesPositionsAndFacesVelocityDirection() {
            GameObject controllerGo = null;
            GameObject aGo = null;
            GameObject bGo = null;

            NativeArray<FlockBehaviourSettings> behaviourSettings = default;
            Build.Core.Simulation.Runtime.PartialFlockSimulation.FlockSimulation sim = null;

            try {
                controllerGo = new GameObject("FlockController_Test");
                var controller = controllerGo.AddComponent<Build.Core.Simulation.Runtime.PartialFlockController.FlockController>();

                // Derīga Box vide (lai Initialize strādā deterministiski un bez “guard” scenārijiem)
                SetPrivateField(controller, "boundsType", FlockBoundsType.Box);
                SetPrivateField(controller, "boundsCenter", Vector3.zero);
                SetPrivateField(controller, "boundsExtents", new Vector3(5f, 5f, 5f));
                SetPrivateField(controller, "cellSize", 1.0f);
                SetPrivateField(controller, "globalDamping", 0.1f);

                var env = InvokePrivateMethod<FlockEnvironmentData>(controller, "BuildEnvironmentData");

                behaviourSettings = new NativeArray<FlockBehaviourSettings>(1, Allocator.Persistent);
                behaviourSettings[0] = new FlockBehaviourSettings {
                    MaxSpeed = 5f,
                    MaxAcceleration = 10f,
                    NeighbourRadius = 3f,
                    SeparationRadius = 0.5f
                };

                sim = new Build.Core.Simulation.Runtime.PartialFlockSimulation.FlockSimulation();
                sim.Initialize(
                    agentCount: 2,
                    environment: env,
                    behaviourSettings: behaviourSettings,
                    obstaclesSource: Array.Empty<FlockObstacleData>(),
                    attractorsSource: Array.Empty<FlockAttractorData>(),
                    allocator: Allocator.Persistent,
                    logger: controller);

                aGo = new GameObject("Agent_A");
                bGo = new GameObject("Agent_B");

                var list = GetPrivateField<List<Transform>>(controller, "agentTransforms");
                list.Clear();
                list.Add(aGo.transform);
                list.Add(bGo.transform);

                // AFTER (valid)
                var positions = sim.Positions;
                var velocities = sim.Velocities;

                positions[0] = new float3(1f, 0f, 0f);
                velocities[0] = new float3(0f, 0f, 2f);

                positions[1] = new float3(-2f, 1f, 3f);
                velocities[1] = new float3(3f, 0f, 0f);


                SetPrivateField(controller, "simulation", sim);
                InvokePrivateMethod<object>(controller, "ApplySimulationToTransforms");

                AssertVector3Approx(aGo.transform.position, new Vector3(1f, 0f, 0f));
                AssertVector3Approx(bGo.transform.position, new Vector3(-2f, 1f, 3f));

                AssertForwardApprox(aGo.transform.forward, new Vector3(0f, 0f, 1f));
                AssertForwardApprox(bGo.transform.forward, new Vector3(1f, 0f, 0f));
            } finally {
                if (sim != null && sim.IsCreated) {
                    sim.Dispose();
                }

                if (behaviourSettings.IsCreated) {
                    behaviourSettings.Dispose();
                }

                if (bGo != null) UnityEngine.Object.DestroyImmediate(bGo);
                if (aGo != null) UnityEngine.Object.DestroyImmediate(aGo);
                if (controllerGo != null) UnityEngine.Object.DestroyImmediate(controllerGo);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value) {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Private field not found: {fieldName}");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName) {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Private field not found: {fieldName}");
            return (T)field.GetValue(target);
        }

        private static T InvokePrivateMethod<T>(object target, string methodName, params object[] args) {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Private method not found: {methodName}");
            return (T)method.Invoke(target, args);
        }

        private static void AssertVector3Approx(Vector3 actual, Vector3 expected, float eps = 1e-5f) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(eps));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(eps));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(eps));
        }

        private static void AssertForwardApprox(Vector3 actualForward, Vector3 expectedForward, float minDot = 0.999f) {
            Vector3 a = actualForward.normalized;
            Vector3 b = expectedForward.normalized;
            float dot = Vector3.Dot(a, b);
            Assert.That(dot, Is.GreaterThan(minDot), $"Forward mismatch. dot={dot}, actual={a}, expected={b}");
        }
    }
}