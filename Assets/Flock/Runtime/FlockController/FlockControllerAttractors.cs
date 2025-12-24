namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using System;
    using UnityEngine;

    public sealed partial class FlockController {
        int[] dynamicAttractorIndices;

        FlockAttractorData[] BuildAttractorData() {
            int staticCount = staticAttractors != null ? staticAttractors.Length : 0;
            int dynamicCount = dynamicAttractors != null ? dynamicAttractors.Length : 0;

            int totalCount = 0;

            if (staticCount > 0) {
                for (int i = 0; i < staticCount; i += 1) {
                    if (staticAttractors[i] != null) {
                        totalCount += 1;
                    }
                }
            }

            if (dynamicCount > 0) {
                for (int i = 0; i < dynamicCount; i += 1) {
                    if (dynamicAttractors[i] != null) {
                        totalCount += 1;
                    }
                }
            }

            if (totalCount == 0) {
                dynamicAttractorIndices = Array.Empty<int>();
                return Array.Empty<FlockAttractorData>();
            }

            FlockAttractorData[] data = new FlockAttractorData[totalCount];
            int writeIndex = 0;

            // Static attractors
            if (staticCount > 0) {
                for (int i = 0; i < staticCount; i += 1) {
                    FlockAttractorArea area = staticAttractors[i];
                    if (area == null) {
                        continue;
                    }

                    uint mask = ComputeAttractorMask(area);
                    data[writeIndex] = area.ToData(mask);
                    writeIndex += 1;
                }
            }

            // Dynamic attractors
            if (dynamicCount > 0) {
                if (dynamicAttractorIndices == null || dynamicAttractorIndices.Length != dynamicCount) {
                    dynamicAttractorIndices = new int[dynamicCount];
                }

                for (int i = 0; i < dynamicCount; i += 1) {
                    FlockAttractorArea area = dynamicAttractors[i];

                    if (area == null) {
                        dynamicAttractorIndices[i] = -1;
                        continue;
                    }

                    uint mask = ComputeAttractorMask(area);
                    data[writeIndex] = area.ToData(mask);
                    dynamicAttractorIndices[i] = writeIndex;
                    writeIndex += 1;
                }
            } else {
                dynamicAttractorIndices = Array.Empty<int>();
            }

            if (writeIndex < totalCount) {
                Array.Resize(ref data, writeIndex);
            }

            return data;
        }

        uint ComputeAttractorMask(FlockAttractorArea area) {
            if (area == null || fishTypes == null || fishTypes.Length == 0) {
                return uint.MaxValue; // affect all types
            }

            FishTypePreset[] targetTypes = area.AttractedTypes;
            if (targetTypes == null || targetTypes.Length == 0) {
                return uint.MaxValue; // affect all types
            }

            uint mask = 0u;

            for (int t = 0; t < targetTypes.Length; t += 1) {
                FishTypePreset target = targetTypes[t];
                if (target == null) {
                    continue;
                }

                for (int i = 0; i < fishTypes.Length; i += 1) {
                    if (fishTypes[i] == target) {
                        if (i < 32) {
                            mask |= (1u << i);
                        }
                        break;
                    }
                }
            }

            if (mask == 0u) {
                return uint.MaxValue; // fallback: if mapping failed, affect everyone
            }

            return mask;
        }

        void UpdateDynamicAttractors() {
            if (simulation == null || !simulation.IsCreated) {
                return;
            }

            if (dynamicAttractors == null
                || dynamicAttractors.Length == 0
                || dynamicAttractorIndices == null
                || dynamicAttractorIndices.Length == 0) {
                return;
            }

            int count = Mathf.Min(dynamicAttractors.Length, dynamicAttractorIndices.Length);
            bool anyUpdated = false;

            for (int i = 0; i < count; i += 1) {
                int attractorIndex = dynamicAttractorIndices[i];
                if (attractorIndex < 0) {
                    continue;
                }

                FlockAttractorArea area = dynamicAttractors[i];
                if (area == null) {
                    continue;
                }

                uint mask = ComputeAttractorMask(area);
                FlockAttractorData data = area.ToData(mask);
                simulation.SetAttractorData(attractorIndex, data);
                anyUpdated = true;
            }

            // Re-stamp attractors into grid if anything moved / changed
            if (anyUpdated) {
                simulation.RebuildAttractorGrid();
            }
        }
    }
}
