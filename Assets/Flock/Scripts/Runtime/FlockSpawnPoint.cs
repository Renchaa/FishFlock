using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Flock.Runtime {
    /**
     * <summary>
     * Supported geometric spawn shapes.
     * </summary>
     */
    public enum FlockSpawnShape {
        Point,
        Sphere,
        Box
    }

    /**
     * <summary>
     * Defines a geometric spawn region in world space.
     * Counts and fish types are defined on <see cref="FlockMainSpawner"/>, not here.
     * </summary>
     */
    public sealed class FlockSpawnPoint : MonoBehaviour {
        [Header("Shape")]

        [Tooltip("Spawn shape used when sampling positions.")]
        [SerializeField]
        private FlockSpawnShape shape = FlockSpawnShape.Point;

        [Tooltip("Sphere radius used when Shape is Sphere.")]
        [SerializeField]
        [Min(0f)]
        private float radius = 1.0f;

        [Tooltip("Box half-extents used when Shape is Box.")]
        [SerializeField]
        private Vector3 halfExtents = new Vector3(1.0f, 1.0f, 1.0f);

        /**
         * <summary>
         * Samples a world-space position inside this spawn shape. No clamping to flock bounds is done here.
         * </summary>
         * <param name="random">Random generator used for sampling.</param>
         * <returns>A sampled world-space position.</returns>
         */
        public float3 SamplePosition(ref Random random) {
            float3 centerPosition = (float3)transform.position;

            switch (shape) {
                case FlockSpawnShape.Point:
                    return centerPosition;

                case FlockSpawnShape.Sphere:
                    return SampleInsideSphere(ref random, centerPosition, radius);

                case FlockSpawnShape.Box:
                    return SampleInsideBox(ref random, centerPosition, transform.rotation, halfExtents);

                default:
                    return centerPosition;
            }
        }

        private void OnDrawGizmosSelected() {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);

            switch (shape) {
                case FlockSpawnShape.Point:
                    Gizmos.DrawSphere(transform.position, 0.1f);
                    return;

                case FlockSpawnShape.Sphere:
                    Gizmos.DrawWireSphere(transform.position, radius);
                    return;

                case FlockSpawnShape.Box:
                    Gizmos.DrawWireCube(transform.position, halfExtents * 2f);
                    return;
            }
        }

        private static float3 SampleInsideSphere(ref Random random, float3 centerPosition, float radius) {
            if (radius <= 0f) {
                return centerPosition;
            }

            // Uniform inside sphere: direction * radius * cbrt(u).
            float3 direction = new float3(0f, 0f, 1f);

            for (int attemptIndex = 0; attemptIndex < 4; attemptIndex += 1) {
                float3 candidate = new float3(
                    random.NextFloat(-1f, 1f),
                    random.NextFloat(-1f, 1f),
                    random.NextFloat(-1f, 1f));

                float lengthSquared = math.lengthsq(candidate);
                if (lengthSquared > 1e-6f) {
                    direction = candidate / math.sqrt(lengthSquared);
                    break;
                }
            }

            float unit = math.saturate(random.NextFloat());
            float sampledRadius = radius * math.pow(unit, 1f / 3f);

            return centerPosition + direction * sampledRadius;
        }

        private static float3 SampleInsideBox(
            ref Random random,
            float3 centerPosition,
            Quaternion rotation,
            Vector3 halfExtents) {
            Vector3 localOffset = new Vector3(
                random.NextFloat(-halfExtents.x, halfExtents.x),
                random.NextFloat(-halfExtents.y, halfExtents.y),
                random.NextFloat(-halfExtents.z, halfExtents.z));

            Vector3 worldOffset = rotation * localOffset;
            return centerPosition + (float3)worldOffset;
        }
    }
}
