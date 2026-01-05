using System.Collections.Generic;
using UnityEngine;

namespace Flock.Runtime {
    /**
     * <summary>
     * Central configuration asset for the flock system.
     * Holds species/type ordering plus references to interaction and pattern assets.
     * </summary>
     */
    [CreateAssetMenu(
        fileName = "FlockSetup",
        menuName = "Flock/Flock Setup",
        order = 0)]
    public sealed class FlockSetup : ScriptableObject {
        [Header("Fish Types / Presets")]

        [Tooltip("Canonical list of FishTypePreset assets used by this setup. Index here defines behaviour ids for controller, matrix, spawner, etc.")]
        public List<FishTypePreset> FishTypes = new();

        [Header("Interaction / Relationships")]

        [Tooltip("Interaction matrix asset (avoid / neutral / attract, leadership, etc.).")]
        public FishInteractionMatrix InteractionMatrix;

        [Header("Group Noise / Patterns")]

        [Tooltip("Group noise configuration asset used to drive the group noise field job.")]
        public GroupNoisePatternProfile GroupNoiseSettings;

        [Tooltip("Optional additional pattern assets for layer-3 pattern jobs (sphere, vortex, etc.).")]
        public List<FlockLayer3PatternProfile> PatternAssets = new();
    }
}
