namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using System;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    public sealed partial class FlockSimulation {
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

        /// <summary>
        /// Enable or update the pattern sphere that layer-3 patterns steer towards.
        /// Only behaviours with PatternWeight > 0 will react to this.
        /// </summary>
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

        /// <summary>
        /// Disable the pattern sphere influence completely.
        /// </summary>
        public void ClearPatternSphere() {
            patternSphereRadius = 0f;
            patternSphereStrength = 0f;
            patternSphereThickness = 0f;
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
            out RuntimeLayer3PatternInstance inst) {

            inst = default;

            if (!handle.IsValid) {
                return false;
            }

            int index = handle.Index;

            if ((uint)index >= (uint)runtimeLayer3Patterns.Count) {
                return false;
            }

            inst = runtimeLayer3Patterns[index];

            if (inst.Generation != handle.Generation) {
                return false; // stale handle
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

        void RemoveFromActiveList(int patternIndex, ref RuntimeLayer3PatternInstance inst) {
            int listIndex = inst.ActiveListIndex;

            if (listIndex < 0 || listIndex >= runtimeLayer3Active.Count) {
                inst.ActiveListIndex = -1;
                return;
            }

            int last = runtimeLayer3Active.Count - 1;
            int movedPatternIndex = runtimeLayer3Active[last];

            runtimeLayer3Active[listIndex] = movedPatternIndex;
            runtimeLayer3Active.RemoveAt(last);

            if (movedPatternIndex != patternIndex) {
                RuntimeLayer3PatternInstance moved = runtimeLayer3Patterns[movedPatternIndex];
                moved.ActiveListIndex = listIndex;
                runtimeLayer3Patterns[movedPatternIndex] = moved;
            }

            inst.ActiveListIndex = -1;
        }

        public FlockLayer3PatternHandle StartPatternSphereShell(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return FlockLayer3PatternHandle.Invalid;
            }

            radius = math.max(0f, radius);
            strength = math.max(0f, strength);

            if (radius <= 0f || strength <= 0f) {
                return FlockLayer3PatternHandle.Invalid;
            }

            float t = thickness <= 0f ? (radius * 0.25f) : thickness;
            t = math.max(0.001f, t);

            int payloadIndex = AcquireSphereShellPayloadSlot();
            runtimeSphereShells[payloadIndex] = new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = radius,
                Thickness = t,
            };

            int patternIndex = AcquireRuntimePatternSlot();
            RuntimeLayer3PatternInstance inst = runtimeLayer3Patterns[patternIndex];

            inst.Active = 1;
            inst.Kind = FlockLayer3PatternKind.SphereShell;
            inst.PayloadIndex = payloadIndex;
            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            inst.ActiveListIndex = runtimeLayer3Active.Count;
            runtimeLayer3Active.Add(patternIndex);

            runtimeLayer3Patterns[patternIndex] = inst;

            var handle = new FlockLayer3PatternHandle {
                Index = patternIndex,
                Generation = inst.Generation,
            };

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StartPatternSphereShell: handle=({handle.Index}:{handle.Generation}) radius={radius:0.###} thickness={t:0.###} strength={strength:0.###}.",
                null);

            return handle;
        }

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

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance inst)) {
                return false;
            }

            if (inst.Active == 0 || inst.Kind != FlockLayer3PatternKind.SphereShell) {
                return false;
            }

            int payloadIndex = inst.PayloadIndex;
            if ((uint)payloadIndex >= (uint)runtimeSphereShells.Count) {
                return false;
            }

            radius = math.max(0f, radius);
            strength = math.max(0f, strength);

            float t = thickness <= 0f ? (radius * 0.25f) : thickness;
            t = math.max(0.001f, t);

            runtimeSphereShells[payloadIndex] = new FlockLayer3PatternSphereShell {
                Center = center,
                Radius = radius,
                Thickness = t,
            };

            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            runtimeLayer3Patterns[handle.Index] = inst;
            return true;
        }

        public FlockLayer3PatternHandle StartPatternBoxShell(
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (!IsCreated) {
                return FlockLayer3PatternHandle.Invalid;
            }

            strength = math.max(0f, strength);

            if (halfExtents.x <= 0f
                || halfExtents.y <= 0f
                || halfExtents.z <= 0f
                || strength <= 0f) {
                return FlockLayer3PatternHandle.Invalid;
            }

            float3 he = new float3(
                math.max(halfExtents.x, 0.001f),
                math.max(halfExtents.y, 0.001f),
                math.max(halfExtents.z, 0.001f));

            float t = thickness <= 0f
                ? math.cmin(he) * 0.25f
                : thickness;

            t = math.max(0.001f, t);

            int payloadIndex = AcquireBoxShellPayloadSlot();
            runtimeBoxShells[payloadIndex] = new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = he,
                Thickness = t,
            };

            int patternIndex = AcquireRuntimePatternSlot();
            RuntimeLayer3PatternInstance inst = runtimeLayer3Patterns[patternIndex];

            inst.Active = 1;
            inst.Kind = FlockLayer3PatternKind.BoxShell;
            inst.PayloadIndex = payloadIndex;
            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            inst.ActiveListIndex = runtimeLayer3Active.Count;
            runtimeLayer3Active.Add(patternIndex);

            runtimeLayer3Patterns[patternIndex] = inst;

            var handle = new FlockLayer3PatternHandle {
                Index = patternIndex,
                Generation = inst.Generation,
            };

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StartPatternBoxShell: handle=({handle.Index}:{handle.Generation}) halfExtents=({he.x:0.###},{he.y:0.###},{he.z:0.###}) thickness={t:0.###} strength={strength:0.###}.",
                null);

            return handle;
        }

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

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance inst)) {
                return false;
            }

            if (inst.Active == 0 || inst.Kind != FlockLayer3PatternKind.BoxShell) {
                return false;
            }

            int payloadIndex = inst.PayloadIndex;
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

            float3 he = new float3(
                math.max(halfExtents.x, 0.001f),
                math.max(halfExtents.y, 0.001f),
                math.max(halfExtents.z, 0.001f));

            float t = thickness <= 0f
                ? math.cmin(he) * 0.25f
                : thickness;

            t = math.max(0.001f, t);

            runtimeBoxShells[payloadIndex] = new FlockLayer3PatternBoxShell {
                Center = center,
                HalfExtents = he,
                Thickness = t,
            };

            inst.Strength = strength;
            inst.BehaviourMask = behaviourMask;

            runtimeLayer3Patterns[handle.Index] = inst;
            return true;
        }

        public bool StopLayer3Pattern(FlockLayer3PatternHandle handle) {
            if (!IsCreated) {
                return false;
            }

            if (!TryGetRuntimePattern(handle, out RuntimeLayer3PatternInstance inst)) {
                return false;
            }

            if (inst.Active == 0) {
                return false;
            }

            int patternIndex = handle.Index;

            RemoveFromActiveList(patternIndex, ref inst);

            switch (inst.Kind) {
                case FlockLayer3PatternKind.SphereShell: {
                        if (inst.PayloadIndex >= 0) {
                            runtimeSphereShellFree.Push(inst.PayloadIndex);
                        }
                        break;
                    }

                case FlockLayer3PatternKind.BoxShell: {
                        if (inst.PayloadIndex >= 0) {
                            runtimeBoxShellFree.Push(inst.PayloadIndex);
                        }
                        break;
                    }
            }

            inst.Active = 0;
            inst.PayloadIndex = -1;
            inst.Strength = 0f;
            inst.BehaviourMask = 0u;

            inst.Generation += 1;

            runtimeLayer3Patterns[patternIndex] = inst;
            runtimeLayer3Free.Push(patternIndex);

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                $"StopLayer3Pattern: stopped handle=({handle.Index}:{handle.Generation}) kind={inst.Kind}.",
                null);

            return true;
        }
    }
}
