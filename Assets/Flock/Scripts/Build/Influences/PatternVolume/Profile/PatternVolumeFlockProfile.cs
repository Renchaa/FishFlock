using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Flock.Scripts.Build.Agents.Fish.Profiles;
using Flock.Scripts.Build.Influence.PatternVolume.Data;
using Flock.Scripts.Build.Influence.Environment.Data;

namespace Flock.Scripts.Build.Influence.PatternVolume.Profiles {
    /**
     * <summary>
     * Base ScriptableObject for Layer-3 pattern profiles that compile into runtime pattern commands/payloads.
     * </summary>
     */
    public abstract class PatternVolumeFlockProfile : ScriptableObject {
        [Header("Common")]

        [Tooltip("If false, this profile contributes no pattern data during baking.")]
        [SerializeField]
        private bool enabled = true;

        [Tooltip("Overall strength multiplier applied to the baked pattern.")]
        [SerializeField]
        [Min(0f)]
        private float strength = 1f;

        [Tooltip("Optional filter. If null -> affects all fish types. If empty -> affects no fish types (max 32 supported by mask).")]
        [SerializeField]
        private FishTypePreset[] affectedTypes;

        /**
         * <summary>
         * Gets whether this profile is enabled for baking.
         * </summary>
         */
        public bool Enabled => enabled;

        /**
         * <summary>
         * Gets the strength multiplier applied by this profile.
         * </summary>
         */
        public float Strength => strength;

        /**
         * <summary>
         * Gets the fish type filter used when building the behaviour mask.
         * </summary>
         */
        public FishTypePreset[] AffectedTypes => affectedTypes;

        /**
         * <summary>
         * Gets the pattern kind produced by this profile.
         * </summary>
         */
        public abstract PatternVolumeKind Kind { get; }

        /**
         * <summary>
         * Compiles this profile into runtime commands and payloads. Called by the controller during simulation initialization.
         * </summary>
         * <param name="env">Environment data used for baking.</param>
         * <param name="controllerFishTypes">Fish type ordering used by the controller.</param>
         * <param name="commands">Command output list.</param>
         * <param name="sphereShellPayloads">Sphere-shell payload output list.</param>
         * <param name="boxShellPayloads">Box-shell payload output list.</param>
         */
        public void Bake(
            in FlockEnvironmentData env,
            FishTypePreset[] controllerFishTypes,
            List<PatternVolumeCommand> commands,
            List<PatternVolumeSphereShell> sphereShellPayloads,
            List<PatternVolumeBoxShell> boxShellPayloads) {
            if (!enabled || strength <= 0f) {
                return;
            }

            uint behaviourMask = BuildBehaviourMask(controllerFishTypes, affectedTypes);

            if (behaviourMask == 0u) {
                return;
            }

            BakeInternal(
                env,
                behaviourMask,
                commands,
                sphereShellPayloads,
                boxShellPayloads);
        }

        /**
         * <summary>
         * Performs profile-specific baking into runtime commands and payloads.
         * </summary>
         * <param name="env">Environment data used for baking.</param>
         * <param name="behaviourMask">Compiled mask of fish types affected by this profile.</param>
         * <param name="commands">Command output list.</param>
         * <param name="sphereShellPayloads">Sphere-shell payload output list.</param>
         * <param name="boxShellPayloads">Box-shell payload output list.</param>
         */
        protected abstract void BakeInternal(
            in FlockEnvironmentData env,
            uint behaviourMask,
            List<PatternVolumeCommand> commands,
            List<PatternVolumeSphereShell> sphereShellPayloads,
            List<PatternVolumeBoxShell> boxShellPayloads);

        /**
         * <summary>
         * Builds a 32-bit behaviour mask from controller fish types and an optional target filter.
         * </summary>
         * <param name="controllerFishTypes">Fish type ordering used by the controller.</param>
         * <param name="targetTypes">Optional filter (null = everyone, empty = nobody).</param>
         * <returns>The compiled 32-bit behaviour mask.</returns>
         */
        protected static uint BuildBehaviourMask(
            FishTypePreset[] controllerFishTypes,
            FishTypePreset[] targetTypes) {
            if (controllerFishTypes == null || controllerFishTypes.Length == 0) {
                return uint.MaxValue;
            }

            if (targetTypes == null) {
                return uint.MaxValue;
            }

            if (targetTypes.Length == 0) {
                return 0u;
            }

            uint behaviourMask = 0u;

            for (int targetTypeIndex = 0; targetTypeIndex < targetTypes.Length; targetTypeIndex += 1) {
                FishTypePreset targetType = targetTypes[targetTypeIndex];
                if (targetType == null) {
                    continue;
                }

                behaviourMask |= FindTypeBit(controllerFishTypes, targetType);
            }

            return behaviourMask;
        }

        /**
         * <summary>
         * Converts a normalised coordinate in [0..1] bounds space into world space using the environment bounds.
         * </summary>
         * <param name="env">Environment data that defines bounds center and extents.</param>
         * <param name="norm01">Normalised coordinate in [0..1].</param>
         * <returns>The corresponding world-space position.</returns>
         */
        protected static float3 BoundsNormToWorld(
            in FlockEnvironmentData env,
            float3 norm01) {
            float3 boundsMinimum = env.BoundsCenter - env.BoundsExtents;
            float3 boundsMaximum = env.BoundsCenter + env.BoundsExtents;
            float3 boundsSize = boundsMaximum - boundsMinimum;

            float3 saturatedNormalised = math.saturate(norm01);
            return boundsMinimum + saturatedNormalised * boundsSize;
        }

        private static uint FindTypeBit(FishTypePreset[] controllerFishTypes, FishTypePreset targetType) {
            for (int controllerTypeIndex = 0; controllerTypeIndex < controllerFishTypes.Length && controllerTypeIndex < 32; controllerTypeIndex += 1) {
                if (controllerFishTypes[controllerTypeIndex] == targetType) {
                    return 1u << controllerTypeIndex;
                }
            }

            return 0u;
        }
    }
}
