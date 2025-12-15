// File: Assets/BubbleFollower.cs
using UnityEngine;
using Flock.Runtime;
using Flock.Runtime.Data;
using Flock.Runtime.Patterns;

public sealed class BubbleFollower : MonoBehaviour {
    [SerializeField] FlockController flock;
    [SerializeField] Layer3SphereShellPatternProfile sphereShellProfile;
    [SerializeField] KeyCode toggleKey = KeyCode.Space;

    FlockLayer3PatternToken token;

    void Update() {
        if (Input.GetKeyDown(toggleKey)) {
            if (token != null && token.IsValid) {
                flock.StopLayer3Pattern(token);
                token = null;
            } else {
                token = flock.StartLayer3Pattern(sphereShellProfile);
            }
        }
    }

    void OnDisable() {
        if (token != null && token.IsValid && flock != null) {
            flock.StopLayer3Pattern(token);
        }
        token = null;
    }
}
