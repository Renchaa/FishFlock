using Flock.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Flock.Runtime {
    /**
     * <summary>
     * Supported procedural group-noise pattern types.
     * </summary>
     */
    public enum FlockGroupNoisePatternType {
        SimpleSine = 0,
        VerticalBands = 1,
        Vortex = 2,
        SphereShell = 3,
    }

    /**
     * <summary>
     * ScriptableObject that stores group-noise pattern parameters and can be converted into runtime settings/payloads.
     * </summary>
     */
    [CreateAssetMenu(
        fileName = "GroupNoisePattern",
        menuName = "Flock/Group Noise Pattern")]
    public sealed class GroupNoisePatternProfile : ScriptableObject {
        [Header("Common")]

        [Tooltip("Base frequency multiplier applied to the selected pattern.")]
        [SerializeField]
        private float baseFrequency = 1.0f;

        [Tooltip("Per-axis time scaling applied to pattern animation.")]
        [SerializeField]
        private Vector3 timeScale = new Vector3(1.0f, 1.1f, 1.3f);

        [Tooltip("Per-axis phase offset applied to the pattern.")]
        [SerializeField]
        private Vector3 phaseOffset = Vector3.zero;

        [Tooltip("World-space scale used when sampling the pattern (must be > 0).")]
        [SerializeField]
        private float worldScale = 10.0f;

        [Tooltip("Seed for deterministic pattern sampling (0 is coerced to 1).")]
        [SerializeField]
        private uint seed = 1234567u;

        [Header("Pattern Type")]

        [Tooltip("Which pattern implementation to use when sampling the group noise field.")]
        [SerializeField]
        private FlockGroupNoisePatternType patternType = FlockGroupNoisePatternType.SimpleSine;

        [Header("Simple / Bands Extras")]

        [Tooltip("Swirl strength used by patterns that support swirl.")]
        [SerializeField]
        private float swirlStrength = 0.0f;

        [Tooltip("Vertical bias applied by patterns that support vertical banding/bias.")]
        [SerializeField]
        private float verticalBias = 0.0f;

        [Header("Vortex Settings")]

        [Tooltip("Normalised vortex center in bounds space [0..1].")]
        [SerializeField]
        private Vector3 vortexCenterNorm = new Vector3(0.5f, 0.5f, 0.5f);

        [Tooltip("Vortex radius in world units.")]
        [SerializeField]
        private float vortexRadius = 10.0f;

        [Tooltip("Vortex tightness multiplier.")]
        [SerializeField]
        private float vortexTightness = 1.0f;

        [Header("Sphere Shell Settings")]

        [Tooltip("Sphere shell radius in world units.")]
        [SerializeField]
        private float sphereRadius = 8.0f;

        [Tooltip("Sphere shell thickness in world units (must be > 0).")]
        [SerializeField]
        private float sphereThickness = 2.0f;

        [Tooltip("Swirl strength applied within the sphere shell pattern.")]
        [SerializeField]
        private float sphereSwirlStrength = 1.0f;

        [Tooltip("Normalised sphere shell center in bounds space [0..1].")]
        [SerializeField]
        private Vector3 sphereCenterNorm = new Vector3(0.5f, 0.5f, 0.5f);

        /**
         * <summary>
         * Gets the configured pattern type.
         * </summary>
         */
        public FlockGroupNoisePatternType PatternType => patternType;

        /**
         * <summary>
         * Converts this profile into a runtime <see cref="FlockGroupNoisePatternSettings"/> snapshot.
         * </summary>
         * <returns>The populated <see cref="FlockGroupNoisePatternSettings"/>.</returns>
         */
        public FlockGroupNoisePatternSettings ToSettings() {
            FlockGroupNoisePatternSettings settings;

            settings.BaseFrequency = Mathf.Max(0f, baseFrequency);

            settings.TimeScale = new float3(
                timeScale.x,
                timeScale.y,
                timeScale.z);

            settings.PhaseOffset = new float3(
                phaseOffset.x,
                phaseOffset.y,
                phaseOffset.z);

            settings.WorldScale = Mathf.Max(0.001f, worldScale);

            settings.Seed = seed == 0u ? 1u : seed;

            settings.PatternType = (int)patternType;

            settings.SwirlStrength = Mathf.Max(0f, swirlStrength);
            settings.VerticalBias = verticalBias;

            settings.VortexCenterNorm = new float3(
                Mathf.Clamp01(vortexCenterNorm.x),
                Mathf.Clamp01(vortexCenterNorm.y),
                Mathf.Clamp01(vortexCenterNorm.z));

            settings.VortexRadius = Mathf.Max(0f, vortexRadius);
            settings.VortexTightness = Mathf.Max(0f, vortexTightness);

            settings.SphereRadius = Mathf.Max(0f, sphereRadius);
            settings.SphereThickness = Mathf.Max(0.001f, sphereThickness);
            settings.SphereSwirlStrength = Mathf.Max(0f, sphereSwirlStrength);

            settings.SphereCenterNorm = new float3(
                Mathf.Clamp01(sphereCenterNorm.x),
                Mathf.Clamp01(sphereCenterNorm.y),
                Mathf.Clamp01(sphereCenterNorm.z));

            return settings;
        }

        /**
         * <summary>
         * Converts the common (type-agnostic) settings into a runtime <see cref="FlockGroupNoiseCommonSettings"/> snapshot.
         * </summary>
         * <returns>The populated <see cref="FlockGroupNoiseCommonSettings"/>.</returns>
         */
        public FlockGroupNoiseCommonSettings ToCommonSettings() {
            return new FlockGroupNoiseCommonSettings {
                BaseFrequency = Mathf.Max(0f, baseFrequency),
                TimeScale = new float3(timeScale.x, timeScale.y, timeScale.z),
                PhaseOffset = new float3(phaseOffset.x, phaseOffset.y, phaseOffset.z),
                WorldScale = Mathf.Max(0.001f, worldScale),
                Seed = seed == 0u ? 1u : seed,
            };
        }

        /**
         * <summary>
         * Converts simple-sine-specific settings into a runtime payload.
         * </summary>
         * <returns>The populated <see cref="FlockGroupNoiseSimpleSinePayload"/>.</returns>
         */
        public FlockGroupNoiseSimpleSinePayload ToSimpleSinePayload() {
            return new FlockGroupNoiseSimpleSinePayload {
                SwirlStrength = Mathf.Max(0f, swirlStrength),
            };
        }

        /**
         * <summary>
         * Converts vertical-bands-specific settings into a runtime payload.
         * </summary>
         * <returns>The populated <see cref="FlockGroupNoiseVerticalBandsPayload"/>.</returns>
         */
        public FlockGroupNoiseVerticalBandsPayload ToVerticalBandsPayload() {
            return new FlockGroupNoiseVerticalBandsPayload {
                VerticalBias = verticalBias,
            };
        }

        /**
         * <summary>
         * Converts vortex-specific settings into a runtime payload.
         * </summary>
         * <returns>The populated <see cref="FlockGroupNoiseVortexPayload"/>.</returns>
         */
        public FlockGroupNoiseVortexPayload ToVortexPayload() {
            return new FlockGroupNoiseVortexPayload {
                CenterNorm = new float3(
                    Mathf.Clamp01(vortexCenterNorm.x),
                    Mathf.Clamp01(vortexCenterNorm.y),
                    Mathf.Clamp01(vortexCenterNorm.z)),
                Radius = Mathf.Max(0f, vortexRadius),
                Tightness = Mathf.Max(0f, vortexTightness),
                VerticalBias = verticalBias,
            };
        }

        /**
         * <summary>
         * Converts sphere-shell-specific settings into a runtime payload.
         * </summary>
         * <returns>The populated <see cref="FlockGroupNoiseSphereShellPayload"/>.</returns>
         */
        public FlockGroupNoiseSphereShellPayload ToSphereShellPayload() {
            return new FlockGroupNoiseSphereShellPayload {
                CenterNorm = new float3(
                    Mathf.Clamp01(sphereCenterNorm.x),
                    Mathf.Clamp01(sphereCenterNorm.y),
                    Mathf.Clamp01(sphereCenterNorm.z)),
                Radius = Mathf.Max(0f, sphereRadius),
                Thickness = Mathf.Max(0.001f, sphereThickness),
                SwirlStrength = Mathf.Max(0f, sphereSwirlStrength),
            };
        }
    }
}
