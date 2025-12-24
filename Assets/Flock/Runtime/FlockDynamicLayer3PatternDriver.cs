using Flock.Runtime.Data;
using Unity.Mathematics;
using UnityEngine;

namespace Flock.Runtime.Testing {
    /**
     * <summary>
     * Supported runtime pattern shapes for this driver.
     * </summary>
     */
    public enum RuntimePatternShape {
        SphereShell,
        BoxShell,
    }

    /**
     * <summary>
     * Creates a runtime Layer-3 pattern (SphereShell or BoxShell) and keeps it centered on a target transform.
     * </summary>
     */
    public sealed class FlockDynamicLayer3PatternDriver : MonoBehaviour {
        private const uint AllAffectedTypesMask = uint.MaxValue;
        private const string MissingControllerLogMessage = "Controller is null";

        [Header("Target")]
        [Tooltip("Pattern will track this transform. If null, uses this GameObject.")]
        [SerializeField]
        private Transform target;

        [Header("Shape")]
        [SerializeField]
        private RuntimePatternShape shape = RuntimePatternShape.BoxShell;

        [Header("Sphere Settings")]
        [SerializeField]
        [Min(0f)]
        private float sphereRadius = 10f;

        [Tooltip("<= 0 means 'auto' = radius * 0.25")]
        [SerializeField]
        private float sphereThickness = -1f;

        [Header("Box Settings")]
        [SerializeField]
        private Vector3 boxHalfExtents = new Vector3(10f, 5f, 10f);

        [Tooltip("<= 0 means 'auto' = min(halfExtents)*0.25")]
        [SerializeField]
        private float boxThickness = -1f;

        [Header("Pattern")]
        [SerializeField]
        [Min(0f)]
        private float strength = 1f;

        [SerializeField]
        private bool createOnEnable = true;

        [SerializeField]
        private FlockController controller;

        private FlockLayer3PatternHandle handle = FlockLayer3PatternHandle.Invalid;

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
            handle = FlockLayer3PatternHandle.Invalid;
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
                case RuntimePatternShape.SphereShell:
                    controller.UpdateRuntimeSphereShell(
                        handle,
                        centerPosition,
                        sphereRadius,
                        sphereThickness,
                        strength,
                        AllAffectedTypesMask);
                    return;

                case RuntimePatternShape.BoxShell:
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

        private FlockLayer3PatternHandle StartPattern(float3 centerPosition) {
            switch (shape) {
                case RuntimePatternShape.SphereShell:
                    return controller.StartRuntimeSphereShell(
                        centerPosition,
                        sphereRadius,
                        sphereThickness,
                        strength,
                        AllAffectedTypesMask);

                case RuntimePatternShape.BoxShell:
                    return controller.StartRuntimeBoxShell(
                        centerPosition,
                        (float3)boxHalfExtents,
                        boxThickness,
                        strength,
                        AllAffectedTypesMask);

                default:
                    return FlockLayer3PatternHandle.Invalid;
            }
        }

        private float3 GetCenterPosition() {
            Transform trackingTransform = target != null ? target : transform;
            return (float3)trackingTransform.position;
        }
    }
}
