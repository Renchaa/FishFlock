using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;

using Unity.Jobs;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Collections;

namespace Flock.Scripts.Tests.EditorMode.Jobs.BuildObstacleGridJob
{
    public sealed class BuildObstacleGridJob_OutsideGrid_DoesNotStamp_Test
    {
        [Test]
        public void BuildObstacleGridJob_OutsideGrid_DoesNotStamp_Test_Run()
        {
            // Grid
            float3 origin = new float3(0f, 0f, 0f);
            int3 resolution = new int3(4, 4, 4);
            float cellSize = 1f;

            // Obstacle far outside: max.x < gridMin.x => early out, no stamps.
            var obstacles = new NativeArray<FlockObstacleData>(1, Allocator.TempJob);
            obstacles[0] = new FlockObstacleData
            {
                Position = new float3(-10f, 0f, 0f),
                Radius = 1.0f
            };

            var map = new NativeParallelMultiHashMap<int, int>(16, Allocator.TempJob);

            try
            {
                var job = new Build.Infrastructure.Grid.Jobs.BuildObstacleGridJob
                {
                    Obstacles = obstacles,
                    GridOrigin = origin,
                    GridResolution = resolution,
                    CellSize = cellSize,
                    CellToObstacles = map.AsParallelWriter()
                };

                JobHandle h = job.Schedule(obstacles.Length, 1, default(JobHandle));
                h.Complete();

                Assert.That(map.Count(), Is.EqualTo(0), "Map should remain empty when obstacle is fully outside grid.");
            }
            finally
            {
                if (map.IsCreated) map.Dispose();
                if (obstacles.IsCreated) obstacles.Dispose();
            }
        }
    }
}
