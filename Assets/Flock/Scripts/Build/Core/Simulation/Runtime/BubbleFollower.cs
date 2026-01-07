using Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController;
using Flock.Scripts.Build.Influence.PatternVolume.Data;
using Flock.Scripts.Build.Influence.PatternVolume.Profile;

using UnityEngine;

namespace Flock.Scripts.Build.Core.Simulation.Runtime {

    public sealed class BubbleFollower : MonoBehaviour {
        [SerializeField] FlockController flock;
        [SerializeField] PatternVolumeSphereShellProfile sphereShellProfile;
        [SerializeField] KeyCode toggleKey = KeyCode.Space;

        PatternVolumeToken token;

        void Update() {
            if (Input.GetKeyDown(toggleKey)) {
                if (token != null && token.IsValid) {
                    flock.StopPatternVolume(token);
                    token = null;
                } else {
                    token = flock.StartPatternVolume(sphereShellProfile);
                }
            }
        }

        void OnDisable() {
            if (token != null && token.IsValid && flock != null) {
                flock.StopPatternVolume(token);
            }
            token = null;
        }
    }
}