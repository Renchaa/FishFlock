namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using System;
    using UnityEngine;

    public sealed partial class FlockController {
        int[] dynamicObstacleIndices;

        FlockObstacleData[] BuildObstacleData() {
            int staticCount = staticObstacles != null ? staticObstacles.Length : 0;
            int dynamicCount = dynamicObstacles != null ? dynamicObstacles.Length : 0;

            int totalCount = staticCount + dynamicCount;

            if (totalCount == 0) {
                dynamicObstacleIndices = Array.Empty<int>();
                return Array.Empty<FlockObstacleData>();
            }

            FlockObstacleData[] data = new FlockObstacleData[totalCount];
            int writeIndex = 0;

            // Static obstacles: no need to track indices, they never move.
            if (staticCount > 0) {
                for (int index = 0; index < staticCount; index += 1) {
                    FlockObstacle obstacle = staticObstacles[index];
                    if (obstacle == null) {
                        continue;
                    }

                    data[writeIndex] = obstacle.ToData();
                    writeIndex += 1;
                }
            }

            // Dynamic obstacles: keep index mapping so we can update positions each frame.
            if (dynamicCount > 0) {
                if (dynamicObstacleIndices == null || dynamicObstacleIndices.Length != dynamicCount) {
                    dynamicObstacleIndices = new int[dynamicCount];
                }

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
            } else {
                dynamicObstacleIndices = Array.Empty<int>();
            }

            if (writeIndex < totalCount) {
                Array.Resize(ref data, writeIndex);
            }

            return data;
        }

        void UpdateDynamicObstacles() {
            if (simulation == null || !simulation.IsCreated) {
                return;
            }

            if (dynamicObstacles == null
                || dynamicObstacles.Length == 0
                || dynamicObstacleIndices == null
                || dynamicObstacleIndices.Length == 0) {
                return;
            }

            int count = Mathf.Min(dynamicObstacles.Length, dynamicObstacleIndices.Length);

            for (int index = 0; index < count; index += 1) {
                int obstacleIndex = dynamicObstacleIndices[index];
                if (obstacleIndex < 0) {
                    continue;
                }

                FlockObstacle obstacle = dynamicObstacles[index];
                if (obstacle == null) {
                    continue;
                }

                FlockObstacleData data = obstacle.ToData();
                simulation.SetObstacleData(obstacleIndex, data);
            }
        }
    }
}
