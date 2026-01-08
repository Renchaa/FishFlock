using Flock.Scripts.Build.Agents.Fish.Profiles;

using UnityEngine;

namespace Flock.Scripts.Build.Core.Simulation.Data
{
    [System.Serializable]
    public struct TypeCountEntry
    {
        [Tooltip("Fish type preset to spawn.")]
        public FishTypePreset preset;

        [Tooltip("Number of agents to spawn for this preset.")]
        [Min(0)]
        public int count;
    }
}