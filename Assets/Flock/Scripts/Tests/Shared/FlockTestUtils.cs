    using System;
    using System.Reflection;
    using Flock.Runtime;
    using Flock.Runtime.Data;
    using Unity.Collections;
    using Unity.Mathematics;
    using UnityEngine;
    using Random = Unity.Mathematics.Random;

    namespace Flock.Tests.Shared {
        internal static class FlockTestUtils {
            private const float SphereClampFactor = 0.999f;

            public static FlockMainSpawner CreateMainSpawner(string name, out GameObject go) {
                go = new GameObject(name);
                return go.AddComponent<FlockMainSpawner>();
            }

            public static FlockSpawnPoint CreateSpawnPoint(
                string name,
                Vector3 position,
                Quaternion rotation,
                FlockSpawnShape shape,
                float radius,
                Vector3 halfExtents,
                out GameObject go) {

                go = new GameObject(name);
                go.transform.SetPositionAndRotation(position, rotation);

                FlockSpawnPoint sp = go.AddComponent<FlockSpawnPoint>();
                SetPrivateField(sp, "shape", shape);
                SetPrivateField(sp, "radius", radius);
                SetPrivateField(sp, "halfExtents", halfExtents);

                return sp;
            }

            public static FishTypePreset CreateFishTypePreset(string name) {
                // Assumes FishTypePreset is a ScriptableObject (typical for presets).
                var preset = ScriptableObject.CreateInstance<FishTypePreset>();
                preset.name = name;
                return preset;
            }

            public static void ConfigureSpawner(
                FlockMainSpawner spawner,
                FlockMainSpawner.PointSpawnConfig[] pointSpawns,
                FlockMainSpawner.SeedSpawnConfig[] seedSpawns,
                uint globalSeed) {

                SetPrivateField(spawner, "pointSpawns", pointSpawns);
                SetPrivateField(spawner, "seedSpawns", seedSpawns);
                SetPrivateField(spawner, "globalSeed", globalSeed);
            }

            public static FlockMainSpawner.TypeCountEntry Entry(FishTypePreset preset, int count) {
                return new FlockMainSpawner.TypeCountEntry {
                    preset = preset,
                    count = count
                };
            }

            public static FlockMainSpawner.PointSpawnConfig PointSpawn(
                FlockSpawnPoint point,
                bool useSeed,
                uint seed,
                params FlockMainSpawner.TypeCountEntry[] types) {

                return new FlockMainSpawner.PointSpawnConfig {
                    point = point,
                    useSeed = useSeed,
                    seed = seed,
                    types = types
                };
            }

            public static FlockMainSpawner.SeedSpawnConfig SeedSpawn(
                bool useSeed,
                uint seed,
                params FlockMainSpawner.TypeCountEntry[] types) {

                return new FlockMainSpawner.SeedSpawnConfig {
                    useSeed = useSeed,
                    seed = seed,
                    types = types
                };
            }

            public static FlockEnvironmentData MakeBoxEnvironment(float3 center, float3 extents) {
                var env = default(FlockEnvironmentData);
                env.BoundsType = FlockBoundsType.Box;
                env.BoundsCenter = center;
                env.BoundsExtents = extents;
                env.BoundsRadius = math.length(extents); // not used by box clamp, but sane.
                return env;
            }

            public static FlockEnvironmentData MakeSphereEnvironment(float3 center, float radius) {
                var env = default(FlockEnvironmentData);
                env.BoundsType = FlockBoundsType.Sphere;
                env.BoundsCenter = center;
                env.BoundsRadius = math.max(radius, 0.0001f);
                env.BoundsExtents = new float3(env.BoundsRadius);
                return env;
            }

            public static float3 ClampToBounds(float3 position, in FlockEnvironmentData environment) {
                float3 boundsCenter = environment.BoundsCenter;

                if (environment.BoundsType == FlockBoundsType.Sphere && environment.BoundsRadius > 0f) {
                    float radius = environment.BoundsRadius;
                    float3 offset = position - boundsCenter;
                    float distanceSquared = math.lengthsq(offset);

                    if (distanceSquared > radius * radius) {
                        float distance = math.sqrt(distanceSquared);
                        float3 direction = offset / math.max(distance, 1e-4f);
                        return boundsCenter + direction * radius * SphereClampFactor;
                    }

                    return position;
                }

                float3 extents = environment.BoundsExtents;
                float3 min = boundsCenter - extents;
                float3 max = boundsCenter + extents;

                return math.clamp(position, min, max);
            }

            public static float3 SampleSpawnPointDeterministic(FlockSpawnPoint point, uint seed) {
                uint s = seed == 0u ? 1u : seed;
                var r = new Random(s);
                return point.SamplePosition(ref r);
            }

            public static void DestroyImmediateSafe(UnityEngine.Object obj) {
                if (obj != null) {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }

            public static void DisposeIfCreated<T>(ref NativeArray<T> array) where T : struct {
                if (array.IsCreated) {
                    array.Dispose();
                    array = default;
                }
            }

            private static void SetPrivateField<T>(object target, string fieldName, T value) {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo field = target.GetType().GetField(fieldName, flags);

                if (field == null) {
                    throw new MissingFieldException(
                        target.GetType().FullName,
                        fieldName);
                }

                field.SetValue(target, value);
            }
        }
    }

