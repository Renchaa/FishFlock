// File: Assets/Flock/Runtime/Debug/FlockDebugRadii.cs
using UnityEngine;
using Flock.Runtime;

[ExecuteAlways]
public sealed class FlockDebugRadii : MonoBehaviour {
    [Header("Source")]
    [SerializeField] FishBehaviourProfile profile;

    [Header("What to draw")]
    [SerializeField] bool drawBodyRadius = true;
    [SerializeField] bool drawNeighbourRadius = true;
    [SerializeField] bool drawSeparationRadius = true;
    [SerializeField] bool drawGridSearchRadius = false;

    [Header("Visuals")]
    [SerializeField] Color bodyColor = new Color(0f, 1f, 1f, 0.8f);   // cyan
    [SerializeField] Color neighbourColor = new Color(0f, 1f, 0f, 0.6f);   // green
    [SerializeField] Color separationColor = new Color(1f, 0f, 0f, 0.6f);   // red
    [SerializeField] Color gridSearchColor = new Color(1f, 0.5f, 0f, 0.6f); // orange

    [Header("Grid debug (optional)")]
    [Tooltip("Cell size from your FlockEnvironmentData; only needed to visualise grid search radius.")]
    [SerializeField] float cellSize = 1.0f;

#if UNITY_EDITOR
    void OnDrawGizmos() {
        if (profile == null) {
            return;
        }

        var settings = profile.ToSettings();
        Vector3 p = transform.position;

        // 1) Body radius (physical size)
        if (drawBodyRadius) {
            Gizmos.color = bodyColor;
            Gizmos.DrawWireSphere(p, settings.BodyRadius);
        }

        // 2) Neighbour radius (how far they see others)
        if (drawNeighbourRadius) {
            Gizmos.color = neighbourColor;
            Gizmos.DrawWireSphere(p, settings.NeighbourRadius);
        }

        // 3) Separation radius (hard "back off" bubble)
        if (drawSeparationRadius) {
            Gizmos.color = separationColor;
            Gizmos.DrawWireSphere(p, settings.SeparationRadius);
        }

        // 4) Grid search radius in world units (derived from neighbour radius & cell size)
        if (drawGridSearchRadius && cellSize > 0.0001f) {
            float viewRadius = Mathf.Max(0f, settings.NeighbourRadius);
            int cellRange = Mathf.Max(1, Mathf.CeilToInt(viewRadius / cellSize));

            float worldRadius = cellRange * cellSize;

            Gizmos.color = gridSearchColor;
            Gizmos.DrawWireSphere(p, worldRadius);

#if UNITY_EDITOR
            // optional label so you see the cellRange value
            UnityEditor.Handles.Label(
                p + Vector3.up * (worldRadius + 0.25f),
                $"cellRange = {cellRange}");
#endif
        }
    }
#endif
}
