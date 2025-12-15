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
        [Header("Fish Types / Presets")]
        [Tooltip("Canonical list of FishTypePreset assets used by this setup. " +
                 "Index here defines behaviour ids for controller, matrix, spawner, etc.")]
        public List<FishTypePreset> FishTypes = new();

        [Header("Interaction / Relationships (Phase 2)")]
        [Tooltip("Interaction matrix asset (avoid / neutral / attract, leadership, etc.).")]
        public FishInteractionMatrix InteractionMatrix;

        [Header("Group Noise / Patterns (Phase 3)")]
        [Tooltip("Group noise configuration asset (used to drive GroupNoiseFieldJob). " +
                 "Type will be specialised later (e.g. FlockGroupNoisePatternSettings asset).")]
        public GroupNoisePatternProfile GroupNoiseSettings;

        [Tooltip("Optional additional pattern assets for layer-3 pattern jobs (sphere, vortex, etc.).")]
        public List<FlockLayer3PatternProfile> PatternAssets = new();
    }
}
