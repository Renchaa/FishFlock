// Assets/Flock/Editor/Tests/EditorMode/Jobs/BuildObstacleGridJob_RadiusNonPositive_UsesHalfCellMinRadius_Test.cs

#if UNITY_EDITOR
using Flock.Runtime.Data;
using Flock.Runtime.Jobs;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace Flock.Scripts.Tests.EditorMode.Jobs.BuildObstacleGridJob {

    public sealed class BuildObstacleGridJob_RadiusNonPositive_UsesHalfCellMinRadius_Test {
        [Test]
        public void BuildObstacleGridJob_RadiusNonPositive_UsesHalfCellMinRadius_Test_Run() {
            // Grid
            float3 origin = new float3(0f, 0f, 0f);
            int3 resolution = new int3(8, 8, 8);
            float cellSize = 1f;

            // Obstacle: negative radius must clamp to 0, then bump to half-cell => 0.5
            // This will span EXACTLY 2 cells per axis => 2*2*2 = 8 stamped cells (when not near edges).
            var obstacles = new NativeArray<FlockObstacleData>(1, Allocator.TempJob);
            obstacles[0] = new FlockObstacleData {
                Position = new float3(3.2f, 3.2f, 3.2f),
                Radius = -1.0f
            };

            // Expected cells: x/y/z in [2..3]
            int expectedMin = 2;
            int expectedMax = 3;
            int expectedCellCount = 8;

            var map = new NativeParallelMultiHashMap<int, int>(expectedCellCount * 2, Allocator.TempJob);

            try {
                var job = new Runtime.Jobs.BuildObstacleGridJob {
                    Obstacles = obstacles,
                    GridOrigin = origin,
                    GridResolution = resolution,
                    CellSize = cellSize,
                    CellToObstacles = map.AsParallelWriter()
                };

                JobHandle h = job.Schedule(obstacles.Length, 1, default(JobHandle));
                h.Complete();

                Assert.That(map.Count(), Is.EqualTo(expectedCellCount), "Unexpected total stamped pair count.");

                for (int z = expectedMin; z <= expectedMax; z += 1) {
                    for (int y = expectedMin; y <= expectedMax; y += 1) {
                        for (int x = expectedMin; x <= expectedMax; x += 1) {
                            int cellIndex = CellIndex(x, y, z, resolution);
                            AssertCellContainsOnlyObstacle(map, cellIndex, obstacleIndex: 0);
                        }
                    }
                }
            } finally {
                if (map.IsCreated) map.Dispose();
                if (obstacles.IsCreated) obstacles.Dispose();
            }
        }

        private static int CellIndex(int x, int y, int z, int3 resolution) {
            int layerSize = resolution.x * resolution.y;
            return x + y * resolution.x + z * layerSize;
        }

        private static void AssertCellContainsOnlyObstacle(
            NativeParallelMultiHashMap<int, int> map,
            int cellIndex,
            int obstacleIndex) {

            NativeParallelMultiHashMapIterator<int> it;
            int value;

            bool found = map.TryGetFirstValue(cellIndex, out value, out it);
            Assert.That(found, Is.True, "Expected stamped cell was missing from map.");

            int matches = 0;
            do {
                if (value == obstacleIndex) {
                    matches += 1;
                }
            } while (map.TryGetNextValue(out value, ref it));

            Assert.That(matches, Is.EqualTo(1), "Cell should contain exactly one entry for the obstacle.");
        }
    }
}
#endif
