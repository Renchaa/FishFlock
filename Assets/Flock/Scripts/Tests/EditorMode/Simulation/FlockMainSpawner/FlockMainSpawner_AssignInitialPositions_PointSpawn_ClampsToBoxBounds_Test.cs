    using Flock.Scripts.Build.Agents.Fish.Profiles;
using Flock.Scripts.Build.Core.Simulation.Runtime.Spawn;
using Flock.Scripts.Build.Influence.Environment.Data;
using Flock.Tests.Shared;
    using NUnit.Framework;
    using Unity.Collections;
    using Unity.Mathematics;
    using UnityEngine;
    namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockMainSpawner {
        public sealed class FlockMainSpawner_AssignInitialPositions_PointSpawn_ClampsToBoxBounds_Test {
            [Test]
            public void FlockMainSpawner_AssignInitialPositions_PointSpawn_ClampsToBoxBounds_Test_Run() {
                var spawner = FlockTestUtils.CreateMainSpawner("Spawner", out GameObject spawnerGo);

                var presetA = FlockTestUtils.CreateFishTypePreset("A");
                FishTypePreset[] fishTypes = { presetA };

                var point = FlockTestUtils.CreateSpawnPoint(
                    name: "PointSpawn",
                    position: new Vector3(10f, 0.5f, -10f),
                    rotation: Quaternion.identity,
                    shape: FlockSpawnShape.Point,
                    radius: 0f,
                    halfExtents: Vector3.zero,
                    out GameObject pointGo);

                var pointConfig = FlockTestUtils.PointSpawn(
                    point: point,
                    useSeed: false,
                    seed: 0u,
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

                var positions = new NativeArray<float3>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                spawner.AssignInitialPositions(env, fishTypes, agentBehaviourIds, positions);

                float3 expected = FlockTestUtils.ClampToBounds((float3)pointGo.transform.position, env);
                float3 actual = positions[0];

                Assert.AreEqual(expected.x, actual.x, 1e-6f);
                Assert.AreEqual(expected.y, actual.y, 1e-6f);
                Assert.AreEqual(expected.z, actual.z, 1e-6f);

                FlockTestUtils.DisposeIfCreated(ref positions);
                FlockTestUtils.DestroyImmediateSafe(spawnerGo);
                FlockTestUtils.DestroyImmediateSafe(pointGo);
                FlockTestUtils.DestroyImmediateSafe(presetA);
            }
        }
    }