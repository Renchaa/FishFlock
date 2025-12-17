// File: Assets/Flock/Runtime/Testing/FlockDynamicLayer3PatternDriver.cs
namespace Flock.Runtime.Testing {
    using Flock.Runtime.Data;
    using Unity.Mathematics;
    using UnityEngine;

    public enum RuntimePatternShape {
        SphereShell,
        BoxShell,
    }

    /// <summary>
    /// Creates a runtime Layer-3 pattern (SphereShell or BoxShell)
    /// and keeps it centered on a target Transform.
    /// </summary>
    public sealed class FlockDynamicLayer3PatternDriver : MonoBehaviour {
        [Header("Target")]
        [Tooltip("Pattern will track this transform. If null, uses this GameObject.")]
        [SerializeField] Transform target;

        [Header("Shape")]
        [SerializeField] RuntimePatternShape shape = RuntimePatternShape.BoxShell;

        [Header("Sphere Settings")]
        [SerializeField, Min(0f)] float sphereRadius = 10f;
        [Tooltip("<= 0 means 'auto' = radius * 0.25")]
        [SerializeField] float sphereThickness = -1f;

        [Header("Box Settings")]
        [SerializeField] Vector3 boxHalfExtents = new Vector3(10f, 5f, 10f);
        [Tooltip("<= 0 means 'auto' = min(halfExtents)*0.25")]
        [SerializeField] float boxThickness = -1f;

        [Header("Pattern")]
        [SerializeField, Min(0f)] float strength = 1f;
        [SerializeField] bool createOnEnable = true;

        [SerializeField] FlockController controller;
        FlockLayer3PatternHandle handle = FlockLayer3PatternHandle.Invalid;

        void OnEnable() {
            if (createOnEnable) {
                CreatePattern();
            }
        }

        void OnDisable() {
            StopPattern();
        }

        void Update() {
            if (controller == null) {
                return;
            }

            // If we don't have a valid handle yet, keep trying to create it.
            // This handles the case where simulation wasn't ready on OnEnable.
            if (!handle.IsValid) {
                if (createOnEnable) {
                    CreatePattern();
                }
                return;
            }

            Transform t = target != null ? target : transform;
            float3 center = (float3)t.position;

            switch (shape) {
                case RuntimePatternShape.SphereShell:
                    controller.UpdateRuntimeSphereShell(
                        handle,
                        center,
                        sphereRadius,
                        sphereThickness,
                        strength,
                        uint.MaxValue);
                    break;

                case RuntimePatternShape.BoxShell:
                    controller.UpdateRuntimeBoxShell(
                        handle,
                        center,
                        (float3)boxHalfExtents,
                        boxThickness,
                        strength,
                        uint.MaxValue);
                    break;
            }
        }

        [ContextMenu("Create Pattern")]
        public void CreatePattern() {
            if (controller == null) {
                controller = GetComponent<FlockController>();
            }

            if (controller == null) {
                Debug.Log("Controller is null");
                return;
            }

            if (handle.IsValid) {
                StopPattern();
            }

            Transform t = target != null ? target : transform;
            float3 center = (float3)t.position;

            switch (shape) {
                case RuntimePatternShape.SphereShell:
                    handle = controller.StartRuntimeSphereShell(
                        center,
                        sphereRadius,
                        sphereThickness,
                        strength,
                        uint.MaxValue);
                    break;

                case RuntimePatternShape.BoxShell:
                    handle = controller.StartRuntimeBoxShell(
                        center,
                        (float3)boxHalfExtents,
                        boxThickness,
                        strength,
                        uint.MaxValue);
                    break;
            }
        }

        [ContextMenu("Stop Pattern")]
        public void StopPattern() {
            if (!handle.IsValid || controller == null) {
                return;
            }

            controller.StopRuntimePattern(handle);
            handle = FlockLayer3PatternHandle.Invalid;
        }
    }
}
