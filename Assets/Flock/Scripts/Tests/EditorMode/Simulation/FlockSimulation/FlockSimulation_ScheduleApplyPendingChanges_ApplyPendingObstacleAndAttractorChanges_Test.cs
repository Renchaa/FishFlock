// Assets/Flock/Editor/Tests/EditorMode/Simulation/FlockSimulation_ApplyPendingObstacleAndAttractorChanges_Test.cs
#if UNITY_EDITOR
using System;
using System.Reflection;
using Flock.Runtime;
using Flock.Runtime.Data;
using Flock.Runtime.Jobs;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockSimulation {

    public sealed class FlockSimulation_ScheduleApplyPendingChanges_ApplyPendingObstacleAndAttractorChanges_Test {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ApplyPendingObstacleAndAttractorChanges_MutatesNativeDataAndSetsDirtyFlags() {
            var sim = new Runtime.FlockSimulation();

            var env = CreateEnvironment(
                gridOrigin: new float3(0, 0, 0),
                gridResolution: new int3(8, 8, 8),
                cellSize: 1.0f,
                boundsCenter: new float3(0, 0, 0),
                boundsExtents: new float3(10, 10, 10));

            var behaviourSettings = new NativeArray<FlockBehaviourSettings>(1, Allocator.Persistent);
            behaviourSettings[0] = CreateBehaviourSettings(maxSpeed: 1.0f, maxAccel: 1.0f, neighbourRadius: 1.0f);

            // Need at least one obstacle/attractor allocated so apply path runs.
            FlockObstacleData[] obstaclesSrc = { default };
            FlockAttractorData[] attractorsSrc = { default };

            try {
                sim.Initialize(
                    agentCount: 1,
                    environment: env,
                    behaviourSettings: behaviourSettings,
                    obstaclesSource: obstaclesSrc,
                    attractorsSource: attractorsSrc,
                    allocator: Allocator.Persistent,
                    logger: null);

                // Force clean first so we can verify "apply" sets dirty.
                SetPrivateField(sim, "obstacleGridDirty", false);
                SetPrivateField(sim, "attractorGridDirty", false);

                // Capture before values.
                NativeArray<FlockObstacleData> obstacles = GetPrivateField<NativeArray<FlockObstacleData>>(sim, "obstacles");
                NativeArray<FlockAttractorData> attractors = GetPrivateField<NativeArray<FlockAttractorData>>(sim, "attractors");
                Assert.That(obstacles.IsCreated && obstacles.Length > 0, Is.True);
                Assert.That(attractors.IsCreated && attractors.Length > 0, Is.True);

                FlockObstacleData beforeObstacle = obstacles[0];
                FlockAttractorData beforeAttractor = attractors[0];

                // Stage ONE obstacle change + ONE attractor change into the real pending lists.
                var pendingObstacles = GetPrivateField<object>(sim, "pendingObstacleChanges"); // List<IndexedObstacleChange>
                var pendingAttractors = GetPrivateField<object>(sim, "pendingAttractorChanges"); // List<IndexedAttractorChange>

                // Create distinct payloads.
                FlockObstacleData newObstacle = CreateDistinctStruct<FlockObstacleData>(seed: 10);
                FlockAttractorData newAttractor = CreateDistinctStruct<FlockAttractorData>(seed: 20);

                // Build indexed changes (field names are discovered via reflection so this doesn't break if you rename them).
                object obstacleChange = CreateIndexedChange(typeof(IndexedObstacleChange), index: 0, payload: newObstacle);
                object attractorChange = CreateIndexedChange(typeof(IndexedAttractorChange), index: 0, payload: newAttractor);

                AddToList(pendingObstacles, obstacleChange);
                AddToList(pendingAttractors, attractorChange);

                // Act
                JobHandle h = (JobHandle)InvokePrivate(sim, "ScheduleApplyPendingChanges", new object[] { default(JobHandle) });
                h.Complete();

                // Assert: pending lists cleared.
                Assert.That(GetListCount(pendingObstacles), Is.EqualTo(0), "pendingObstacleChanges should be cleared");
                Assert.That(GetListCount(pendingAttractors), Is.EqualTo(0), "pendingAttractorChanges should be cleared");

                // Assert: dirty flags set.
                Assert.That(GetPrivateField<bool>(sim, "obstacleGridDirty"), Is.True, "obstacleGridDirty should be set true after applying obstacle changes");
                Assert.That(GetPrivateField<bool>(sim, "attractorGridDirty"), Is.True, "attractorGridDirty should be set true after applying attractor changes");

                // Assert: native arrays mutated (actual state mutation).
                FlockObstacleData afterObstacle = obstacles[0];
                FlockAttractorData afterAttractor = attractors[0];

                Assert.That(afterObstacle, Is.Not.EqualTo(beforeObstacle), "Obstacle[0] should change after apply");
                Assert.That(afterAttractor, Is.Not.EqualTo(beforeAttractor), "Attractor[0] should change after apply");
            } finally {
                if (behaviourSettings.IsCreated) behaviourSettings.Dispose();
                sim.Dispose();
            }
        }

        // ----------------- indexed-change builder (reflection-safe) -----------------

        private static object CreateIndexedChange(Type changeType, int index, object payload) {
            object boxed = Activator.CreateInstance(changeType);

            // Pick an int field for index (prefer name contains "index")
            FieldInfo indexField = FindBestField(changeType, typeof(int), "index");
            Assert.That(indexField, Is.Not.Null, $"No int index field found on {changeType.Name}");
            indexField.SetValue(boxed, index);

            // Pick a payload field matching payload type (exact match preferred)
            FieldInfo payloadField = FindPayloadField(changeType, payload.GetType());
            Assert.That(payloadField, Is.Not.Null, $"No payload field of type {payload.GetType().Name} found on {changeType.Name}");
            payloadField.SetValue(boxed, payload);

            return boxed;
        }

        private static FieldInfo FindBestField(Type t, Type fieldType, string nameContainsLower) {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = t.GetFields(flags);

            for (int i = 0; i < fields.Length; i++) {
                if (fields[i].FieldType == fieldType && fields[i].Name.ToLowerInvariant().Contains(nameContainsLower)) {
                    return fields[i];
                }
            }

            for (int i = 0; i < fields.Length; i++) {
                if (fields[i].FieldType == fieldType) {
                    return fields[i];
                }
            }

            return null;
        }

        private static FieldInfo FindPayloadField(Type changeType, Type payloadType) {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = changeType.GetFields(flags);

            for (int i = 0; i < fields.Length; i++) {
                if (fields[i].FieldType == payloadType) return fields[i];
            }

            // fallback: assignable (in case payload is a base type, unlikely but safe)
            for (int i = 0; i < fields.Length; i++) {
                if (fields[i].FieldType.IsAssignableFrom(payloadType)) return fields[i];
            }

            return null;
        }

        private static void AddToList(object list, object item) {
            var mi = list.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(mi, Is.Not.Null, "List.Add not found");
            mi.Invoke(list, new object[] { item });
        }

        private static int GetListCount(object list) {
            var pi = list.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(pi, Is.Not.Null, "List.Count not found");
            return (int)pi.GetValue(list, null);
        }

        // ----------------- distinct payload builder -----------------

        private static T CreateDistinctStruct<T>(int seed) where T : struct {
            object boxed = Activator.CreateInstance(typeof(T));
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = typeof(T).GetFields(flags);

            for (int i = 0; i < fields.Length; i++) {
                FieldInfo f = fields[i];
                Type ft = f.FieldType;

                if (ft == typeof(float)) f.SetValue(boxed, 10f + seed);
                else if (ft == typeof(int)) f.SetValue(boxed, seed);
                else if (ft == typeof(uint)) f.SetValue(boxed, (uint)(seed + 1));
                else if (ft == typeof(byte)) f.SetValue(boxed, (byte)(seed % 255));
                else if (ft == typeof(bool)) f.SetValue(boxed, (seed & 1) == 0);
                else if (ft == typeof(float3)) f.SetValue(boxed, new float3(seed + 1, seed + 2, seed + 3));
                else if (ft == typeof(int3)) f.SetValue(boxed, new int3(seed + 1, seed + 2, seed + 3));
            }

            return (T)boxed;
        }

        // ----------------- environment / behaviour settings -----------------

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
