namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using System;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /**
     * <summary>
     * Simulation runtime that manages native state for agents, grids, attractors, and Layer-3 pattern steering.
     * </summary>
     */
    public sealed partial class FlockSimulation {
        /**
         * <summary>
         * Sets the baked Layer-3 pattern command stream and payload arrays used by the simulation.
         * </summary>
         * <param name="commands">Baked command list.</param>
         * <param name="sphereShellPayloads">Sphere-shell payloads referenced by commands.</param>
         * <param name="boxShellPayloads">Box-shell payloads referenced by commands.</param>
         */
        public void SetLayer3Patterns(
            FlockLayer3PatternCommand[] commands,
            FlockLayer3PatternSphereShell[] sphereShellPayloads,
            FlockLayer3PatternBoxShell[] boxShellPayloads) {

            layer3PatternCommands = commands ?? Array.Empty<FlockLayer3PatternCommand>();
            layer3SphereShells = sphereShellPayloads ?? Array.Empty<FlockLayer3PatternSphereShell>();
            layer3BoxShells = boxShellPayloads ?? Array.Empty<FlockLayer3PatternBoxShell>();

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"Layer-3 baked patterns set: commands={layer3PatternCommands.Length}, sphereShells={layer3SphereShells.Length}, boxShells={layer3BoxShells.Length}.",
                null);
        }

        /**
         * <summary>
         * Enables or updates the pattern sphere that Layer-3 patterns steer towards.
         * Only behaviours with PatternWeight &gt; 0 will react to this.
         * </summary>
         * <param name="center">World-space center.</param>
         * <param name="radius">Sphere radius.</param>
         * <param name="thickness">Shell thickness; if &lt;= 0 uses radius * 0.25.</param>
         * <param name="strength">Steering strength.</param>
         * <param name="behaviourMask">Behaviour mask filter.</param>
         */
        public void SetPatternSphereTarget(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            patternSphereCenter = center;
            patternSphereRadius = math.max(0f, radius);
            patternSphereStrength = math.max(0f, strength);
            patternSphereBehaviourMask = behaviourMask;

            if (thickness <= 0f) {
                patternSphereThickness = patternSphereRadius * 0.25f;
            } else {
                patternSphereThickness = math.max(0.001f, thickness);
            }
        }

        /**
         * <summary>
         * Disables the pattern sphere influence completely.
         * </summary>
         */
        public void ClearPatternSphere() {
            patternSphereRadius = 0f;
            patternSphereStrength = 0f;
            patternSphereThickness = 0f;
        }

        /**
         * <summary>
         * Starts a runtime Layer-3 sphere-shell pattern instance.
         * Returns an invalid handle if the simulation is not created or if the parameters produce no influence.
         * </summary>
         * <param name="center">World-space center.</param>
         * <param name="radius">Sphere radius.</param>
         * <param name="thickness">Shell thickness; if &lt;= 0 uses radius * 0.25.</param>
         * <param name="strength">Steering strength.</param>
         * <param name="behaviourMask">Behaviour mask filter.</param>
         * <returns>A handle to the runtime pattern instance, or <see cref="FlockLayer3PatternHandle.Invalid"/>.</returns>
         */
        public FlockLayer3PatternHandle StartPatternSphereShell(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return FlockLayer3PatternHandle.Invalid;
            }

            if (!TryValidateSphereShellStart(radius, strength, thickness, out float clampedRadius, out float clampedStrength, out float clampedThickness)) {
                return FlockLayer3PatternHandle.Invalid;
            }

            int payloadIndex = AcquireSphereShellPayloadSlot();
            runtimeSphereShells[payloadIndex] = new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = clampedRadius,
                Thickness = clampedThickness,
            };

            int patternIndex = AcquireRuntimePatternSlot();
            RuntimeLayer3PatternInstance runtimePatternInstance = runtimeLayer3Patterns[patternIndex];

            runtimePatternInstance.Active = 1;
            runtimePatternInstance.Kind = FlockLayer3PatternKind.SphereShell;
            runtimePatternInstance.PayloadIndex = payloadIndex;
            runtimePatternInstance.Strength = clampedStrength;
            runtimePatternInstance.BehaviourMask = behaviourMask;

            runtimePatternInstance.ActiveListIndex = runtimeLayer3Active.Count;
            runtimeLayer3Active.Add(patternIndex);

            runtimeLayer3Patterns[patternIndex] = runtimePatternInstance;

            FlockLayer3PatternHandle handle = new FlockLayer3PatternHandle {
                Index = patternIndex,
                Generation = runtimePatternInstance.Generation,
            };

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StartPatternSphereShell: handle=({handle.Index}:{handle.Generation}) radius={clampedRadius:0.###} thickness={clampedThickness:0.###} strength={clampedStrength:0.###}.",
                null);

            return handle;
        }

        /**
         * <summary>
         * Updates a runtime Layer-3 sphere-shell pattern instance.
         * Returns false if the handle is invalid, stale, or does not reference a sphere-shell instance.
         * </summary>
         * <param name="handle">Runtime pattern handle.</param>
         * <param name="center">World-space center.</param>
         * <param name="radius">Sphere radius.</param>
         * <param name="thickness">Shell thickness; if &lt;= 0 uses radius * 0.25.</param>
         * <param name="strength">Steering strength.</param>
         * <param name="behaviourMask">Behaviour mask filter.</param>
         * <returns>True if the instance was updated; otherwise false.</returns>
         */
        public bool UpdatePatternSphereShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return false;
            }

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance runtimePatternInstance)) {
                return false;
            }

            if (runtimePatternInstance.Active == 0 || runtimePatternInstance.Kind != FlockLayer3PatternKind.SphereShell) {
                return false;
            }

            int payloadIndex = runtimePatternInstance.PayloadIndex;
            if ((uint)payloadIndex >= (uint)runtimeSphereShells.Count) {
                return false;
            }

            float clampedRadius = math.max(0f, radius);
            float clampedStrength = math.max(0f, strength);
            float clampedThickness = ComputeSphereShellThickness(clampedRadius, thickness);

            runtimeSphereShells[payloadIndex] = new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = clampedRadius,
                Thickness = clampedThickness,
            };

            runtimePatternInstance.Strength = clampedStrength;
            runtimePatternInstance.BehaviourMask = behaviourMask;

            runtimeLayer3Patterns[handle.Index] = runtimePatternInstance;
            return true;
        }

        /**
         * <summary>
         * Starts a runtime Layer-3 box-shell pattern instance.
         * Returns an invalid handle if the simulation is not created or if the parameters produce no influence.
         * </summary>
         * <param name="center">World-space center.</param>
         * <param name="halfExtents">Box half extents.</param>
         * <param name="thickness">Shell thickness; if &lt;= 0 uses min(halfExtents) * 0.25.</param>
         * <param name="strength">Steering strength.</param>
         * <param name="behaviourMask">Behaviour mask filter.</param>
         * <returns>A handle to the runtime pattern instance, or <see cref="FlockLayer3PatternHandle.Invalid"/>.</returns>
         */
        public FlockLayer3PatternHandle StartPatternBoxShell(
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return FlockLayer3PatternHandle.Invalid;
            }

            if (!TryValidateBoxShellStart(halfExtents, strength, thickness, out float3 clampedHalfExtents, out float clampedStrength, out float clampedThickness)) {
                return FlockLayer3PatternHandle.Invalid;
            }

            int payloadIndex = AcquireBoxShellPayloadSlot();
            runtimeBoxShells[payloadIndex] = new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = clampedHalfExtents,
                Thickness = clampedThickness,
            };

            int patternIndex = AcquireRuntimePatternSlot();
            RuntimeLayer3PatternInstance runtimePatternInstance = runtimeLayer3Patterns[patternIndex];

            runtimePatternInstance.Active = 1;
            runtimePatternInstance.Kind = FlockLayer3PatternKind.BoxShell;
            runtimePatternInstance.PayloadIndex = payloadIndex;
            runtimePatternInstance.Strength = clampedStrength;
            runtimePatternInstance.BehaviourMask = behaviourMask;

            runtimePatternInstance.ActiveListIndex = runtimeLayer3Active.Count;
            runtimeLayer3Active.Add(patternIndex);

            runtimeLayer3Patterns[patternIndex] = runtimePatternInstance;

            FlockLayer3PatternHandle handle = new FlockLayer3PatternHandle {
                Index = patternIndex,
                Generation = runtimePatternInstance.Generation,
            };

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StartPatternBoxShell: handle=({handle.Index}:{handle.Generation}) halfExtents=({clampedHalfExtents.x:0.###},{clampedHalfExtents.y:0.###},{clampedHalfExtents.z:0.###}) thickness={clampedThickness:0.###} strength={clampedStrength:0.###}.",
                null);

            return handle;
        }

        /**
         * <summary>
         * Updates a runtime Layer-3 box-shell pattern instance.
         * Returns false if the handle is invalid, stale, or does not reference a box-shell instance.
         * </summary>
         * <param name="handle">Runtime pattern handle.</param>
         * <param name="center">World-space center.</param>
         * <param name="halfExtents">Box half extents.</param>
         * <param name="thickness">Shell thickness; if &lt;= 0 uses min(halfExtents) * 0.25.</param>
         * <param name="strength">Steering strength.</param>
         * <param name="behaviourMask">Behaviour mask filter.</param>
         * <returns>True if the instance was updated; otherwise false.</returns>
         */
        public bool UpdatePatternBoxShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return false;
            }

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance runtimePatternInstance)) {
                return false;
            }

            if (runtimePatternInstance.Active == 0 || runtimePatternInstance.Kind != FlockLayer3PatternKind.BoxShell) {
                return false;
            }

            int payloadIndex = runtimePatternInstance.PayloadIndex;
            if ((uint)payloadIndex >= (uint)runtimeBoxShells.Count) {
                return false;
            }

            strength = math.max(0f, strength);

            if (halfExtents.x <= 0f
                || halfExtents.y <= 0f
                || halfExtents.z <= 0f
                || strength <= 0f) {
                return false;
            }

            float3 clampedHalfExtents = ClampPositiveHalfExtents(halfExtents);
            float clampedThickness = ComputeBoxShellThickness(clampedHalfExtents, thickness);

            runtimeBoxShells[payloadIndex] = new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = clampedHalfExtents,
                Thickness = clampedThickness,
            };

            runtimePatternInstance.Strength = strength;
            runtimePatternInstance.BehaviourMask = behaviourMask;

            runtimeLayer3Patterns[handle.Index] = runtimePatternInstance;
            return true;
        }

        /**
         * <summary>
         * Stops a runtime Layer-3 pattern instance and releases its resources back to the internal pools.
         * </summary>
         * <param name="handle">Runtime pattern handle.</param>
         * <returns>True if the pattern was active and is now stopped; otherwise false.</returns>
         */
        public bool StopLayer3Pattern(FlockLayer3PatternHandle handle) {
            if (!IsCreated) {
                return false;
            }

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance runtimePatternInstance)) {
                return false;
            }

            if (runtimePatternInstance.Active == 0) {
                return false;
            }

            int patternIndex = handle.Index;

            RemoveFromActiveList(patternIndex, ref runtimePatternInstance);
            ReleaseRuntimePatternPayload(runtimePatternInstance);

            runtimePatternInstance.Active = 0;
            runtimePatternInstance.PayloadIndex = -1;
            runtimePatternInstance.Strength = 0f;
            runtimePatternInstance.BehaviourMask = 0u;

            runtimePatternInstance.Generation += 1;

            runtimeLayer3Patterns[patternIndex] = runtimePatternInstance;
            runtimeLayer3Free.Push(patternIndex);

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StopLayer3Pattern: stopped handle=({handle.Index}:{handle.Generation}) kind={runtimePatternInstance.Kind}.",
                null);

            return true;
        }

        static JobHandle ScheduleLayer3PatternJob<T>(
            ref T job,
            NativeArray<float3> positions,
            NativeArray<int> behaviourIds,
            NativeArray<float3> patternSteering,
            uint behaviourMask,
            float strength,
            int agentCount,
            JobHandle inputHandle)
            where T : struct, IJobParallelFor, IFlockLayer3PatternJob {

            job.SetCommonData(
                positions,
                behaviourIds,
                patternSteering,
                behaviourMask,
                strength);

            return job.Schedule(agentCount, 64, inputHandle);
        }

        bool TryGetRuntimePattern(
            FlockLayer3PatternHandle handle,
            out RuntimeLayer3PatternInstance runtimePatternInstance) {

            runtimePatternInstance = default;

            if (!handle.IsValid) {
                return false;
            }

            int index = handle.Index;

            if ((uint)index >= (uint)runtimeLayer3Patterns.Count) {
                return false;
            }

            runtimePatternInstance = runtimeLayer3Patterns[index];

            if (runtimePatternInstance.Generation != handle.Generation) {
                return false;
            }

            return true;
        }

        int AcquireRuntimePatternSlot() {
            if (runtimeLayer3Free.Count > 0) {
                return runtimeLayer3Free.Pop();
            }

            runtimeLayer3Patterns.Add(new RuntimeLayer3PatternInstance {
                Generation = 0,
                Active = 0,
                ActiveListIndex = -1,
                Kind = 0,
                PayloadIndex = -1,
                Strength = 0f,
                BehaviourMask = 0u,
            });

            return runtimeLayer3Patterns.Count - 1;
        }

        int AcquireSphereShellPayloadSlot() {
            if (runtimeSphereShellFree.Count > 0) {
                return runtimeSphereShellFree.Pop();
            }

            runtimeSphereShells.Add(default);
            return runtimeSphereShells.Count - 1;
        }

        int AcquireBoxShellPayloadSlot() {
            if (runtimeBoxShellFree.Count > 0) {
                return runtimeBoxShellFree.Pop();
            }

            runtimeBoxShells.Add(default);
            return runtimeBoxShells.Count - 1;
        }

        void RemoveFromActiveList(int patternIndex, ref RuntimeLayer3PatternInstance runtimePatternInstance) {
            int activeListIndex = runtimePatternInstance.ActiveListIndex;

            if (activeListIndex < 0 || activeListIndex >= runtimeLayer3Active.Count) {
                runtimePatternInstance.ActiveListIndex = -1;
                return;
            }

            int lastIndex = runtimeLayer3Active.Count - 1;
            int movedPatternIndex = runtimeLayer3Active[lastIndex];

            runtimeLayer3Active[activeListIndex] = movedPatternIndex;
            runtimeLayer3Active.RemoveAt(lastIndex);

            if (movedPatternIndex != patternIndex) {
                RuntimeLayer3PatternInstance movedPatternInstance = runtimeLayer3Patterns[movedPatternIndex];
                movedPatternInstance.ActiveListIndex = activeListIndex;
                runtimeLayer3Patterns[movedPatternIndex] = movedPatternInstance;
            }

            runtimePatternInstance.ActiveListIndex = -1;
        }

        bool TryValidateSphereShellStart(
            float radius,
            float strength,
            float thickness,
            out float clampedRadius,
            out float clampedStrength,
            out float clampedThickness) {

            clampedRadius = math.max(0f, radius);
            clampedStrength = math.max(0f, strength);

            if (clampedRadius <= 0f || clampedStrength <= 0f) {
                clampedThickness = 0f;
                return false;
            }

            clampedThickness = ComputeSphereShellThickness(clampedRadius, thickness);
            return true;
        }

        static float ComputeSphereShellThickness(float radius, float thickness) {
            float thicknessValue = thickness <= 0f ? (radius * 0.25f) : thickness;
            return math.max(0.001f, thicknessValue);
        }

        bool TryValidateBoxShellStart(
            float3 halfExtents,
            float strength,
            float thickness,
            out float3 clampedHalfExtents,
            out float clampedStrength,
            out float clampedThickness) {

            clampedStrength = math.max(0f, strength);

            if (halfExtents.x <= 0f
                || halfExtents.y <= 0f
                || halfExtents.z <= 0f
                || clampedStrength <= 0f) {
                clampedHalfExtents = default;
                clampedThickness = 0f;
                return false;
            }

            clampedHalfExtents = ClampPositiveHalfExtents(halfExtents);
            clampedThickness = ComputeBoxShellThickness(clampedHalfExtents, thickness);

            return true;
        }

        static float3 ClampPositiveHalfExtents(float3 halfExtents) {
            return new float3(
                math.max(halfExtents.x, 0.001f),
                math.max(halfExtents.y, 0.001f),
                math.max(halfExtents.z, 0.001f));
        }

        static float ComputeBoxShellThickness(float3 halfExtents, float thickness) {
            float thicknessValue = thickness <= 0f
                ? math.cmin(halfExtents) * 0.25f
                : thickness;

            return math.max(0.001f, thicknessValue);
        }

        void ReleaseRuntimePatternPayload(in RuntimeLayer3PatternInstance runtimePatternInstance) {
            switch (runtimePatternInstance.Kind) {
                case FlockLayer3PatternKind.SphereShell: {
                        if (runtimePatternInstance.PayloadIndex >= 0) {
                            runtimeSphereShellFree.Push(runtimePatternInstance.PayloadIndex);
                        }
                        break;
                    }

                case FlockLayer3PatternKind.BoxShell: {
                        if (runtimePatternInstance.PayloadIndex >= 0) {
                            runtimeBoxShellFree.Push(runtimePatternInstance.PayloadIndex);
                        }
                        break;
                    }
            }
        }
    }
}
