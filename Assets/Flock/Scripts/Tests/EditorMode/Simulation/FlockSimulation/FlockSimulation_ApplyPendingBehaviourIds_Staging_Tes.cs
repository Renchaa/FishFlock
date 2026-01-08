// Assets/Flock/Editor/Tests/EditorMode/Simulation/FlockSimulation_ApplyPendingBehaviourIds_Test.cs
#if UNITY_EDITOR
using System;
using System.Reflection;
using Flock.Scripts.Build.Agents.Fish.Data;
using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockSimulation {

    public sealed class FlockSimulation_ApplyPendingBehaviourIds_Staging_Test {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ApplyPendingBehaviourIds_CopiesToNativeAndResetsStagingFlags() {
            var sim = new Build.Core.Simulation.Runtime.PartialFlockSimulation.FlockSimulation();

            var env = CreateEnvironment(
                gridOrigin: new float3(0, 0, 0),
                gridResolution: new int3(8, 8, 8),
                cellSize: 1.0f,
                boundsCenter: new float3(0, 0, 0),
                boundsExtents: new float3(10, 10, 10));

            var behaviourSettings = new NativeArray<FlockBehaviourSettings>(1, Allocator.Persistent);
            behaviourSettings[0] = CreateBehaviourSettings(maxSpeed: 1.0f, maxAccel: 1.0f, neighbourRadius: 1.0f);

            try {
                sim.Initialize(
                    agentCount: 8,
                    environment: env,
                    behaviourSettings: behaviourSettings,
                    obstaclesSource: Array.Empty<FlockObstacleData>(),
                    attractorsSource: Array.Empty<FlockAttractorData>(),
                    allocator: Allocator.Persistent,
                    logger: null);

                int[] pending = { 0, 1, 1, 0, 1, 0, 0, 1 };

                SetPrivateField(sim, "pendingBehaviourIdsDirty", true);
                SetPrivateField(sim, "pendingBehaviourIdsCount", pending.Length);
                SetPrivateField(sim, "pendingBehaviourIdsManaged", pending);

                JobHandle h = (JobHandle)InvokePrivate(sim, "ScheduleApplyPendingChanges", new object[] { default(JobHandle) });
                h.Complete();

                NativeArray<int> behaviourIds = GetPrivateField<NativeArray<int>>(sim, "behaviourIds");
                Assert.That(behaviourIds.IsCreated, Is.True);

                for (int i = 0; i < pending.Length; i++) {
                    Assert.That(behaviourIds[i], Is.EqualTo(pending[i]), $"behaviourIds[{i}] mismatch");
                }

                Assert.That(GetPrivateField<bool>(sim, "pendingBehaviourIdsDirty"), Is.False);
                Assert.That(GetPrivateField<int>(sim, "pendingBehaviourIdsCount"), Is.EqualTo(0));
            } finally {
                if (behaviourSettings.IsCreated) behaviourSettings.Dispose();
                sim.Dispose();
            }
        }

        // ----------------- creation helpers (reflection-safe) -----------------

        private static FlockEnvironmentData CreateEnvironment(
            float3 gridOrigin,
            int3 gridResolution,
            float cellSize,
            float3 boundsCenter,
            float3 boundsExtents) {

            FlockEnvironmentData env = default;
            env = SetStructMember(env, "GridOrigin", gridOrigin);
            env = SetStructMember(env, "GridResolution", gridResolution);
            env = SetStructMember(env, "CellSize", cellSize);
            env = SetStructMember(env, "BoundsCenter", boundsCenter);
            env = SetStructMember(env, "BoundsExtents", boundsExtents);
            return env;
        }

        private static FlockBehaviourSettings CreateBehaviourSettings(float maxSpeed, float maxAccel, float neighbourRadius) {
            FlockBehaviourSettings s = default;
            s = SetStructMember(s, "MaxSpeed", maxSpeed);
            s = SetStructMember(s, "MaxAcceleration", maxAccel);
            s = SetStructMember(s, "NeighbourRadius", neighbourRadius);
            return s;
        }

        private static T SetStructMember<T>(T value, string name, object memberValue) where T : struct {
            object boxed = value;
            Type t = typeof(T);

            var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) {
                fi.SetValue(boxed, memberValue);
                return (T)boxed;
            }

            var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.CanWrite) {
                pi.SetValue(boxed, memberValue, null);
                return (T)boxed;
            }

            Assert.Fail($"Missing settable member '{name}' on {t.Name}.");
            return value;
        }

        // ----------------- reflection helpers -----------------

        private static object InvokePrivate(object target, string method, object[] args) {
            var mi = target.GetType().GetMethod(method, BF);
            Assert.That(mi, Is.Not.Null, $"Missing method {target.GetType().Name}.{method}");
            return mi.Invoke(target, args);
        }

        private static T GetPrivateField<T>(object target, string field) {
            var fi = target.GetType().GetField(field, BF);
            Assert.That(fi, Is.Not.Null, $"Missing field {target.GetType().Name}.{field}");
            return (T)fi.GetValue(target);
        }

        private static void SetPrivateField(object target, string field, object value) {
            var fi = target.GetType().GetField(field, BF);
            Assert.That(fi, Is.Not.Null, $"Missing field {target.GetType().Name}.{field}");
            fi.SetValue(target, value);
        }
    }
}
#endif
