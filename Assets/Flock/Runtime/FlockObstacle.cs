// REPLACE FILE: Assets/Flock/Runtime/FlockObstacle.cs
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Unity.Mathematics;
    using UnityEngine;

    public sealed class FlockObstacle : MonoBehaviour {
        [SerializeField]
        FlockObstacleShape shape = FlockObstacleShape.Sphere;

        [Header("Sphere Settings")]
        [SerializeField]
        float sphereRadius = 1.0f;

        [Header("Box Settings")]
        [SerializeField]
        Vector3 boxSize = Vector3.one;

        public FlockObstacleShape Shape => shape;
        public float SphereRadius => sphereRadius;
        public Vector3 BoxSize => boxSize;

        public FlockObstacleData ToData() {
            FlockObstacleData data;

            data.Shape = shape;
            data.Position = transform.position;

            if (shape == FlockObstacleShape.Sphere) {
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

                quaternion rotation = transform.rotation;
                data.BoxRotation = rotation;

                // Bounding sphere radius for cheap broad-phase
                data.Radius = math.length(halfExtents);
            }

            return data;
        }

        void OnDrawGizmos() {
            Gizmos.color = Color.yellow;

            if (shape == FlockObstacleShape.Sphere) {
                Gizmos.DrawWireSphere(
                    transform.position,
                    sphereRadius);
            } else {
                Matrix4x4 previous = Gizmos.matrix;

                Gizmos.matrix = Matrix4x4.TRS(
                    transform.position,
                    transform.rotation,
                    Vector3.one);

                Gizmos.DrawWireCube(
                    Vector3.zero,
                    boxSize);

                Gizmos.matrix = previous;
            }
        }
    }
}
