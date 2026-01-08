using Flock.Scripts.Build.Core.Simulation.Runtime.Spawn;

using UnityEngine;

namespace Flock.Scripts.Build.Core.Simulation.Data
{
    [System.Serializable]
    public sealed class PointSpawnConfig
    {
        [Tooltip("Spawn point shape used to sample positions.")]
        public FlockSpawnPoint point;

        [Tooltip("If enabled, overrides the global seed for this point spawn.")]
        public bool useSeed;

        [Tooltip("Explicit seed for this point spawn. Ignored when Use Seed is disabled or seed is 0.")]
        public uint seed;

        [Tooltip("List of (preset + count) entries spawned from this point.")]
        public TypeCountEntry[] types;
    }
}