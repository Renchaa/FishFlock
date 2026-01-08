using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
using Flock.Scripts.Build.Agents.Fish.Profiles;

using System;
using UnityEngine;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Environment.Attractors.Runtime
{
    /**
     * <summary>
     * Scene component that defines an attractor volume and converts it into a
     * <see cref="AttractorData"/> snapshot for runtime use.
     * </summary>
     */
    public sealed class FlockAttractorArea : MonoBehaviour
    {
        [SerializeField]
        private FlockAttractorShape shape = FlockAttractorShape.Sphere;

        [Header("Sphere Settings")]
        [SerializeField]
        private float sphereRadius = 3.0f;

        [Header("Box Settings")]
        [SerializeField]
        private Vector3 boxSize = new Vector3(6.0f, 4.0f, 6.0f);

        [Header("Attraction")]
        [SerializeField]
        [Min(0f)]
        private float baseStrength = 1.0f;

        [SerializeField]
        [Range(0.1f, 4f)]
        private float falloffPower = 1.0f;

        [SerializeField]
        private FishTypePreset[] attractedTypes = Array.Empty<FishTypePreset>();

        [Header("Usage")]
        [SerializeField]
        private AttractorUsage usage = AttractorUsage.Individual;

        [Tooltip("Higher value wins when multiple attractors overlap the same grid cells.")]
        [SerializeField]
        [Min(0f)]
        private float cellPriority = 1.0f;

        public FishTypePreset[] AttractedTypes => attractedTypes;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.0f, 0.8f, 1.0f, 0.75f);

            if (shape == FlockAttractorShape.Sphere)
            {
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
         * Converts this component state into a runtime <see cref="AttractorData"/> snapshot.
         * </summary>
         * <param name="affectedTypesMask">Mask of fish types affected by this attractor (0 means all).</param>
         * <returns>The populated <see cref="AttractorData"/>.</returns>
         */
        public FlockAttractorData ToData(uint affectedTypesMask)
        {
            FlockAttractorData data = CreateBaseData(affectedTypesMask);
            ApplyShapeData(ref data);
            return data;
        }

        private FlockAttractorData CreateBaseData(uint affectedTypesMask)
        {
            FlockAttractorData data = default;

            data.Shape = shape;
            data.Position = transform.position;

            data.BaseStrength = Mathf.Max(0f, baseStrength);
            data.FalloffPower = Mathf.Max(0.1f, falloffPower);
            data.AffectedTypesMask = affectedTypesMask == 0u ? uint.MaxValue : affectedTypesMask;

            data.Usage = usage;
            data.CellPriority = Mathf.Max(0f, cellPriority);

            return data;
        }

        private void ApplyShapeData(ref FlockAttractorData data)
        {
            if (shape == FlockAttractorShape.Sphere)
            {
                ApplySphereData(ref data);
                return;
            }

            ApplyBoxData(ref data);
        }

        private void ApplySphereData(ref FlockAttractorData data)
        {
            float radius = math.max(0.0f, sphereRadius);

            data.Radius = radius;
            data.BoxHalfExtents = float3.zero;
            data.BoxRotation = quaternion.identity;
        }

        private void ApplyBoxData(ref FlockAttractorData data)
        {
            float3 halfExtents = new float3(
                math.max(0.0f, boxSize.x * 0.5f),
                math.max(0.0f, boxSize.y * 0.5f),
                math.max(0.0f, boxSize.z * 0.5f));

            data.BoxHalfExtents = halfExtents;
            data.BoxRotation = transform.rotation;

            // Bounding sphere radius for broad-phase / stamping.
            data.Radius = math.length(halfExtents);
        }
    }
}
