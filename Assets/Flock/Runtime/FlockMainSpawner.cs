using System.Collections.Generic;
using Flock.Runtime.Data;
using Flock.Runtime.Logging;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Flock.Runtime {
    /**
     * <summary>
     * Central spawn configuration for a <see cref="FlockController"/>.
     * - All spawn counts live here (not in <see cref="FishTypePreset"/>).
     * - Point spawns: spawn around <see cref="FlockSpawnPoint"/> shapes.
     * - Seed spawns: scatter across entire bounds using a seed.
     * </summary>
     */
    public sealed class FlockMainSpawner : MonoBehaviour, IFlockLogger {
        #region Serialized Fields

        [Header("Point Spawns (spawn in regions)")]
        [Tooltip("Point-based spawn batches, evaluated before seed-based and fallback spawning.")]
        [SerializeField]
        private PointSpawnConfig[] pointSpawns;

        [Header("Seed Spawns (scatter in bounds)")]
        [Tooltip("Seed-based spawn batches, evaluated after point-based spawns and before fallback spawning.")]
        [SerializeField]
        private SeedSpawnConfig[] seedSpawns;

        [Header("Randomization")]
        [Tooltip("Global seed used to derive per-point/per-type seeds.")]
        [SerializeField]
        private uint globalSeed = 1u;

        #endregion

        #region Public Properties

        /**
         * <summary>
         * Gets the enabled log levels for this logger.
         * </summary>
         */
        public FlockLogLevel EnabledLevels => FlockLogLevel.All;

        /**
         * <summary>
         * Gets the enabled log categories for this logger.
         * </summary>
         */
        public FlockLogCategory EnabledCategories => FlockLogCategory.All;

        #endregion

        #region Public API

        /**
         * <summary>
         * Builds the agent behaviour id array from the configured point and seed spawns.
         * This is the only place where spawn counts are summed.
         * </summary>
         * <param name="fishTypes">Ordered fish types used to map presets to behaviour indices.</param>
         * <returns>An array of behaviour indices (length = total agent count), or null if no agents should spawn.</returns>
         */
        public int[] BuildAgentBehaviourIds(FishTypePreset[] fishTypes) {
            if (!TryGetBehaviourCount(fishTypes, out int behaviourCount)) {
                return null;
            }

            if (!TryBuildPresetToIndexMap(fishTypes, behaviourCount, out Dictionary<FishTypePreset, int> presetToIndex)) {
                return null;
            }

            int[] perTypeTotals = new int[behaviourCount];

            AccumulateTotalsFromPointSpawns(presetToIndex, perTypeTotals);
            AccumulateTotalsFromSeedSpawns(presetToIndex, perTypeTotals);

            int totalAgentCount = GetTotalAgentCount(perTypeTotals);
            if (totalAgentCount <= 0) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.BuildAgentBehaviourIds: computed total agent count <= 0. No agents will be spawned.",
                    this);
                return null;
            }

            return BuildAgentBehaviourIdArray(perTypeTotals, totalAgentCount);
        }

        /**
         * <summary>
         * Writes initial positions into the positions array, using:
         * - point-based spawns first,
         * - then seed-based spawns across the whole bounds,
         * - then fallback scatter for any remaining agents.
         * </summary>
         * <param name="environment">Environment/bounds data used for sampling positions.</param>
         * <param name="fishTypes">Ordered fish types used to map presets to behaviour indices.</param>
         * <param name="agentBehaviourIds">Behaviour id per agent (must match positions length).</param>
         * <param name="positions">Output positions array.</param>
         */
        public void AssignInitialPositions(
            FlockEnvironmentData environment,
            FishTypePreset[] fishTypes,
            int[] agentBehaviourIds,
            NativeArray<float3> positions) {
            if (!positions.IsCreated) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.AssignInitialPositions: positions NativeArray is not created.",
                    this);
                return;
            }

            if (agentBehaviourIds == null || agentBehaviourIds.Length != positions.Length) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.AssignInitialPositions: agentBehaviourIds is null or length mismatch with positions.",
                    this);
                return;
            }

            int behaviourCount = fishTypes != null ? fishTypes.Length : 0;
            if (behaviourCount == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.AssignInitialPositions: fishTypes array is null or empty.",
                    this);
                return;
            }

            Dictionary<FishTypePreset, int> presetToIndex = BuildPresetToIndexMap(fishTypes, behaviourCount);
            List<int>[] freeAgentIndexPoolsByType = BuildFreeAgentIndexPoolsByType(agentBehaviourIds, behaviourCount);

            ApplyPointSpawns(environment, presetToIndex, freeAgentIndexPoolsByType, positions);
            ApplySeedSpawns(environment, presetToIndex, freeAgentIndexPoolsByType, positions);
            ApplyFallbackScatter(environment, freeAgentIndexPoolsByType, positions);
        }

        #endregion

        #region Utility Methods

        private bool TryGetBehaviourCount(FishTypePreset[] fishTypes, out int behaviourCount) {
            if (fishTypes == null || fishTypes.Length == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.BuildAgentBehaviourIds: fishTypes array is null or empty.",
                    this);
                behaviourCount = 0;
                return false;
            }

            behaviourCount = fishTypes.Length;
            return true;
        }

        private bool TryBuildPresetToIndexMap(
            FishTypePreset[] fishTypes,
            int behaviourCount,
            out Dictionary<FishTypePreset, int> presetToIndex) {
            presetToIndex = BuildPresetToIndexMap(fishTypes, behaviourCount);

            if (presetToIndex.Count == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.BuildAgentBehaviourIds: no valid FishTypePreset entries to map.",
                    this);
                return false;
            }

            return true;
        }

        private static Dictionary<FishTypePreset, int> BuildPresetToIndexMap(FishTypePreset[] fishTypes, int behaviourCount) {
            Dictionary<FishTypePreset, int> presetToIndex = new Dictionary<FishTypePreset, int>(behaviourCount);

            for (int behaviourIndex = 0; behaviourIndex < behaviourCount; behaviourIndex += 1) {
                FishTypePreset preset = fishTypes[behaviourIndex];
                if (preset == null) {
                    continue;
                }

                if (!presetToIndex.ContainsKey(preset)) {
                    presetToIndex.Add(preset, behaviourIndex);
                }
            }

            return presetToIndex;
        }

        private void AccumulateTotalsFromPointSpawns(
            Dictionary<FishTypePreset, int> presetToIndex,
            int[] perTypeTotals) {
            if (pointSpawns == null) {
                return;
            }

            for (int spawnConfigIndex = 0; spawnConfigIndex < pointSpawns.Length; spawnConfigIndex += 1) {
                PointSpawnConfig config = pointSpawns[spawnConfigIndex];
                if (config == null) {
                    continue;
                }

                AccumulateFromEntries(config.types, $"PointSpawn[{spawnConfigIndex}]", presetToIndex, perTypeTotals);
            }
        }

        private void AccumulateTotalsFromSeedSpawns(
            Dictionary<FishTypePreset, int> presetToIndex,
            int[] perTypeTotals) {
            if (seedSpawns == null) {
                return;
            }

            for (int spawnConfigIndex = 0; spawnConfigIndex < seedSpawns.Length; spawnConfigIndex += 1) {
                SeedSpawnConfig config = seedSpawns[spawnConfigIndex];
                if (config == null) {
                    continue;
                }

                AccumulateFromEntries(config.types, $"SeedSpawn[{spawnConfigIndex}]", presetToIndex, perTypeTotals);
            }
        }

        private void AccumulateFromEntries(
            TypeCountEntry[] entries,
            string contextLabel,
            Dictionary<FishTypePreset, int> presetToIndex,
            int[] perTypeTotals) {
            if (entries == null) {
                return;
            }

            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex += 1) {
                TypeCountEntry entry = entries[entryIndex];
                if (entry.preset == null || entry.count <= 0) {
                    continue;
                }

                if (!presetToIndex.TryGetValue(entry.preset, out int typeIndex)) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"FlockMainSpawner.BuildAgentBehaviourIds: preset '{entry.preset.name}' in {contextLabel} is not present in fishTypes array.",
                        this);
                    continue;
                }

                perTypeTotals[typeIndex] += entry.count;
            }
        }

        private static int GetTotalAgentCount(int[] perTypeTotals) {
            int totalAgentCount = 0;

            for (int typeIndex = 0; typeIndex < perTypeTotals.Length; typeIndex += 1) {
                totalAgentCount += math.max(0, perTypeTotals[typeIndex]);
            }

            return totalAgentCount;
        }

        private static int[] BuildAgentBehaviourIdArray(int[] perTypeTotals, int totalAgentCount) {
            int[] agentBehaviourIds = new int[totalAgentCount];
            int writeIndex = 0;

            for (int typeIndex = 0; typeIndex < perTypeTotals.Length; typeIndex += 1) {
                int count = math.max(0, perTypeTotals[typeIndex]);

                for (int agentOffsetIndex = 0; agentOffsetIndex < count; agentOffsetIndex += 1) {
                    agentBehaviourIds[writeIndex] = typeIndex;
                    writeIndex += 1;
                }
            }

            return agentBehaviourIds;
        }

        private List<int>[] BuildFreeAgentIndexPoolsByType(int[] agentBehaviourIds, int behaviourCount) {
            List<int>[] freeAgentIndexPoolsByType = new List<int>[behaviourCount];

            for (int typeIndex = 0; typeIndex < behaviourCount; typeIndex += 1) {
                freeAgentIndexPoolsByType[typeIndex] = new List<int>();
            }

            for (int agentIndex = 0; agentIndex < agentBehaviourIds.Length; agentIndex += 1) {
                int typeIndex = agentBehaviourIds[agentIndex];
                if ((uint)typeIndex >= (uint)behaviourCount) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"FlockMainSpawner.AssignInitialPositions: agentBehaviourIds[{agentIndex}] = {typeIndex} is out of range.",
                        this);
                    continue;
                }

                freeAgentIndexPoolsByType[typeIndex].Add(agentIndex);
            }

            return freeAgentIndexPoolsByType;
        }

        private void ApplyPointSpawns(
            FlockEnvironmentData environment,
            Dictionary<FishTypePreset, int> presetToIndex,
            List<int>[] freeAgentIndexPoolsByType,
            NativeArray<float3> positions) {
            if (pointSpawns == null) {
                return;
            }

            for (int spawnConfigIndex = 0; spawnConfigIndex < pointSpawns.Length; spawnConfigIndex += 1) {
                PointSpawnConfig config = pointSpawns[spawnConfigIndex];
                if (config == null || config.point == null || config.types == null || config.types.Length == 0) {
                    continue;
                }

                uint seed = (config.useSeed && config.seed != 0u)
                    ? config.seed
                    : DeriveSeed(globalSeed, (uint)(spawnConfigIndex + 1), 0x9E3779B9u);

                if (seed == 0u) {
                    seed = 1u; // Random cannot take 0.
                }

                Random random = new Random(seed);

                for (int typeEntryIndex = 0; typeEntryIndex < config.types.Length; typeEntryIndex += 1) {
                    TypeCountEntry entry = config.types[typeEntryIndex];
                    if (entry.preset == null || entry.count <= 0) {
                        continue;
                    }

                    if (!presetToIndex.TryGetValue(entry.preset, out int typeIndex)) {
                        FlockLog.Warning(
                            this,
                            FlockLogCategory.Controller,
                            $"FlockMainSpawner.AssignInitialPositions: preset '{entry.preset.name}' in PointSpawn[{spawnConfigIndex}] is not in fishTypes.",
                            this);
                        continue;
                    }

                    List<int> pool = freeAgentIndexPoolsByType[typeIndex];
                    int spawnCount = math.min(entry.count, pool.Count);

                    if (spawnCount < entry.count) {
                        FlockLog.Warning(
                            this,
                            FlockLogCategory.Controller,
                            $"PointSpawn[{spawnConfigIndex}] requested {entry.count} agents of type '{entry.preset.name}' but only {pool.Count} were available.",
                            this);
                    }

                    for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex += 1) {
                        int agentIndex = PopLastIndex(pool);
                        float3 sampledPosition = config.point.SamplePosition(ref random);
                        sampledPosition = ClampToBounds(sampledPosition, environment);
                        positions[agentIndex] = sampledPosition;
                    }
                }
            }
        }

        private void ApplySeedSpawns(
            FlockEnvironmentData environment,
            Dictionary<FishTypePreset, int> presetToIndex,
            List<int>[] freeAgentIndexPoolsByType,
            NativeArray<float3> positions) {
            if (seedSpawns == null) {
                return;
            }

            for (int spawnConfigIndex = 0; spawnConfigIndex < seedSpawns.Length; spawnConfigIndex += 1) {
                SeedSpawnConfig config = seedSpawns[spawnConfigIndex];
                if (config == null || config.types == null || config.types.Length == 0) {
                    continue;
                }

                uint baseSeed = (config.useSeed && config.seed != 0u)
                    ? config.seed
                    : DeriveSeed(globalSeed, (uint)(spawnConfigIndex + 1), 0xBB67AE85u);

                if (baseSeed == 0u) {
                    baseSeed = 1u;
                }

                Random random = new Random(baseSeed);

                for (int typeEntryIndex = 0; typeEntryIndex < config.types.Length; typeEntryIndex += 1) {
                    TypeCountEntry entry = config.types[typeEntryIndex];
                    if (entry.preset == null || entry.count <= 0) {
                        continue;
                    }

                    if (!presetToIndex.TryGetValue(entry.preset, out int typeIndex)) {
                        FlockLog.Warning(
                            this,
                            FlockLogCategory.Controller,
                            $"FlockMainSpawner.AssignInitialPositions: preset '{entry.preset.name}' in SeedSpawn[{spawnConfigIndex}] is not in fishTypes.",
                            this);
                        continue;
                    }

                    List<int> pool = freeAgentIndexPoolsByType[typeIndex];
                    int spawnCount = math.min(entry.count, pool.Count);

                    if (spawnCount < entry.count) {
                        FlockLog.Warning(
                            this,
                            FlockLogCategory.Controller,
                            $"SeedSpawn[{spawnConfigIndex}] requested {entry.count} agents of type '{entry.preset.name}' but only {pool.Count} were available.",
                            this);
                    }

                    for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex += 1) {
                        int agentIndex = PopLastIndex(pool);
                        float3 sampledPosition = SampleInBounds(ref random, environment);
                        positions[agentIndex] = sampledPosition;
                    }
                }
            }
        }

        private void ApplyFallbackScatter(
            FlockEnvironmentData environment,
            List<int>[] freeAgentIndexPoolsByType,
            NativeArray<float3> positions) {
            for (int typeIndex = 0; typeIndex < freeAgentIndexPoolsByType.Length; typeIndex += 1) {
                List<int> pool = freeAgentIndexPoolsByType[typeIndex];
                if (pool.Count == 0) {
                    continue;
                }

                uint seed = DeriveSeed(globalSeed, (uint)(typeIndex + 1), 0x3C6EF372u);
                Random random = new Random(seed == 0u ? 1u : seed);

                for (int poolIndex = pool.Count - 1; poolIndex >= 0; poolIndex -= 1) {
                    int agentIndex = pool[poolIndex];
                    float3 sampledPosition = SampleInBounds(ref random, environment);
                    positions[agentIndex] = sampledPosition;
                }
            }
        }

        private static int PopLastIndex(List<int> list) {
            int lastIndex = list.Count - 1;
            int value = list[lastIndex];
            list.RemoveAt(lastIndex);
            return value;
        }

        private static uint DeriveSeed(uint baseSeed, uint salt, uint prime) {
            uint seed = baseSeed ^ (salt * prime);

            if (seed == 0u) {
                seed = 1u;
            }

            return seed;
        }

        private static float3 SampleInBounds(ref Random random, FlockEnvironmentData environment) {
            float3 boundsCenter = environment.BoundsCenter;

            // Spherical bounds: sample inside sphere volume.
            if (environment.BoundsType == FlockBoundsType.Sphere && environment.BoundsRadius > 0f) {
                float radius = environment.BoundsRadius;

                // Rejection sample direction in unit sphere, then scale radius with cubic root.
                for (int attemptIndex = 0; attemptIndex < 8; attemptIndex++) {
                    float3 randomVector = random.NextFloat3(new float3(-1f), new float3(1f));
                    float lengthSquared = math.lengthsq(randomVector);

                    if (lengthSquared > 1e-4f && lengthSquared <= 1f) {
                        float length = math.sqrt(lengthSquared);
                        float3 direction = randomVector / length;

                        float unit = random.NextFloat();                    // 0..1
                        float sampledRadius = radius * math.pow(unit, 1f / 3f); // uniform volume

                        return boundsCenter + direction * sampledRadius;
                    }
                }

                // Fallback if rejection failed: random in cube, then clamp to sphere.
                float3 fallbackPosition = boundsCenter + random.NextFloat3(new float3(-radius), new float3(radius));
                float3 offset = fallbackPosition - boundsCenter;
                float distanceSquared = math.lengthsq(offset);

                if (distanceSquared > radius * radius) {
                    float distance = math.sqrt(distanceSquared);
                    float3 direction = offset / math.max(distance, 1e-4f);
                    fallbackPosition = boundsCenter + direction * radius * 0.999f;
                }

                return fallbackPosition;
            }

            // Box bounds (default).
            float3 extents = environment.BoundsExtents;
            float3 boundsMinimum = boundsCenter - extents;
            float3 boundsSize = extents * 2f;

            float3 randomUnit = new float3(
                random.NextFloat(),
                random.NextFloat(),
                random.NextFloat());

            return boundsMinimum + randomUnit * boundsSize;
        }

        private static float3 ClampToBounds(float3 position, FlockEnvironmentData environment) {
            float3 boundsCenter = environment.BoundsCenter;

            if (environment.BoundsType == FlockBoundsType.Sphere && environment.BoundsRadius > 0f) {
                float radius = environment.BoundsRadius;
                float3 offset = position - boundsCenter;
                float distanceSquared = math.lengthsq(offset);

                if (distanceSquared > radius * radius) {
                    float distance = math.sqrt(distanceSquared);
                    float3 direction = offset / math.max(distance, 1e-4f);
                    return boundsCenter + direction * radius * 0.999f;
                }

                return position;
            }

            // Box.
            float3 extents = environment.BoundsExtents;
            float3 boundsMinimum = boundsCenter - extents;
            float3 boundsMaximum = boundsCenter + extents;

            return math.clamp(position, boundsMinimum, boundsMaximum);
        }

        #endregion

        #region Inner Structs

        [System.Serializable]
        public struct TypeCountEntry {
            [Tooltip("Fish type preset to spawn.")]
            public FishTypePreset preset;

            [Tooltip("Number of agents to spawn for this preset.")]
            [Min(0)]
            public int count;
        }

        [System.Serializable]
        public sealed class PointSpawnConfig {
            [Tooltip("Spawn point shape used to sample positions.")]
            public FlockSpawnPoint point;

            [Tooltip("If enabled, overrides the global seed for this point spawn.")]
            public bool useSeed;

            [Tooltip("Explicit seed for this point spawn. Ignored when Use Seed is disabled or seed is 0.")]
            public uint seed;

            [Tooltip("List of (preset + count) entries spawned from this point.")]
            public TypeCountEntry[] types;
        }

        [System.Serializable]
        public sealed class SeedSpawnConfig {
            [Tooltip("If enabled, overrides the global seed for this scatter batch.")]
            public bool useSeed;

            [Tooltip("Base seed for this global scatter batch. When Use Seed is disabled or seed is 0, a seed is derived from the Global Seed.")]
            public uint seed;

            [Tooltip("List of (preset + count) entries spawned in bounds for this batch.")]
            public TypeCountEntry[] types;
        }

        #endregion
    }
}
