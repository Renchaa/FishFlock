// File: Assets/Flock/Tests/Runtime/Spawning/FlockMainSpawner_AssignInitialPositions_BoxSpawn_ClampsToBoxBounds_Test.cs
    using Flock.Runtime;
    using Flock.Runtime.Data;
    using Flock.Tests.Shared;
    using NUnit.Framework;
    using Unity.Collections;
    using Unity.Mathematics;
    using UnityEngine;
    namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockMainSpawner {
        public sealed class FlockMainSpawner_AssignInitialPositions_BoxShape_ClampsToBoxBounds_Test {
            [Test]
            public void FlockMainSpawner_AssignInitialPositions_BoxSpawn_ClampsToBoxBounds_Test_Run() {
                var spawner = FlockTestUtils.CreateMainSpawner("Spawner", out GameObject spawnerGo);

                var presetA = FlockTestUtils.CreateFishTypePreset("A");
                FishTypePreset[] fishTypes = { presetA };

                // Center is far outside +X so clamp must occur; Y/Z remain near center and are used to validate sampling path.
                var boxPoint = FlockTestUtils.CreateSpawnPoint(
                    name: "BoxSpawn",
                    position: new Vector3(10f, 0.25f, -0.25f),
                    rotation: Quaternion.Euler(0f, 90f, 0f),
                    shape: FlockSpawnShape.Box,
                    radius: 0f,
                    halfExtents: new Vector3(0.5f, 0.5f, 0.5f),
                    out GameObject boxGo);

                const uint seed = 123u;

                var pointConfig = FlockTestUtils.PointSpawn(
                    point: boxPoint,
                    useSeed: true,
                    seed: seed,
                    FlockTestUtils.Entry(presetA, 1));

                FlockTestUtils.ConfigureSpawner(
                    spawner,
                    pointSpawns: new[] { pointConfig },
                    seedSpawns: null,
                    globalSeed: 1u);

                int[] agentBehaviourIds = { 0 };

                FlockEnvironmentData env = FlockTestUtils.MakeBoxEnvironment(
                    center: new float3(0f, 0f, 0f),
                    extents: new float3(1f, 1f, 1f));

                float3 raw = FlockTestUtils.SampleSpawnPointDeterministic(boxPoint, seed);
                float3 expected = FlockTestUtils.ClampToBounds(raw, env);

                var positions = new NativeArray<float3>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                spawner.AssignInitialPositions(env, fishTypes, agentBehaviourIds, positions);

                float3 actual = positions[0];

                Assert.AreEqual(expected.x, actual.x, 1e-6f);
                Assert.AreEqual(expected.y, actual.y, 1e-6f);
                Assert.AreEqual(expected.z, actual.z, 1e-6f);

                FlockTestUtils.DisposeIfCreated(ref positions);
                FlockTestUtils.DestroyImmediateSafe(spawnerGo);
                FlockTestUtils.DestroyImmediateSafe(boxGo);
                FlockTestUtils.DestroyImmediateSafe(presetA);
            }
        }
    }