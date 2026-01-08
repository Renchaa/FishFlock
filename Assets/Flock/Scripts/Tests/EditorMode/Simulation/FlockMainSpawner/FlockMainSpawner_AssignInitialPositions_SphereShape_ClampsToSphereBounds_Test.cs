using Flock.Scripts.Build.Agents.Fish.Profiles;
using Flock.Scripts.Build.Core.Simulation.Runtime.Spawn;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Tests.Shared;
    using NUnit.Framework;
    using Unity.Collections;
    using Unity.Mathematics;
    using UnityEngine;
    namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockMainSpawner {
        public sealed class FlockMainSpawner_AssignInitialPositions_SphereSpawn_ClampsToSphereBounds_Test {
            [Test]
            public void FlockMainSpawner_AssignInitialPositions_SphereSpawn_ClampsToSphereBounds_Test_Run() {
                var spawner = FlockTestUtils.CreateMainSpawner("Spawner", out GameObject spawnerGo);

                var presetA = FlockTestUtils.CreateFishTypePreset("A");
                FishTypePreset[] fishTypes = { presetA };

                // Spawn sphere centered outside the environment sphere so clamping path is exercised.
                var spherePoint = FlockTestUtils.CreateSpawnPoint(
                    name: "SphereSpawn",
                    position: new Vector3(20f, 0f, 0f),
                    rotation: Quaternion.identity,
                    shape: FlockSpawnShape.Sphere,
                    radius: 5f,
                    halfExtents: Vector3.zero,
                    out GameObject sphereGo);

                const uint seed = 777u;

                var pointConfig = FlockTestUtils.PointSpawn(
                    point: spherePoint,
                    useSeed: true,
                    seed: seed,
                    FlockTestUtils.Entry(presetA, 1));

                FlockTestUtils.ConfigureSpawner(
                    spawner,
                    pointSpawns: new[] { pointConfig },
                    seedSpawns: null,
                    globalSeed: 1u);

                int[] agentBehaviourIds = { 0 };

                FlockEnvironmentData env = FlockTestUtils.MakeSphereEnvironment(
                    center: new float3(0f, 0f, 0f),
                    radius: 10f);

                float3 raw = FlockTestUtils.SampleSpawnPointDeterministic(spherePoint, seed);
                float3 expected = FlockTestUtils.ClampToBounds(raw, env);

                var positions = new NativeArray<float3>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                spawner.AssignInitialPositions(env, fishTypes, agentBehaviourIds, positions);

                float3 actual = positions[0];

                Assert.AreEqual(expected.x, actual.x, 1e-6f);
                Assert.AreEqual(expected.y, actual.y, 1e-6f);
                Assert.AreEqual(expected.z, actual.z, 1e-6f);

                // Additionally enforce "inside sphere" invariant.
                float distSq = math.lengthsq(actual - env.BoundsCenter);
                Assert.LessOrEqual(distSq, env.BoundsRadius * env.BoundsRadius);

                FlockTestUtils.DisposeIfCreated(ref positions);
                FlockTestUtils.DestroyImmediateSafe(spawnerGo);
                FlockTestUtils.DestroyImmediateSafe(sphereGo);
                FlockTestUtils.DestroyImmediateSafe(presetA);
            }
        }
    }