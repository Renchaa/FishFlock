using System.Collections.Generic;
using UnityEngine;

namespace Flock.Runtime {
    /// <summary>
    /// Central config asset for the flock system.
    /// Phase 1: mainly holds species (FishBehaviourProfile) plus placeholders
    /// for interaction matrix and noise/pattern assets (wired in later phases).
    /// </summary>
    [CreateAssetMenu(
        fileName = "FlockSetup",
        menuName = "Flock/Flock Setup",
        order = 0)]
    public sealed class FlockSetup : ScriptableObject {
        [Header("Species / Behaviour Profiles")]
        [Tooltip("All fish behaviour profiles used by this flock setup. " +
                 "Index here should match behaviour indices in your runtime arrays.")]
        public List<FishBehaviourProfile> Species = new();

        [Header("Interaction / Relationships (Phase 2)")]
        [Tooltip("Interaction matrix asset (avoid / neutral / attract, leadership, etc.).")]
        public FishInteractionMatrix InteractionMatrix;

        [Header("Group Noise / Patterns (Phase 3)")]
        [Tooltip("Group noise configuration asset (used to drive GroupNoiseFieldJob). " +
                 "Type will be specialised later (e.g. FlockGroupNoisePatternSettings asset).")]
        public GroupNoisePatternProfile GroupNoiseSettings;

        [Tooltip("Optional additional pattern assets for layer-3 pattern jobs (sphere, vortex, etc.).")]
        public List<ScriptableObject> PatternAssets = new();
    }
}
