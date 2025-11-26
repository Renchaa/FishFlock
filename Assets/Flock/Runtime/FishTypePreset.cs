// File: Assets/Flock/Runtime/FishTypePreset.cs

namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "FishTypePreset",
        menuName = "Flock/Fish Type Preset")]
    public sealed class FishTypePreset : ScriptableObject {
        [Header("Identity")]
        [SerializeField] string displayName;

        [Header("Behaviour")]
        [SerializeField] FishBehaviourProfile behaviourProfile;

        [Header("Visuals")]
        [SerializeField] GameObject prefab;

        [Header("Spawning")]
        [SerializeField, Min(0)] int spawnCount = 0;

        public string DisplayName => displayName;
        public FishBehaviourProfile BehaviourProfile => behaviourProfile;
        public GameObject Prefab => prefab;
        public int SpawnCount => spawnCount;

        public FlockBehaviourSettings ToSettings() {
            return behaviourProfile != null
                ? behaviourProfile.ToSettings()
                : default;
        }
    }
}
