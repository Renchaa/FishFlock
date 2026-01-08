using Flock.Scripts.Build.Agents.Fish.Profiles;
using Flock.Tests.Shared;
    using NUnit.Framework;
    using UnityEngine;
    namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockMainSpawner {
        public sealed class FlockMainSpawner_BuildAgentBehaviourIds_SumsPointAndSeedCounts_Test {
            [Test]
            public void FlockMainSpawner_BuildAgentBehaviourIds_SumsPointAndSeedCounts_Test_Run() {
                var spawner = FlockTestUtils.CreateMainSpawner("Spawner", out GameObject spawnerGo);

                var presetA = FlockTestUtils.CreateFishTypePreset("A");
                var presetB = FlockTestUtils.CreateFishTypePreset("B");
                FishTypePreset[] fishTypes = { presetA, presetB };

                var pointConfig = FlockTestUtils.PointSpawn(
                    point: null,
                    useSeed: false,
                    seed: 0u,
                    FlockTestUtils.Entry(presetA, 2),
                    FlockTestUtils.Entry(presetB, 1));

                var seedConfig = FlockTestUtils.SeedSpawn(
                    useSeed: true,
                    seed: 123u,
                    FlockTestUtils.Entry(presetB, 3));

                FlockTestUtils.ConfigureSpawner(
                    spawner,
                    pointSpawns: new[] { pointConfig },
                    seedSpawns: new[] { seedConfig },
                    globalSeed: 1u);

                int[] result = spawner.BuildAgentBehaviourIds(fishTypes);

                // Totals: A=2, B=4 => [0,0,1,1,1,1]
                Assert.NotNull(result);
                Assert.AreEqual(6, result.Length);

                Assert.AreEqual(0, result[0]);
                Assert.AreEqual(0, result[1]);
                Assert.AreEqual(1, result[2]);
                Assert.AreEqual(1, result[3]);
                Assert.AreEqual(1, result[4]);
                Assert.AreEqual(1, result[5]);

                FlockTestUtils.DestroyImmediateSafe(spawnerGo);
                FlockTestUtils.DestroyImmediateSafe(presetA);
                FlockTestUtils.DestroyImmediateSafe(presetB);
            }
        }
    }