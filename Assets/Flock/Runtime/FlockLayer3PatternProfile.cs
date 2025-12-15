namespace Flock.Runtime {
    using System.Collections.Generic;
    using Flock.Runtime.Data;
    using Unity.Mathematics;
    using UnityEngine;

    public abstract class FlockLayer3PatternProfile : ScriptableObject {
        [Header("Common")]
        [SerializeField] bool enabled = true;

        [SerializeField, Min(0f)]
        float strength = 1f;

        [Tooltip("Optional filter. If null/empty -> affects all fish types (max 32 supported by mask).")]
        [SerializeField] FishTypePreset[] affectedTypes;

        public bool Enabled => enabled;
        public float Strength => strength;
        public FishTypePreset[] AffectedTypes => affectedTypes;
        public abstract FlockLayer3PatternKind Kind { get; }

        // Called by controller once at sim init to compile SOs into runtime structs.
        public void Bake(
            in FlockEnvironmentData env,
            FishTypePreset[] controllerFishTypes,
            List<FlockLayer3PatternCommand> commands,
            List<FlockLayer3PatternSphereShell> sphereShellPayloads) {

            if (!enabled || strength <= 0f) {
                return;
            }

            uint mask = BuildBehaviourMask(controllerFishTypes, affectedTypes);

            // NEW: explicit "affect nobody" => bake nothing
            if (mask == 0u) {
                return;
            }

            BakeInternal(
                env,
                mask,
                commands,
                sphereShellPayloads);
        }

        protected abstract void BakeInternal(
            in FlockEnvironmentData env,
            uint behaviourMask,
            List<FlockLayer3PatternCommand> commands,
            List<FlockLayer3PatternSphereShell> sphereShellPayloads);

        protected static uint BuildBehaviourMask(
            FishTypePreset[] controllerFishTypes,
            FishTypePreset[] targetTypes) {

            // If controller has no types configured -> safest is "everyone"
            if (controllerFishTypes == null || controllerFishTypes.Length == 0) {
                return uint.MaxValue;
            }

            // Null => caller did not filter => everyone
            if (targetTypes == null) {
                return uint.MaxValue;
            }

            // Empty array => explicit "filter to nobody"
            if (targetTypes.Length == 0) {
                return 0u;
            }

            uint mask = 0u;

            for (int t = 0; t < targetTypes.Length; t += 1) {
                FishTypePreset target = targetTypes[t];
                if (target == null) {
                    continue;
                }

                for (int i = 0; i < controllerFishTypes.Length && i < 32; i += 1) {
                    if (controllerFishTypes[i] == target) {
                        mask |= (1u << i);
                        break;
                    }
                }
            }

            // IMPORTANT: if nothing matched, affect nobody (NOT everyone)
            return mask;
        }

        protected static float3 BoundsNormToWorld(
            in FlockEnvironmentData env,
            float3 norm01) {

            float3 min = env.BoundsCenter - env.BoundsExtents;
            float3 max = env.BoundsCenter + env.BoundsExtents;
            float3 size = max - min;

            float3 n = math.saturate(norm01);
            return min + n * size;
        }
    }
}
