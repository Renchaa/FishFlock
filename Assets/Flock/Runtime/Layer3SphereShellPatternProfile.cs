using System.Collections.Generic;
using Flock.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Flock.Runtime.Patterns {
    /**
     * <summary>
     * Layer-3 pattern profile that bakes a sphere shell pattern payload and command.
     * </summary>
     */
    [CreateAssetMenu(
        menuName = "Flock/Layer-3 Patterns/Sphere Shell",
        fileName = "Layer3_SphereShell")]
    public sealed class Layer3SphereShellPatternProfile : FlockLayer3PatternProfile {
        [Header("Sphere Shell")]

        [Tooltip("If true, uses the environment bounds center as the sphere shell center.")]
        [SerializeField]
        private bool useBoundsCenter = true;

        [Tooltip("Normalised center in bounds space [0..1] when Use Bounds Center is false.")]
        [SerializeField]
        private Vector3 centerNorm = new Vector3(0.5f, 0.5f, 0.5f);

        [Tooltip("Sphere radius in world units.")]
        [SerializeField]
        [Min(0f)]
        private float radius = 5f;

        [Tooltip("Shell thickness in world units. <= 0 means auto = radius * 0.25.")]
        [SerializeField]
        private float thickness = -1f;

        /**
         * <summary>
         * Gets the baked pattern kind.
         * </summary>
         */
        public override FlockLayer3PatternKind Kind => FlockLayer3PatternKind.SphereShell;

        /**
         * <summary>
         * Bakes this profile into a command and sphere-shell payload.
         * </summary>
         * <param name="env">Environment data used to resolve world-space positions.</param>
         * <param name="behaviourMask">Compiled mask of fish types affected by this pattern.</param>
         * <param name="commands">Command output list.</param>
         * <param name="sphereShellPayloads">Sphere-shell payload output list.</param>
         * <param name="boxShellPayloads">Box-shell payload output list (unused by this profile).</param>
         */
        protected override void BakeInternal(
            in FlockEnvironmentData env,
            uint behaviourMask,
            List<FlockLayer3PatternCommand> commands,
            List<FlockLayer3PatternSphereShell> sphereShellPayloads,
            List<FlockLayer3PatternBoxShell> boxShellPayloads) {
            EnsureMinimumRadius();

            float safeThickness = GetSafeThickness(radius, thickness);

            float3 center = useBoundsCenter
                ? env.BoundsCenter
                : BoundsNormToWorld(env, (float3)centerNorm);

            int payloadIndex = sphereShellPayloads.Count;

            sphereShellPayloads.Add(new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = radius,
                Thickness = safeThickness,
            });

            commands.Add(new FlockLayer3PatternCommand {
                Kind = Kind,
                PayloadIndex = payloadIndex,
                Strength = Strength,
                BehaviourMask = behaviourMask,
            });
        }

        // Preserves existing behaviour: mutates the serialized radius field when <= 0.
        private void EnsureMinimumRadius() {
            if (radius <= 0f) {
                radius = 0.1f;
            }
        }

        private static float GetSafeThickness(float radius, float thickness) {
            float computedThickness = thickness <= 0f
                ? radius * 0.25f
                : thickness;

            return math.max(0.001f, computedThickness);
        }
    }
}
