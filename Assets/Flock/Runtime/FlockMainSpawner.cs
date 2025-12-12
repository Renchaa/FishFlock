// File: Assets/Flock/Runtime/FlockMainSpawner.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using System.Collections.Generic;
    using Unity.Collections;
    using Unity.Mathematics;
    using UnityEngine;
    using Random = Unity.Mathematics.Random;

    /// <summary>
    /// Central spawn configuration for a FlockController.
    /// - All spawn counts live here (not in FishTypePreset).
    /// - Point spawns: spawn around FlockSpawnPoint shapes.
    /// - Seed spawns: scatter across entire bounds using a seed.
    /// </summary>
    public sealed class FlockMainSpawner : MonoBehaviour, IFlockLogger {

        [System.Serializable]
        public struct TypeCountEntry {
            public FishTypePreset preset;   // visible, selectable in inspector
            [Min(0)] public int count;
        }

        [System.Serializable]
        public sealed class PointSpawnConfig {
            public FlockSpawnPoint point;
            public TypeCountEntry[] types;  // visible list of (preset + count)
        }

        [System.Serializable]
        public sealed class SeedSpawnConfig {
            [Tooltip("Base seed for this global scatter batch. 0 will be remapped to a non-zero seed.")]
            public uint seed;
            public TypeCountEntry[] types;  // visible list of (preset + count)
        }

        [Header("Point Spawns (spawn in regions)")]
        [SerializeField] PointSpawnConfig[] pointSpawns;

        [Header("Seed Spawns (scatter in bounds)")]
        [SerializeField] SeedSpawnConfig[] seedSpawns;

        [Header("Randomization")]
        [Tooltip("Global seed used to derive per-point/per-type seeds.")]
        [SerializeField] uint globalSeed = 1u;

        public FlockLogLevel EnabledLevels => FlockLogLevel.All;

        public FlockLogCategory EnabledCategories => FlockLogCategory.All;

        /// <summary>
        /// Builds the agent behaviour id array from the configured point and seed spawns.
        /// This is the only place where spawn counts are summed.
        /// </summary>
        public int[] BuildAgentBehaviourIds(FishTypePreset[] fishTypes) {
            if (fishTypes == null || fishTypes.Length == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.BuildAgentBehaviourIds: fishTypes array is null or empty.",
                    this);
                return null;
            }

            int behaviourCount = fishTypes.Length;

            // Map preset -> behaviour index
            var presetToIndex = new Dictionary<FishTypePreset, int>(behaviourCount);
            for (int i = 0; i < behaviourCount; i += 1) {
                FishTypePreset preset = fishTypes[i];
                if (preset == null) {
                    continue;
                }

                if (!presetToIndex.ContainsKey(preset)) {
                    presetToIndex.Add(preset, i);
                }
            }

            if (presetToIndex.Count == 0) {
                FlockLog.Error(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.BuildAgentBehaviourIds: no valid FishTypePreset entries to map.",
                    this);
                return null;
            }

            int[] perTypeTotals = new int[behaviourCount];

            void AccumulateFromEntries(TypeCountEntry[] entries, string contextLabel) {
                if (entries == null) {
                    return;
                }

                for (int i = 0; i < entries.Length; i += 1) {
                    TypeCountEntry entry = entries[i];
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

            if (pointSpawns != null) {
                for (int i = 0; i < pointSpawns.Length; i += 1) {
                    PointSpawnConfig cfg = pointSpawns[i];
                    if (cfg == null) {
                        continue;
                    }

                    AccumulateFromEntries(cfg.types, $"PointSpawn[{i}]");
                }
            }

            if (seedSpawns != null) {
                for (int i = 0; i < seedSpawns.Length; i += 1) {
                    SeedSpawnConfig cfg = seedSpawns[i];
                    if (cfg == null) {
                        continue;
                    }

                    AccumulateFromEntries(cfg.types, $"SeedSpawn[{i}]");
                }
            }

            int totalAgentCount = 0;
            for (int i = 0; i < perTypeTotals.Length; i += 1) {
                totalAgentCount += math.max(0, perTypeTotals[i]);
            }

            if (totalAgentCount <= 0) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    "FlockMainSpawner.BuildAgentBehaviourIds: computed total agent count <= 0. No agents will be spawned.",
                    this);
                return null;
            }

            int[] agentBehaviourIds = new int[totalAgentCount];
            int writeIndex = 0;

            for (int typeIndex = 0; typeIndex < perTypeTotals.Length; typeIndex += 1) {
                int count = math.max(0, perTypeTotals[typeIndex]);
                for (int j = 0; j < count; j += 1) {
                    agentBehaviourIds[writeIndex] = typeIndex;
                    writeIndex += 1;
                }
            }

            return agentBehaviourIds;
        }

        /// <summary>
        /// Writes initial positions into the positions array, using:
        /// - point-based spawns first
        /// - then seed-based spawns across the whole bounds
        /// - then fallback scatter for any remaining agents
        /// </summary>
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

            // Map preset -> behaviour index for configs
            var presetToIndex = new Dictionary<FishTypePreset, int>(behaviourCount);
            for (int i = 0; i < behaviourCount; i += 1) {
                FishTypePreset preset = fishTypes[i];
                if (preset == null) {
                    continue;
                }

                if (!presetToIndex.ContainsKey(preset)) {
                    presetToIndex.Add(preset, i);
                }
            }

            // Build per-type pools of free indices
            List<int>[] freeByType = new List<int>[behaviourCount];
            for (int i = 0; i < behaviourCount; i += 1) {
                freeByType[i] = new List<int>();
            }

            int totalAgents = agentBehaviourIds.Length;
            for (int i = 0; i < totalAgents; i += 1) {
                int typeIndex = agentBehaviourIds[i];
                if ((uint)typeIndex >= (uint)behaviourCount) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"FlockMainSpawner.AssignInitialPositions: agentBehaviourIds[{i}] = {typeIndex} is out of range.",
                        this);
                    continue;
                }

                freeByType[typeIndex].Add(i);
            }

            static int PopIndex(List<int> list) {
                int last = list.Count - 1;
                int value = list[last];
                list.RemoveAt(last);
                return value;
            }

            // --- 1) Point-based spawns (highest priority) ---
            if (pointSpawns != null) {
                for (int cfgIndex = 0; cfgIndex < pointSpawns.Length; cfgIndex += 1) {
                    PointSpawnConfig cfg = pointSpawns[cfgIndex];
                    if (cfg == null
                        || cfg.point == null
                        || cfg.types == null
                        || cfg.types.Length == 0) {
                        continue;
                    }

                    // Derive a stable per-config seed from globalSeed + index
                    uint seed = DeriveSeed(globalSeed, (uint)(cfgIndex + 1), 0x9E3779B9u);
                    Random rng = new Random(seed);

                    for (int t = 0; t < cfg.types.Length; t += 1) {
                        TypeCountEntry entry = cfg.types[t];
                        if (entry.preset == null || entry.count <= 0) {
                            continue;
                        }

                        if (!presetToIndex.TryGetValue(entry.preset, out int typeIndex)) {
                            FlockLog.Warning(
                                this,
                                FlockLogCategory.Controller,
                                $"FlockMainSpawner.AssignInitialPositions: preset '{entry.preset.name}' in PointSpawn[{cfgIndex}] is not in fishTypes.",
                                this);
                            continue;
                        }

                        List<int> pool = freeByType[typeIndex];
                        int toSpawn = math.min(entry.count, pool.Count);

                        if (toSpawn < entry.count) {
                            FlockLog.Warning(
                                this,
                                FlockLogCategory.Controller,
                                $"PointSpawn[{cfgIndex}] requested {entry.count} agents of type '{entry.preset.name}' but only {pool.Count} were available.",
                                this);
                        }

                        for (int c = 0; c < toSpawn; c += 1) {
                            int agentIndex = PopIndex(pool);
                            float3 pos = cfg.point.SamplePosition(ref rng);
                            pos = ClampToBounds(pos, environment);
                            positions[agentIndex] = pos;
                        }
                    }
                }
            }

            // --- 2) Seed-based spawns (scatter in entire bounds) ---
            if (seedSpawns != null) {
                for (int cfgIndex = 0; cfgIndex < seedSpawns.Length; cfgIndex += 1) {
                    SeedSpawnConfig cfg = seedSpawns[cfgIndex];
                    if (cfg == null
                        || cfg.types == null
                        || cfg.types.Length == 0) {
                        continue;
                    }

                    uint baseSeed = cfg.seed != 0u
                        ? cfg.seed
                        : DeriveSeed(globalSeed, (uint)(cfgIndex + 1), 0xBB67AE85u);

                    if (baseSeed == 0u) {
                        baseSeed = 1u;
                    }

                    Random rng = new Random(baseSeed);

                    for (int t = 0; t < cfg.types.Length; t += 1) {
                        TypeCountEntry entry = cfg.types[t];
                        if (entry.preset == null || entry.count <= 0) {
                            continue;
                        }

                        if (!presetToIndex.TryGetValue(entry.preset, out int typeIndex)) {
                            FlockLog.Warning(
                                this,
                                FlockLogCategory.Controller,
                                $"FlockMainSpawner.AssignInitialPositions: preset '{entry.preset.name}' in SeedSpawn[{cfgIndex}] is not in fishTypes.",
                                this);
                            continue;
                        }

                        List<int> pool = freeByType[typeIndex];
                        int toSpawn = math.min(entry.count, pool.Count);

                        if (toSpawn < entry.count) {
                            FlockLog.Warning(
                                this,
                                FlockLogCategory.Controller,
                                $"SeedSpawn[{cfgIndex}] requested {entry.count} agents of type '{entry.preset.name}' but only {pool.Count} were available.",
                                this);
                        }

                        for (int c = 0; c < toSpawn; c += 1) {
                            int agentIndex = PopIndex(pool);
                            float3 pos = SampleInBounds(ref rng, environment);
                            positions[agentIndex] = pos;
                        }
                    }
                }
            }

            // --- 3) Fallback: scatter any remaining agents uniformly in bounds ---
            for (int typeIndex = 0; typeIndex < freeByType.Length; typeIndex += 1) {
                List<int> pool = freeByType[typeIndex];
                if (pool.Count == 0) {
                    continue;
                }

                uint seed = DeriveSeed(globalSeed, (uint)(typeIndex + 1), 0x3C6EF372u);
                Random rng = new Random(seed == 0u ? 1u : seed);

                for (int i = pool.Count - 1; i >= 0; i -= 1) {
                    int agentIndex = pool[i];
                    float3 pos = SampleInBounds(ref rng, environment);
                    positions[agentIndex] = pos;
                }
            }
        }

        static uint DeriveSeed(uint baseSeed, uint salt, uint prime) {
            uint seed = baseSeed ^ (salt * prime);
            if (seed == 0u) {
                seed = 1u;
            }
            return seed;
        }

        static float3 SampleInBounds(ref Random rng, FlockEnvironmentData environment) {
            float3 center = environment.BoundsCenter;
            float3 extents = environment.BoundsExtents;
            float3 min = center - extents;
            float3 size = extents * 2f;

            float3 r = new float3(
                rng.NextFloat(),
                rng.NextFloat(),
                rng.NextFloat());

            return min + r * size;
        }

        static float3 ClampToBounds(float3 position, FlockEnvironmentData environment) {
            float3 center = environment.BoundsCenter;
            float3 extents = environment.BoundsExtents;
            float3 min = center - extents;
            float3 max = center + extents;

            return math.clamp(position, min, max);
        }

#if UNITY_EDITOR
        // Editor-only helper: sync spawn type lists with a canonical FishTypePreset array.
        // - One entry per preset in sourceTypes.
        // - Preserve existing counts where presets match.
        // - Drop presets that are not in sourceTypes.
        // - New presets get count = 0.
        public void EditorSyncTypesFrom(FishTypePreset[] sourceTypes) {
            if (sourceTypes == null) {
                return;
            }

            UnityEditor.Undo.RecordObject(this, "Sync Flock Spawner Types");

            if (pointSpawns != null) {
                for (int i = 0; i < pointSpawns.Length; i += 1) {
                    PointSpawnConfig cfg = pointSpawns[i];
                    if (cfg == null) {
                        continue;
                    }

                    cfg.types = SyncEntryArray(cfg.types, sourceTypes);
                }
            }

            if (seedSpawns != null) {
                for (int i = 0; i < seedSpawns.Length; i += 1) {
                    SeedSpawnConfig cfg = seedSpawns[i];
                    if (cfg == null) {
                        continue;
                    }

                    cfg.types = SyncEntryArray(cfg.types, sourceTypes);
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
        }

        TypeCountEntry[] SyncEntryArray(TypeCountEntry[] current, FishTypePreset[] sourceTypes) {
            if (sourceTypes == null || sourceTypes.Length == 0) {
                return System.Array.Empty<TypeCountEntry>();
            }

            var result = new TypeCountEntry[sourceTypes.Length];

            for (int i = 0; i < sourceTypes.Length; i += 1) {
                FishTypePreset preset = sourceTypes[i];
                int count = 0;

                if (current != null) {
                    for (int j = 0; j < current.Length; j += 1) {
                        if (current[j].preset == preset) {
                            count = current[j].count;
                            break;
                        }
                    }
                }

                result[i].preset = preset;
                result[i].count = count;
            }

            return result;
        }
#endif

    }
}
