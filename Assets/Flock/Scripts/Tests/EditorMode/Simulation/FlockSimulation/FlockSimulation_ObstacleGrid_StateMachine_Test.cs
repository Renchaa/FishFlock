// Assets/Flock/Editor/Tests/EditorMode/Simulation/FlockSimulation_ObstacleGridDirtyStateMachine_Test.cs
#if UNITY_EDITOR
using System;
using System.Reflection;
using Flock.Runtime;
using Flock.Runtime.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockSimulation {

    public sealed class FlockSimulation_ObstacleGrid_StateMachine_Test {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ObstacleGrid_RebuildOnlyWhenDirty_ThenBecomesClean() {
            var sim = new Runtime.FlockSimulation();

            var env = CreateEnvironment(
                new float3(0, 0, 0),
                new int3(8, 8, 8),
                1.0f,
                new float3(0, 0, 0),
                new float3(10, 10, 10));

            var behaviourSettings = new NativeArray<FlockBehaviourSettings>(1, Allocator.Persistent);
            behaviourSettings[0] = CreateBehaviourSettings(1.0f, 1.0f, 1.0f);

            try {
                sim.Initialize(
                    agentCount: 1,
                    environment: env,
                    behaviourSettings: behaviourSettings,
                    obstaclesSource: new[] { default(FlockObstacleData) }, // ensure obstacle buffers exist
                    attractorsSource: Array.Empty<FlockAttractorData>(),
                    allocator: Allocator.Persistent,
                    logger: null);

                bool useObstacleAvoidance = (bool)InvokePrivate(sim, "ShouldUseObstacleAvoidance", Array.Empty<object>());
                Assert.That(useObstacleAvoidance, Is.True);

                // Force dirty -> should schedule rebuild and immediately mark clean.
                SetPrivateField(sim, "obstacleGridDirty", true);

                JobHandle deps = default;
                JobHandle h = (JobHandle)InvokePrivate(sim, "ScheduleRebuildObstacleGridIfDirty", new object[] { deps, useObstacleAvoidance });

                Assert.That(GetPrivateField<bool>(sim, "obstacleGridDirty"), Is.False, "Should clear dirty after scheduling rebuild");
                h.Complete();

                // Already clean -> should return deps unchanged.
                JobHandle h2 = (JobHandle)InvokePrivate(sim, "ScheduleRebuildObstacleGridIfDirty", new object[] { deps, useObstacleAvoidance });
                Assert.That(h2, Is.EqualTo(deps));
                Assert.That(GetPrivateField<bool>(sim, "obstacleGridDirty"), Is.False);
            } finally {
                if (behaviourSettings.IsCreated) behaviourSettings.Dispose();
                sim.Dispose();
            }
        }

        private static FlockEnvironmentData CreateEnvironment(float3 o, int3 r, float s, float3 c, float3 e) {
            FlockEnvironmentData env = default;
            env = SetStructMember(env, "GridOrigin", o);
            env = SetStructMember(env, "GridResolution", r);
            env = SetStructMember(env, "CellSize", s);
            env = SetStructMember(env, "BoundsCenter", c);
            env = SetStructMember(env, "BoundsExtents", e);
            return env;
        }

        private static FlockBehaviourSettings CreateBehaviourSettings(float ms, float ma, float nr) {
            FlockBehaviourSettings bs = default;
            bs = SetStructMember(bs, "MaxSpeed", ms);
            bs = SetStructMember(bs, "MaxAcceleration", ma);
            bs = SetStructMember(bs, "NeighbourRadius", nr);
            return bs;
        }

        private static T SetStructMember<T>(T value, string name, object memberValue) where T : struct {
            object boxed = value;
            Type t = typeof(T);

            var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) { fi.SetValue(boxed, memberValue); return (T)boxed; }

            var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.CanWrite) { pi.SetValue(boxed, memberValue, null); return (T)boxed; }

            Assert.Fail($"Missing settable member '{name}' on {t.Name}.");
            return value;
        }

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
