namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController {
    using Flock.Runtime.Data;
    using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;
    using Flock.Scripts.Build.Influence.Environment.Obstacles.Runtime;
    using System;
    using UnityEngine;

    /**
    * <summary>
    * Runtime obstacle ingestion and per-frame dynamic obstacle synchronization for the flock simulation.
    * </summary>
    */
    public sealed partial class FlockController {
        private int[] dynamicObstacleIndices;

        private FlockObstacleData[] BuildObstacleData() {
            int staticCount = staticObstacles != null ? staticObstacles.Length : 0;
            int dynamicCount = dynamicObstacles != null ? dynamicObstacles.Length : 0;
            int totalCount = staticCount + dynamicCount;

            if (totalCount == 0) {
                dynamicObstacleIndices = Array.Empty<int>();
                return Array.Empty<FlockObstacleData>();
            }

            FlockObstacleData[] data = new FlockObstacleData[totalCount];

            int writeIndex = 0;
            writeIndex = WriteStaticObstacleData(data, writeIndex, staticCount);
            writeIndex = WriteDynamicObstacleData(data, writeIndex, dynamicCount);

            if (writeIndex < totalCount) {
                Array.Resize(ref data, writeIndex);
            }

            return data;
        }

        private void UpdateDynamicObstacles() {
            if (!TryGetDynamicObstacleUpdateCount(out int count)) {
                return;
            }

            for (int index = 0; index < count; index += 1) {
                UpdateDynamicObstacleAtIndex(index);
            }
        }

        private int WriteStaticObstacleData(
            FlockObstacleData[] data,
            int writeIndex,
            int staticCount) {

            if (staticCount <= 0) {
                return writeIndex;
            }

            for (int index = 0; index < staticCount; index += 1) {
                FlockObstacle obstacle = staticObstacles[index];
                if (obstacle == null) {
                    continue;
                }

                data[writeIndex] = obstacle.ToData();
                writeIndex += 1;
            }

            return writeIndex;
        }

        private int WriteDynamicObstacleData(
            FlockObstacleData[] data,
            int writeIndex,
            int dynamicCount) {

            if (dynamicCount <= 0) {
                dynamicObstacleIndices = Array.Empty<int>();
                return writeIndex;
            }

            EnsureDynamicObstacleIndexBuffer(dynamicCount);

            for (int index = 0; index < dynamicCount; index += 1) {
                FlockObstacle obstacle = dynamicObstacles[index];

                if (obstacle == null) {
                    dynamicObstacleIndices[index] = -1;
                    continue;
                }

                data[writeIndex] = obstacle.ToData();
                dynamicObstacleIndices[index] = writeIndex;
                writeIndex += 1;
            }

            return writeIndex;
        }

        private void EnsureDynamicObstacleIndexBuffer(int dynamicCount) {
            if (dynamicObstacleIndices == null || dynamicObstacleIndices.Length != dynamicCount) {
                dynamicObstacleIndices = new int[dynamicCount];
            }
        }

        private bool TryGetDynamicObstacleUpdateCount(out int count) {
            count = 0;

            if (simulation == null || !simulation.IsCreated) {
                return false;
            }

            if (dynamicObstacles == null
                || dynamicObstacles.Length == 0
                || dynamicObstacleIndices == null
                || dynamicObstacleIndices.Length == 0) {
                return false;
            }

            count = Mathf.Min(dynamicObstacles.Length, dynamicObstacleIndices.Length);
            return count > 0;
        }

        private void UpdateDynamicObstacleAtIndex(int index) {
            int obstacleIndex = dynamicObstacleIndices[index];
            if (obstacleIndex < 0) {
                return;
            }

            FlockObstacle obstacle = dynamicObstacles[index];
            if (obstacle == null) {
                return;
            }

            FlockObstacleData data = obstacle.ToData();
            simulation.SetObstacleData(obstacleIndex, data);
        }
    }
}
