using UnityEngine;
using Flock.Runtime;

public class BubbleFollower : MonoBehaviour {
    [SerializeField] FlockController flock;
    [SerializeField] Transform target;

    [Header("Bubble Settings")]
    [SerializeField] float bubbleRadius = 8f;
    [SerializeField] float bubbleThickness = 1f;
    [SerializeField] float bubbleStrength = 100f;

    [Header("Affected Fish Types (optional)")]
    [SerializeField] FishTypePreset[] affectedTypes;

    void Update() {
        if (flock == null || target == null) {
            return;
        }

        // If specific types are assigned → build bitmask from them.
        // If not → controller will use "all types" mask.
        if (affectedTypes != null) {
            flock.SetPatternBubbleCenter(
                target.position,
                bubbleRadius,
                bubbleThickness,
                bubbleStrength,
                affectedTypes);
        } else {
            flock.SetPatternBubbleCenter(
                target.position,
                bubbleRadius,
                bubbleThickness,
                bubbleStrength);
        }
    }

    void OnDisable() {
        // Make sure bubble is not left active when this follower is turned off.
        if (flock != null) {
            flock.ClearPatternBubble();
        }
    }
}
