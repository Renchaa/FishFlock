// File: Assets/Flock/Tests/Runtime/Spawning/FlockMainSpawner_AssignInitialPositions_IsDeterministicForExplicitSeed_Test.cs
    using Flock.Runtime;
    using Flock.Runtime.Data;
    using Flock.Tests.Shared;
    using NUnit.Framework;
    using Unity.Collections;
    using Unity.Mathematics;
    using UnityEngine;
    namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockMainSpawner {
        public sealed class FlockMainSpawner_AssignInitialPositions_IsDeterministicForExplicitSeed_Test {
            [Test]
            public void FlockMainSpawner_AssignInitialPositions_IsDeterministicForExplicitSeed_Test_Run() {
                var spawner = FlockTestUtils.CreateMainSpawner("Spawner", out GameObject spawnerGo);

                var presetA = FlockTestUtils.CreateFishTypePreset("A");
                FishTypePreset[] fishTypes = { presetA };

                const int agentCount = 8;
                int[] agentBehaviourIds = new int[agentCount];
                for (int i = 0; i < agentCount; i += 1) {
                    agentBehaviourIds[i] = 0;
                }

                var seedConfig = FlockTestUtils.SeedSpawn(
                    useSeed: true,
                    seed: 123u,
                    FlockTestUtils.Entry(presetA, agentCount));

                FlockTestUtils.ConfigureSpawner(
                    spawner,
                    pointSpawns: null,
                    seedSpawns: new[] { seedConfig },
                    globalSeed: 999u);

                FlockEnvironmentData env = FlockTestUtils.MakeBoxEnvironment(
                    center: new float3(0f, 0f, 0f),
                    extents: new float3(5f, 5f, 5f));

                var positionsA = new NativeArray<float3>(agentCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var positionsB = new NativeArray<float3>(agentCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                spawner.AssignInitialPositions(env, fishTypes, agentBehaviourIds, positionsA);
                spawner.AssignInitialPositions(env, fishTypes, agentBehaviourIds, positionsB);

                for (int i = 0; i < agentCount; i += 1) {
                    float3 a = positionsA[i];
                    float3 b = positionsB[i];

                    Assert.AreEqual(a.x, b.x, 0f);
                    Assert.AreEqual(a.y, b.y, 0f);
                    Assert.AreEqual(a.z, b.z, 0f);
                }

                FlockTestUtils.DisposeIfCreated(ref positionsA);
                FlockTestUtils.DisposeIfCreated(ref positionsB);
                FlockTestUtils.DestroyImmediateSafe(spawnerGo);
                FlockTestUtils.DestroyImmediateSafe(presetA);
            }
        }
    }