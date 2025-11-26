// ==========================================
// 2) NEW COMPONENT
// File: Assets/Flock/Runtime/FlockAttractorArea.cs
// ==========================================
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Unity.Mathematics;
    using UnityEngine;

    public sealed class FlockAttractorArea : MonoBehaviour {
        [SerializeField]
        FlockAttractorShape shape = FlockAttractorShape.Sphere;

        [Header("Sphere Settings")]
        [SerializeField]
        float sphereRadius = 3.0f;

        [Header("Box Settings")]
        [SerializeField]
        Vector3 boxSize = new Vector3(6.0f, 4.0f, 6.0f);

        [Header("Attraction")]
        [SerializeField, Min(0f)]
        float baseStrength = 1.0f;

        // 0.1 = very soft spread, 1 = linear, 2+ = concentrated near center
        [SerializeField, Range(0.1f, 4f)]
        float falloffPower = 1.0f;

        // If empty => affects all fish types
        [SerializeField]
        FishTypePreset[] attractedTypes = System.Array.Empty<FishTypePreset>();

        public FlockAttractorShape Shape => shape;
        public float SphereRadius => sphereRadius;
        public Vector3 BoxSize => boxSize;
        public float BaseStrength => baseStrength;
        public float FalloffPower => falloffPower;
        public FishTypePreset[] AttractedTypes => attractedTypes;

        // Controller passes the bitmask it computed from AttractedTypes + its FishTypes[]
        public FlockAttractorData ToData(uint affectedTypesMask) {
            FlockAttractorData data;

            data.Shape = shape;
            data.Position = transform.position;

            data.BaseStrength = Mathf.Max(0f, baseStrength);
            data.FalloffPower = Mathf.Max(0.1f, falloffPower);
            data.AffectedTypesMask = affectedTypesMask == 0u ? uint.MaxValue : affectedTypesMask;

            if (shape == FlockAttractorShape.Sphere) {
                float radius = math.max(0.0f, sphereRadius);

                data.Radius = radius;
                data.BoxHalfExtents = float3.zero;
                data.BoxRotation = quaternion.identity;
            } else {
                float3 halfExtents = new float3(
                    math.max(0.0f, boxSize.x * 0.5f),
                    math.max(0.0f, boxSize.y * 0.5f),
                    math.max(0.0f, boxSize.z * 0.5f));

                data.BoxHalfExtents = halfExtents;
                data.BoxRotation = transform.rotation;

                // Bounding sphere radius for cheap radial falloff
                data.Radius = math.length(halfExtents);
            }

            return data;
        }

        void OnDrawGizmos() {
            Gizmos.color = new Color(0.0f, 0.8f, 1.0f, 0.75f);

            if (shape == FlockAttractorShape.Sphere) {
                Gizmos.DrawWireSphere(transform.position, sphereRadius);
            } else {
                Matrix4x4 previous = Gizmos.matrix;

                Gizmos.matrix = Matrix4x4.TRS(
                    transform.position,
                    transform.rotation,
                    Vector3.one);

                Gizmos.DrawWireCube(Vector3.zero, boxSize);

                Gizmos.matrix = previous;
            }
        }
    }
}
