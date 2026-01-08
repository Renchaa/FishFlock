using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Flock.Scripts.Build.Influence.Environment.Obstacles.Runtime {
    /**
     * <summary>
     * Scene obstacle definition that can be converted into a runtime <see cref="FlockObstacleData"/> snapshot.
     * </summary>
     */
    public sealed class FlockObstacle : MonoBehaviour {
        [Tooltip("Obstacle shape used when converting to runtime obstacle data.")]
        [SerializeField]
        private FlockObstacleShape shape = FlockObstacleShape.Sphere;

        [Header("Sphere Settings")]

        [Tooltip("Sphere radius in world units.")]
        [SerializeField]
        private float sphereRadius = 1.0f;

        [Header("Box Settings")]

        [Tooltip("Box size in world units (full size, not half-extents).")]
        [SerializeField]
        private Vector3 boxSize = Vector3.one;

        private void OnDrawGizmos() {
            Gizmos.color = Color.yellow;

            if (shape == FlockObstacleShape.Sphere) {
                Gizmos.DrawWireSphere(transform.position, sphereRadius);
                return;
            }

            Matrix4x4 previousGizmosMatrix = Gizmos.matrix;

            Gizmos.matrix = Matrix4x4.TRS(
                transform.position,
                transform.rotation,
                Vector3.one);

            Gizmos.DrawWireCube(Vector3.zero, boxSize);

            Gizmos.matrix = previousGizmosMatrix;
        }

        /**
         * <summary>
         * Converts this component state into a runtime <see cref="FlockObstacleData"/> snapshot.
         * </summary>
         * <returns>The populated <see cref="FlockObstacleData"/>.</returns>
         */
        public FlockObstacleData ToData() {
            FlockObstacleData data;

            data.Shape = shape;
            data.Position = transform.position;

            if (shape == FlockObstacleShape.Sphere) {
                float radius = math.max(0.0f, sphereRadius);

                data.Radius = radius;
                data.BoxHalfExtents = float3.zero;
                data.BoxRotation = quaternion.identity;

                return data;
            }

            float3 halfExtents = new float3(
                math.max(0.0f, boxSize.x * 0.5f),
                math.max(0.0f, boxSize.y * 0.5f),
                math.max(0.0f, boxSize.z * 0.5f));

            data.BoxHalfExtents = halfExtents;

            quaternion rotation = transform.rotation;
            data.BoxRotation = rotation;

            // Bounding sphere radius for cheap broad-phase.
            data.Radius = math.length(halfExtents);

            return data;
        }
    }
}
