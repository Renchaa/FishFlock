using UnityEngine;

namespace Flock.Scripts.Build.Core.Simulation.Data
{
    [System.Serializable]
    public sealed class SeedSpawnConfig
    {
        [Tooltip("If enabled, overrides the global seed for this scatter batch.")]
        public bool useSeed;

        [Tooltip("Base seed for this global scatter batch. When Use Seed is disabled or seed is 0, a seed is derived from the Global Seed.")]
        public uint seed;

        [Tooltip("List of (preset + count) entries spawned in bounds for this batch.")]
        public TypeCountEntry[] types;
    }
}