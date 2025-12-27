namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Unity.Collections;
    using Unity.Mathematics;

    /**
     * <summary>
     * Simulation runtime that manages native state for agents, grids, and attractors.
     * </summary>
     */
    public sealed partial class FlockSimulation {
        /**
         * <summary>
         * Queues a single attractor data update to be applied to the simulation.
         * Marks the attractor grid as dirty so spatial stamping can be rebuilt.
         * </summary>
         * <param name="index">Index of the attractor to update.</param>
         * <param name="data">New attractor data to apply.</param>
         */
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

        /**
         * <summary>
         * Marks the attractor grid as dirty so it will be rebuilt on the next simulation update.
         * </summary>
         */
        public void RebuildAttractorGrid() {
            attractorGridDirty = true;
        }

        void AllocateAttractors(FlockAttractorData[] sourceAttractors, Allocator allocator) {
            if (!TryAllocateAttractorArray(sourceAttractors, allocator)) {
                return;
            }

            GetEnvironmentDepthBounds(out float environmentMinimumY, out float environmentHeight);

            for (int index = 0; index < attractorCount; index += 1) {
                FlockAttractorData attractorData = sourceAttractors[index];
                ApplyNormalizedDepthRange(ref attractorData, environmentMinimumY, environmentHeight);
                attractors[index] = attractorData;
            }
        }

        void AllocateAttractorSimulationData(Allocator allocator) {
            attractionSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);
        }

        bool TryAllocateAttractorArray(FlockAttractorData[] sourceAttractors, Allocator allocator) {
            if (sourceAttractors == null || sourceAttractors.Length == 0) {
                attractorCount = 0;
                attractors = default;
                return false;
            }

            attractorCount = sourceAttractors.Length;

            attractors = new NativeArray<FlockAttractorData>(
                attractorCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            return true;
        }

        void GetEnvironmentDepthBounds(out float environmentMinimumY, out float environmentHeight) {
            environmentMinimumY = environmentData.BoundsCenter.y - environmentData.BoundsExtents.y;

            float environmentMaximumY = environmentData.BoundsCenter.y + environmentData.BoundsExtents.y;
            environmentHeight = math.max(environmentMaximumY - environmentMinimumY, 0.0001f);
        }

        void ApplyNormalizedDepthRange(
            ref FlockAttractorData attractorData,
            float environmentMinimumY,
            float environmentHeight) {

            GetAttractorWorldMinMaxY(attractorData, out float worldMinimumY, out float worldMaximumY);

            float depthMinimumNormalized = math.saturate((worldMinimumY - environmentMinimumY) / environmentHeight);
            float depthMaximumNormalized = math.saturate((worldMaximumY - environmentMinimumY) / environmentHeight);

            EnsureOrderedMinMax(ref depthMinimumNormalized, ref depthMaximumNormalized);

            attractorData.DepthMinNorm = depthMinimumNormalized;
            attractorData.DepthMaxNorm = depthMaximumNormalized;
        }

        void GetAttractorWorldMinMaxY(
            in FlockAttractorData attractorData,
            out float worldMinimumY,
            out float worldMaximumY) {

            if (attractorData.Shape == FlockAttractorShape.Sphere) {
                worldMinimumY = attractorData.Position.y - attractorData.Radius;
                worldMaximumY = attractorData.Position.y + attractorData.Radius;
                return;
            }

            float worldExtentY = ComputeWorldBoxExtentY(attractorData.BoxRotation, attractorData.BoxHalfExtents);
            worldMinimumY = attractorData.Position.y - worldExtentY;
            worldMaximumY = attractorData.Position.y + worldExtentY;
        }

        float ComputeWorldBoxExtentY(quaternion rotation, float3 halfExtents) {
            float3 right = math.mul(rotation, new float3(1f, 0f, 0f));
            float3 up = math.mul(rotation, new float3(0f, 1f, 0f));
            float3 forward = math.mul(rotation, new float3(0f, 0f, 1f));

            return math.abs(right.y) * halfExtents.x
                + math.abs(up.y) * halfExtents.y
                + math.abs(forward.y) * halfExtents.z;
        }

        static void EnsureOrderedMinMax(ref float minimum, ref float maximum) {
            if (maximum >= minimum) {
                return;
            }

            float temporary = minimum;
            minimum = maximum;
            maximum = temporary;
        }
    }
}
