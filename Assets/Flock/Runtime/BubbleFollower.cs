using UnityEngine;

public class BubbleFollower : MonoBehaviour {
    [SerializeField] Flock.Runtime.FlockController flock;
    [SerializeField] Transform target;
    [SerializeField] float bubbleRadius = 8f;

    void Update() {
        if (flock != null && target != null) {
            flock.SetPatternBubbleCenter(target.position, bubbleRadius, 1f, 100f);
        }
    }
}
