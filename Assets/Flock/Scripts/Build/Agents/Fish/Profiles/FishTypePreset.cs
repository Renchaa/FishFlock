using Flock.Scripts.Build.Agents.Fish.Data;

using UnityEngine;

namespace Flock.Scripts.Build.Agents.Fish.Profiles
{
    [CreateAssetMenu(
        fileName = "FishTypePreset",
        menuName = "Flock/Fish Type Preset")]
    public sealed class FishTypePreset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] string displayName;

        [Header("Behaviour")]
        [SerializeField] FishBehaviourProfile behaviourProfile;

        [Header("Visuals")]
        [SerializeField] GameObject prefab;

        public string DisplayName => displayName;
        public FishBehaviourProfile BehaviourProfile => behaviourProfile;
        public GameObject Prefab => prefab;

        public FlockBehaviourSettings ToSettings()
        {
            return behaviourProfile != null
                ? behaviourProfile.ToSettings()
                : default;
        }
    }
}
