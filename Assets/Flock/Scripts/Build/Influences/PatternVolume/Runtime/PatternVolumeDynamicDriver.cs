using Flock.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Flock.Scripts.Build.Influence.PatternVolume.Runtime {

    /**
     * <summary>
     * Creates a runtime Layer-3 pattern (SphereShell or BoxShell) and keeps it centered on a target transform.
     * </summary>
     */
    public sealed class PatternVolumeDynamicDriver : MonoBehaviour {
        [Tooltip("Bitmask used to affect all fish types.")]
        private const uint AllAffectedTypesMask = uint.MaxValue;

        [Tooltip("Log message used when no FlockController can be found.")]
        private const string MissingControllerLogMessage = "Controller is null";

        [Header("Target")]

        [Tooltip("Pattern will track this transform. If null, uses this GameObject.")]
        [SerializeField]
        private Transform target;

        [Header("Shape")]

        [Tooltip("Which runtime shell shape to generate and update.")]
        [SerializeField]
        private PatternVolumeShape shape = PatternVolumeShape.BoxShell;

        [Header("Sphere Settings")]

        [Tooltip("Radius of the sphere shell.")]
        [SerializeField]
        [Min(0f)]
        private float sphereRadius = 10f;

        [Tooltip("Shell thickness. <= 0 means 'auto' = radius * 0.25.")]
        [SerializeField]
        private float sphereThickness = -1f;

        [Header("Box Settings")]

        [Tooltip("Half-extents of the box shell in local XYZ size units.")]
        [SerializeField]
        private Vector3 boxHalfExtents = new Vector3(10f, 5f, 10f);

        [Tooltip("Shell thickness. <= 0 means 'auto' = min(halfExtents) * 0.25.")]
        [SerializeField]
        private float boxThickness = -1f;

        [Header("Pattern")]

        [Tooltip("Overall strength applied by the pattern updates.")]
        [SerializeField]
        [Min(0f)]
        private float strength = 1f;

        [Tooltip("If true, attempts to create the pattern automatically on enable.")]
        [SerializeField]
        private bool createOnEnable = true;

        [Tooltip("Flock controller used to start, update, and stop runtime patterns. If null, fetched from this GameObject.")]
        [SerializeField]
        private FlockController controller;

        [Tooltip("Handle to the currently running runtime pattern (invalid when not running).")]
        private PatternVolumeHandle handle = PatternVolumeHandle.Invalid;

        private void OnEnable() {
            if (createOnEnable) {
                CreatePattern();
            }
        }

        private void OnDisable() {
            StopPattern();
        }

        private void Update() {
            if (controller == null) {
                return;
            }

            if (!handle.IsValid) {
                TryCreatePatternWhenMissingHandle();
                return;
            }

            UpdatePattern();
        }

        /**
         * <summary>
         * Creates (or recreates) the runtime pattern using the current inspector settings.
         * </summary>
         */
        [ContextMenu("Create Pattern")]
        public void CreatePattern() {
            EnsureControllerReference();

            if (controller == null) {
                Debug.Log(MissingControllerLogMessage);
                return;
            }

            if (handle.IsValid) {
                StopPattern();
            }

            float3 centerPosition = GetCenterPosition();
            handle = StartPattern(centerPosition);
        }

        /**
         * <summary>
         * Stops the currently running runtime pattern, if any.
         * </summary>
         */
        [ContextMenu("Stop Pattern")]
        public void StopPattern() {
            if (!handle.IsValid || controller == null) {
                return;
            }

            controller.StopRuntimePattern(handle);
            handle = PatternVolumeHandle.Invalid;
        }

        // If we don't have a valid handle yet, keep trying to create it.
        // This handles the case where simulation wasn't ready on OnEnable.
        private void TryCreatePatternWhenMissingHandle() {
            if (!createOnEnable) {
                return;
            }

            CreatePattern();
        }

        private void EnsureControllerReference() {
            if (controller != null) {
                return;
            }

            controller = GetComponent<FlockController>();
        }

        private void UpdatePattern() {
            float3 centerPosition = GetCenterPosition();

            switch (shape) {
                case PatternVolumeShape.SphereShell:
                    controller.UpdateRuntimeSphereShell(
                        handle,
                        centerPosition,
                        sphereRadius,
                        sphereThickness,
                        strength,
                        AllAffectedTypesMask);
                    return;

                case PatternVolumeShape.BoxShell:
                    controller.UpdateRuntimeBoxShell(
                        handle,
                        centerPosition,
                        (float3)boxHalfExtents,
                        boxThickness,
                        strength,
                        AllAffectedTypesMask);
                    return;
            }
        }

        private PatternVolumeHandle StartPattern(float3 centerPosition) {
            switch (shape) {
                case PatternVolumeShape.SphereShell:
                    return controller.StartRuntimeSphereShell(
                        centerPosition,
                        sphereRadius,
                        sphereThickness,
                        strength,
                        AllAffectedTypesMask);

                case PatternVolumeShape.BoxShell:
                    return controller.StartRuntimeBoxShell(
                        centerPosition,
                        (float3)boxHalfExtents,
                        boxThickness,
                        strength,
                        AllAffectedTypesMask);

                default:
                    return PatternVolumeHandle.Invalid;
            }
        }

        private float3 GetCenterPosition() {
            Transform trackingTransform = target != null ? target : transform;
            return (float3)trackingTransform.position;
        }
    }
}
