// =====================================
// NEW FILE: GroupNoisePatternProfile.cs
// File: Assets/Flock/Runtime/GroupNoisePatternProfile.cs
// NO CHANGES NEEDED – just showing for completeness
// =====================================
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using UnityEngine;
    using Unity.Mathematics;

    public enum FlockGroupNoisePatternType {
        SimpleSine = 0,
        VerticalBands = 1,
        Vortex = 2,
        SphereShell = 3   // NEW: spherical shell pattern
    }

    [CreateAssetMenu(
        fileName = "GroupNoisePattern",
        menuName = "Flock/Group Noise Pattern")]
    public sealed class GroupNoisePatternProfile : ScriptableObject {

        [Header("Common")]
        [SerializeField] float baseFrequency = 1.0f;
        [SerializeField] Vector3 timeScale = new Vector3(1.0f, 1.1f, 1.3f);
        [SerializeField] Vector3 phaseOffset = Vector3.zero;
        [SerializeField] float worldScale = 10.0f;
        [SerializeField] uint seed = 1234567u;

        [Header("Pattern Type")]
        [SerializeField]
        FlockGroupNoisePatternType patternType =
            FlockGroupNoisePatternType.SimpleSine;

        [Header("Simple / Bands Extras")]
        [SerializeField] float swirlStrength = 0.0f;
        [SerializeField] float verticalBias = 0.0f;

        [Header("Vortex Settings")]
        [SerializeField] Vector3 vortexCenterNorm = new Vector3(0.5f, 0.5f, 0.5f);
        [SerializeField] float vortexRadius = 10.0f;
        [SerializeField] float vortexTightness = 1.0f;

        // NEW: spherical shell pattern
        [Header("Sphere Shell Settings")]
        [SerializeField] float sphereRadius = 8.0f;
        [SerializeField] float sphereThickness = 2.0f;
        [SerializeField] float sphereSwirlStrength = 1.0f;
        [SerializeField]
        Vector3 sphereCenterNorm = new Vector3(0.5f, 0.5f, 0.5f);

        public FlockGroupNoisePatternType PatternType => patternType;

        public FlockGroupNoisePatternSettings ToSettings() {
            FlockGroupNoisePatternSettings s;

            s.BaseFrequency = Mathf.Max(0f, baseFrequency);

            s.TimeScale = new float3(
                timeScale.x,
                timeScale.y,
                timeScale.z);

            s.PhaseOffset = new float3(
                phaseOffset.x,
                phaseOffset.y,
                phaseOffset.z);

            s.WorldScale = Mathf.Max(0.001f, worldScale);

            s.Seed = seed == 0u ? 1u : seed;

            s.PatternType = (int)patternType;

            s.SwirlStrength = Mathf.Max(0f, swirlStrength);
            s.VerticalBias = verticalBias;

            s.VortexCenterNorm = new float3(
                Mathf.Clamp01(vortexCenterNorm.x),
                Mathf.Clamp01(vortexCenterNorm.y),
                Mathf.Clamp01(vortexCenterNorm.z));

            s.VortexRadius = Mathf.Max(0f, vortexRadius);
            s.VortexTightness = Mathf.Max(0f, vortexTightness);

            // NEW: spherical shell settings
            s.SphereRadius = Mathf.Max(0f, sphereRadius);
            s.SphereThickness = Mathf.Max(0.001f, sphereThickness);
            s.SphereSwirlStrength = Mathf.Max(0f, sphereSwirlStrength);
            s.SphereCenterNorm = new float3(
                Mathf.Clamp01(sphereCenterNorm.x),
                Mathf.Clamp01(sphereCenterNorm.y),
                Mathf.Clamp01(sphereCenterNorm.z));

            return s;
        }

        public FlockGroupNoiseCommonSettings ToCommonSettings() {
            return new FlockGroupNoiseCommonSettings {
                BaseFrequency = Mathf.Max(0f, baseFrequency),
                TimeScale = new float3(timeScale.x, timeScale.y, timeScale.z),
                PhaseOffset = new float3(phaseOffset.x, phaseOffset.y, phaseOffset.z),
                WorldScale = Mathf.Max(0.001f, worldScale),
                Seed = seed == 0u ? 1u : seed,
            };
        }

        public FlockGroupNoiseSimpleSinePayload ToSimpleSinePayload() {
            return new FlockGroupNoiseSimpleSinePayload {
                SwirlStrength = Mathf.Max(0f, swirlStrength),
            };
        }

        public FlockGroupNoiseVerticalBandsPayload ToVerticalBandsPayload() {
            return new FlockGroupNoiseVerticalBandsPayload {
                VerticalBias = verticalBias,
            };
        }

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
