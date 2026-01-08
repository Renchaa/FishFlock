using Flock.Scripts.Build.Influence.Environment.Attractors.Runtime;
using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
using Flock.Scripts.Build.Agents.Fish.Profiles;

using System;
using UnityEngine;

namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController
{
    /**
     * <summary>
     * Runtime controller that owns flock simulation state, configuration, and per-frame updates.
     * </summary>
     */
    public sealed partial class FlockController
    {
        private int[] dynamicAttractorIndices;

        private FlockAttractorData[] BuildAttractorData()
        {
            int staticAttractorCount = staticAttractors != null ? staticAttractors.Length : 0;
            int dynamicAttractorCount = dynamicAttractors != null ? dynamicAttractors.Length : 0;

            int totalCount = 0;

            if (staticAttractorCount > 0)
            {
                for (int index = 0; index < staticAttractorCount; index += 1)
                {
                    if (staticAttractors[index] != null)
                    {
                        totalCount += 1;
                    }
                }
            }

            if (dynamicAttractorCount > 0)
            {
                for (int index = 0; index < dynamicAttractorCount; index += 1)
                {
                    if (dynamicAttractors[index] != null)
                    {
                        totalCount += 1;
                    }
                }
            }

            if (totalCount == 0)
            {
                dynamicAttractorIndices = Array.Empty<int>();
                return Array.Empty<FlockAttractorData>();
            }

            FlockAttractorData[] data = new FlockAttractorData[totalCount];
            int writeIndex = 0;

            if (staticAttractorCount > 0)
            {
                for (int index = 0; index < staticAttractorCount; index += 1)
                {
                    FlockAttractorArea area = staticAttractors[index];
                    if (area == null)
                    {
                        continue;
                    }

                    uint mask = ComputeAttractorMask(area);
                    data[writeIndex] = area.ToData(mask);
                    writeIndex += 1;
                }
            }

            if (dynamicAttractorCount > 0)
            {
                if (dynamicAttractorIndices == null || dynamicAttractorIndices.Length != dynamicAttractorCount)
                {
                    dynamicAttractorIndices = new int[dynamicAttractorCount];
                }

                for (int index = 0; index < dynamicAttractorCount; index += 1)
                {
                    FlockAttractorArea area = dynamicAttractors[index];

                    if (area == null)
                    {
                        dynamicAttractorIndices[index] = -1;
                        continue;
                    }

                    uint mask = ComputeAttractorMask(area);
                    data[writeIndex] = area.ToData(mask);
                    dynamicAttractorIndices[index] = writeIndex;
                    writeIndex += 1;
                }
            }
            else
            {
                dynamicAttractorIndices = Array.Empty<int>();
            }

            if (writeIndex < totalCount)
            {
                Array.Resize(ref data, writeIndex);
            }

            return data;
        }

        private uint ComputeAttractorMask(FlockAttractorArea area)
        {
            if (area == null || fishTypes == null || fishTypes.Length == 0)
            {
                return uint.MaxValue;
            }

            FishTypePreset[] targetTypes = area.AttractedTypes;
            if (targetTypes == null || targetTypes.Length == 0)
            {
                return uint.MaxValue;
            }

            uint mask = 0u;

            for (int targetTypeIndex = 0; targetTypeIndex < targetTypes.Length; targetTypeIndex += 1)
            {
                FishTypePreset targetType = targetTypes[targetTypeIndex];
                if (targetType == null)
                {
                    continue;
                }

                for (int fishTypeIndex = 0; fishTypeIndex < fishTypes.Length; fishTypeIndex += 1)
                {
                    if (fishTypes[fishTypeIndex] == targetType)
                    {
                        if (fishTypeIndex < 32)
                        {
                            mask |= 1u << fishTypeIndex;
                        }

                        break;
                    }
                }
            }

            if (mask == 0u)
            {
                return uint.MaxValue;
            }

            return mask;
        }

        private void UpdateDynamicAttractors()
        {
            if (simulation == null || !simulation.IsCreated)
            {
                return;
            }

            if (dynamicAttractors == null
                || dynamicAttractors.Length == 0
                || dynamicAttractorIndices == null
                || dynamicAttractorIndices.Length == 0)
            {
                return;
            }

            int count = Mathf.Min(dynamicAttractors.Length, dynamicAttractorIndices.Length);
            bool anyUpdated = false;

            for (int index = 0; index < count; index += 1)
            {
                int attractorIndex = dynamicAttractorIndices[index];
                if (attractorIndex < 0)
                {
                    continue;
                }

                FlockAttractorArea area = dynamicAttractors[index];
                if (area == null)
                {
                    continue;
                }

                uint mask = ComputeAttractorMask(area);
                FlockAttractorData data = area.ToData(mask);
                simulation.SetAttractorData(attractorIndex, data);
                anyUpdated = true;
            }

            if (anyUpdated)
            {
                simulation.RebuildAttractorGrid();
            }
        }
    }
}
