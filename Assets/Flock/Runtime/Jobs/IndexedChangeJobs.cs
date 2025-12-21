namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    public struct IndexedObstacleChange {
        public int Index;
        public FlockObstacleData Data;
    }

    public struct IndexedAttractorChange {
        public int Index;
        public FlockAttractorData Data;
    }

    [BurstCompile]
    public struct ApplyIndexedObstacleChangesJob : IJobParallelFor {
        [ReadOnly] public NativeArray<IndexedObstacleChange> Changes;
        public NativeArray<FlockObstacleData> Obstacles;

        public void Execute(int index) {
            IndexedObstacleChange c = Changes[index];
            Obstacles[c.Index] = c.Data;
        }
    }

    [BurstCompile]
    public struct ApplyIndexedAttractorChangesJob : IJobParallelFor {

        [ReadOnly] public NativeArray<IndexedAttractorChange> Changes;

        // This is the authoritative runtime array being edited
        public NativeArray<FlockAttractorData> Attractors;

        // Environment vertical normalisation inputs (world -> [0..1])
        public float EnvMinY;
        public float EnvHeight;

        public void Execute(int i) {
            IndexedAttractorChange c = Changes[i];

            int index = c.Index;
            if ((uint)index >= (uint)Attractors.Length) {
                return;
            }

            FlockAttractorData data = c.Data;

            // Protect against divide-by-zero
            float invH = 1.0f / math.max(EnvHeight, 0.0001f);

            float worldMinY;
            float worldMaxY;

            if (data.Shape == FlockAttractorShape.Sphere) {
                float r = math.max(0f, data.Radius);
                worldMinY = data.Position.y - r;
                worldMaxY = data.Position.y + r;
            } else {
                // Box: project rotated half-extents onto Y axis
                quaternion rot = data.BoxRotation;
                float3 he = data.BoxHalfExtents;

                float3 right = math.mul(rot, new float3(1f, 0f, 0f));
                float3 up = math.mul(rot, new float3(0f, 1f, 0f));
                float3 fwd = math.mul(rot, new float3(0f, 0f, 1f));

                float extentY =
                    math.abs(right.y) * he.x +
                    math.abs(up.y) * he.y +
                    math.abs(fwd.y) * he.z;

                worldMinY = data.Position.y - extentY;
                worldMaxY = data.Position.y + extentY;
            }

            float depthMinNorm = math.saturate((worldMinY - EnvMinY) * invH);
            float depthMaxNorm = math.saturate((worldMaxY - EnvMinY) * invH);

            if (depthMaxNorm < depthMinNorm) {
                float tmp = depthMinNorm;
                depthMinNorm = depthMaxNorm;
                depthMaxNorm = tmp;
            }

            data.DepthMinNorm = depthMinNorm;
            data.DepthMaxNorm = depthMaxNorm;

            Attractors[index] = data;
        }
    }
}
