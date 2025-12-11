// File: Assets/Flock/Runtime/FlockSpawnPoint.cs
namespace Flock.Runtime {
    using Unity.Mathematics;
    using UnityEngine;
    using Random = Unity.Mathematics.Random;

    public enum FlockSpawnShape {
        Point,
        Sphere,
        Box
    }

    /// <summary>
    /// Defines a geometric spawn region in world space.
    /// Counts and fish types are defined on FlockMainSpawner, not here.
    /// </summary>
    public sealed class FlockSpawnPoint : MonoBehaviour {
        [Header("Shape")]
        [SerializeField] FlockSpawnShape shape = FlockSpawnShape.Point;

        [SerializeField, Min(0f)]
        float radius = 1.0f; // Used for Sphere

        [SerializeField]
        Vector3 halfExtents = new Vector3(1.0f, 1.0f, 1.0f); // Used for Box

        public FlockSpawnShape Shape => shape;
        public float Radius => radius;
        public Vector3 HalfExtents => halfExtents;

        /// <summary>
        /// Samples a world-space position inside this spawn shape.
        /// No clamping to flock bounds is done here.
        /// </summary>
        public float3 SamplePosition(ref Random rng) {
            float3 center = (float3)transform.position;

            switch (shape) {
                case FlockSpawnShape.Point:
                    return center;

                case FlockSpawnShape.Sphere:
                    return SampleInsideSphere(ref rng, center, radius);

                case FlockSpawnShape.Box:
                    return SampleInsideBox(ref rng, center, transform.rotation, halfExtents);

                default:
                    return center;
            }
        }

        static float3 SampleInsideSphere(ref Random rng, float3 center, float radius) {
            if (radius <= 0f) {
                return center;
            }

            // Uniform inside sphere: direction * radius * cbrt(u)
            float3 dir = new float3(0f, 0f, 1f);

            for (int attempt = 0; attempt < 4; attempt += 1) {
                float3 candidate = new float3(
                    rng.NextFloat(-1f, 1f),
                    rng.NextFloat(-1f, 1f),
                    rng.NextFloat(-1f, 1f));

                float lenSq = math.lengthsq(candidate);
                if (lenSq > 1e-6f) {
                    dir = candidate / math.sqrt(lenSq);
                    break;
                }
            }

            float u = math.saturate(rng.NextFloat());
            float r = radius * math.pow(u, 1f / 3f);

            return center + dir * r;
        }

        static float3 SampleInsideBox(
            ref Random rng,
            float3 center,
            Quaternion rotation,
            Vector3 halfExtents) {

            Vector3 local = new Vector3(
                rng.NextFloat(-halfExtents.x, halfExtents.x),
                rng.NextFloat(-halfExtents.y, halfExtents.y),
                rng.NextFloat(-halfExtents.z, halfExtents.z));

            Vector3 worldOffset = rotation * local;
            return center + (float3)worldOffset;
        }

        #region Gizmos
        // Draw the spawn area based on shape
        private void OnDrawGizmosSelected() {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);  // Red with transparency for visibility

            switch (shape) {
                case FlockSpawnShape.Point:
                    Gizmos.DrawSphere(transform.position, 0.1f);  // Point visualized as a small sphere
                    break;

                case FlockSpawnShape.Sphere:
                    Gizmos.DrawWireSphere(transform.position, radius);  // Draw a wireframe sphere for radius
                    break;

                case FlockSpawnShape.Box:
                    Gizmos.DrawWireCube(transform.position, halfExtents * 2);  // Draw a wireframe box
                    break;
            }
        }
        #endregion
    }
}
