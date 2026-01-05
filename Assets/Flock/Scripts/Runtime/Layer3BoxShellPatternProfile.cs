using System.Collections.Generic;
using Flock.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Flock.Runtime.Patterns {
    /**
     * <summary>
     * Layer-3 pattern profile that bakes a box shell pattern payload and command.
     * </summary>
     */
    [CreateAssetMenu(
        menuName = "Flock/Layer-3 Patterns/Box Shell",
        fileName = "Layer3_BoxShell")]
    public sealed class Layer3BoxShellPatternProfile : FlockLayer3PatternProfile {
        [Header("Box Shell")]

        [Tooltip("If true, uses the environment bounds center as the box shell center.")]
        [SerializeField]
        private bool useBoundsCenter = true;

        [Tooltip("Normalised center in bounds space [0..1] when Use Bounds Center is false.")]
        [SerializeField]
        private Vector3 centerNorm = new Vector3(0.5f, 0.5f, 0.5f);

        [Tooltip("Half-extents of the box shell in world units.")]
        [SerializeField]
        private Vector3 halfExtents = new Vector3(5f, 5f, 5f);

        [Tooltip("Shell thickness in world units. <= 0 means auto = min(halfExtents) * 0.25.")]
        [SerializeField]
        private float thickness = -1f;

        /**
         * <summary>
         * Gets the baked pattern kind.
         * </summary>
         */
        public override FlockLayer3PatternKind Kind => FlockLayer3PatternKind.BoxShell;

        /**
         * <summary>
         * Bakes this profile into a command and box-shell payload.
         * </summary>
         * <param name="env">Environment data used to resolve world-space positions.</param>
         * <param name="behaviourMask">Compiled mask of fish types affected by this pattern.</param>
         * <param name="commands">Command output list.</param>
         * <param name="sphereShellPayloads">Sphere-shell payload output list (unused by this profile).</param>
         * <param name="boxShellPayloads">Box-shell payload output list.</param>
         */
        protected override void BakeInternal(
            in FlockEnvironmentData env,
            uint behaviourMask,
            List<FlockLayer3PatternCommand> commands,
            List<FlockLayer3PatternSphereShell> sphereShellPayloads,
            List<FlockLayer3PatternBoxShell> boxShellPayloads) {
            if (!IsHalfExtentsValid(halfExtents)) {
                return;
            }

            float3 center = GetCenter(env, useBoundsCenter, centerNorm);
            float3 safeHalfExtents = GetSafeHalfExtents(halfExtents);
            float safeThickness = GetSafeThickness(thickness, safeHalfExtents);

            int payloadIndex = boxShellPayloads.Count;

            boxShellPayloads.Add(new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = safeHalfExtents,
                Thickness = safeThickness,
            });

            commands.Add(new FlockLayer3PatternCommand {
                Kind = Kind,
                PayloadIndex = payloadIndex,
                Strength = Strength,
                BehaviourMask = behaviourMask,
            });
        }

        private static bool IsHalfExtentsValid(Vector3 halfExtents) {
            return halfExtents.x > 0f
                && halfExtents.y > 0f
                && halfExtents.z > 0f;
        }

        private static float3 GetCenter(in FlockEnvironmentData env, bool useBoundsCenter, Vector3 centerNorm) {
            return useBoundsCenter
                ? env.BoundsCenter
                : BoundsNormToWorld(env, (float3)centerNorm);
        }

        private static float3 GetSafeHalfExtents(Vector3 halfExtents) {
            return new float3(
                math.max(halfExtents.x, 0.001f),
                math.max(halfExtents.y, 0.001f),
                math.max(halfExtents.z, 0.001f));
        }

        private static float GetSafeThickness(float thickness, float3 safeHalfExtents) {
            float computedThickness = thickness <= 0f
                ? math.cmin(safeHalfExtents) * 0.25f
                : thickness;

            return math.max(0.001f, computedThickness);
        }
    }
}
