namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Unity.Collections;
    using Unity.Mathematics;

    public sealed partial class FlockSimulation {
        void AllocateAttractors(FlockAttractorData[] source, Allocator allocator) {
            if (source == null || source.Length == 0) {
                attractorCount = 0;
                attractors = default;
                return;
            }

            attractorCount = source.Length;

            attractors = new NativeArray<FlockAttractorData>(
                attractorCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            float envMinY = environmentData.BoundsCenter.y - environmentData.BoundsExtents.y;
            float envMaxY = environmentData.BoundsCenter.y + environmentData.BoundsExtents.y;
            float envHeight = math.max(envMaxY - envMinY, 0.0001f);

            for (int index = 0; index < attractorCount; index += 1) {
                FlockAttractorData data = source[index];

                float worldMinY;
                float worldMaxY;

                if (data.Shape == FlockAttractorShape.Sphere) {
                    worldMinY = data.Position.y - data.Radius;
                    worldMaxY = data.Position.y + data.Radius;
                } else {
                    quaternion rot = data.BoxRotation;
                    float3 halfExtents = data.BoxHalfExtents;

                    float3 right = math.mul(rot, new float3(1f, 0f, 0f));
                    float3 up = math.mul(rot, new float3(0f, 1f, 0f));
                    float3 fwd = math.mul(rot, new float3(0f, 0f, 1f));

                    float extentY =
                        math.abs(right.y) * halfExtents.x +
                        math.abs(up.y) * halfExtents.y +
                        math.abs(fwd.y) * halfExtents.z;

                    worldMinY = data.Position.y - extentY;
                    worldMaxY = data.Position.y + extentY;
                }

                float depthMinNorm = math.saturate((worldMinY - envMinY) / envHeight);
                float depthMaxNorm = math.saturate((worldMaxY - envMinY) / envHeight);

                if (depthMaxNorm < depthMinNorm) {
                    float tmp = depthMinNorm;
                    depthMinNorm = depthMaxNorm;
                    depthMaxNorm = tmp;
                }

                data.DepthMinNorm = depthMinNorm;
                data.DepthMaxNorm = depthMaxNorm;

                attractors[index] = data;
            }
        }

        void AllocateAttractorSimulationData(Allocator allocator) {
            attractionSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);
        }

        public void SetAttractorData(int index, FlockAttractorData data) {
            if (!IsCreated || !attractors.IsCreated) {
                return;
            }

            if ((uint)index >= (uint)attractors.Length) {
                return;
            }

            pendingAttractorChanges.Add(new Flock.Runtime.Jobs.IndexedAttractorChange {
                Index = index,
                Data = data,
            });

            attractorGridDirty = true;
        }

        public void RebuildAttractorGrid() {
            attractorGridDirty = true;
        }
    }
}
