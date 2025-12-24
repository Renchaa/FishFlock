namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using System;
    using System.Collections.Generic;
    using Unity.Mathematics;
    using UnityEngine;

    public sealed partial class FlockController {
        // Runtime Layer-3 start/update scratch (reused to avoid allocations)
        readonly List<FlockLayer3PatternCommand> runtimeL3CmdScratch = new List<FlockLayer3PatternCommand>(4);
        readonly List<FlockLayer3PatternSphereShell> runtimeL3SphereScratch = new List<FlockLayer3PatternSphereShell>(4);
        readonly List<FlockLayer3PatternBoxShell> runtimeL3BoxShellScratch = new List<FlockLayer3PatternBoxShell>(4);
        readonly List<FlockLayer3PatternHandle> runtimeL3HandleScratch = new List<FlockLayer3PatternHandle>(4);

        public FlockLayer3PatternToken StartLayer3Pattern(FlockLayer3PatternProfile profile) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return null;
            }

            if (profile == null) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' with null profile.",
                    this);
                return null;
            }

            runtimeL3HandleScratch.Clear();

            if (!TryBuildRuntimeLayer3FromProfile(profile, runtimeL3HandleScratch)) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern on '{name}' for profile '{profile.name}' produced no valid runtime commands.",
                    this);
                return null;
            }

            var token = ScriptableObject.CreateInstance<FlockLayer3PatternToken>();
            token.hideFlags = HideFlags.DontSave;
            token.SetHandles(runtimeL3HandleScratch);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Started Layer-3 pattern '{profile.name}' with {runtimeL3HandleScratch.Count} runtime handles on '{name}'.",
                this);

            return token;
        }

        public bool UpdateLayer3Pattern(FlockLayer3PatternToken token, FlockLayer3PatternProfile profile) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (token == null) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' with null token.",
                    this);
                return false;
            }

            if (profile == null) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' with null profile.",
                    this);
                return false;
            }

            // Bake fresh commands/payloads from the profile
            FlockEnvironmentData env = BuildEnvironmentData();

            runtimeL3CmdScratch.Clear();
            runtimeL3SphereScratch.Clear();
            runtimeL3BoxShellScratch.Clear();

            profile.Bake(env, fishTypes, runtimeL3CmdScratch, runtimeL3SphereScratch, runtimeL3BoxShellScratch);

            // If profile currently bakes nothing -> stop whatever was running
            if (runtimeL3CmdScratch.Count == 0) {
                StopLayer3Pattern(token);
                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' stopped token because profile '{profile.name}' baked no commands.",
                    this);
                return true;
            }

            // If the token doesn't match the baked command count, rebuild the token in-place
            if (token.HandleCount != runtimeL3CmdScratch.Count) {
                StopLayer3Pattern(token);

                runtimeL3HandleScratch.Clear();
                if (!TryBuildRuntimeLayer3FromProfile(profile, runtimeL3HandleScratch)) {
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"UpdateLayer3Pattern on '{name}' failed to rebuild token for profile '{profile.name}'.",
                        this);
                    return false;
                }

                token.SetHandles(runtimeL3HandleScratch);

                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' rebuilt token for profile '{profile.name}' with {runtimeL3HandleScratch.Count} handles.",
                    this);

                return true;
            }

            bool allOk = true;

            // Update each handle using the baked command list order
            for (int i = 0; i < runtimeL3CmdScratch.Count; i += 1) {
                FlockLayer3PatternCommand cmd = runtimeL3CmdScratch[i];
                FlockLayer3PatternHandle handle = token.GetHandle(i);

                if (cmd.Strength <= 0f) {
                    // If command bakes but strength is 0, stop this one and invalidate via restart
                    simulation.StopLayer3Pattern(handle);
                    allOk = false;
                    continue;
                }

                switch (cmd.Kind) {
                    case FlockLayer3PatternKind.SphereShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3SphereScratch.Count) {
                                allOk = false;
                                continue;
                            }

                            FlockLayer3PatternSphereShell s = runtimeL3SphereScratch[cmd.PayloadIndex];

                            bool ok = simulation.UpdatePatternSphereShell(
                                handle,
                                s.Center,
                                s.Radius,
                                s.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (!ok) {
                                // stale/invalid handle -> restart just this entry
                                simulation.StopLayer3Pattern(handle);

                                FlockLayer3PatternHandle newHandle = simulation.StartPatternSphereShell(
                                    s.Center,
                                    s.Radius,
                                    s.Thickness,
                                    cmd.Strength,
                                    cmd.BehaviourMask);

                                if (!newHandle.IsValid) {
                                    allOk = false;
                                    continue;
                                }

                                token.ReplaceHandle(i, newHandle);
                            }

                            break;
                        }
                    case FlockLayer3PatternKind.BoxShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3BoxShellScratch.Count) {
                                allOk = false;
                                continue;
                            }

                            FlockLayer3PatternBoxShell b = runtimeL3BoxShellScratch[cmd.PayloadIndex];

                            bool ok = simulation.UpdatePatternBoxShell(
                                handle,
                                b.Center,
                                b.HalfExtents,
                                b.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (!ok) {
                                // stale/invalid handle -> restart just this entry
                                simulation.StopLayer3Pattern(handle);

                                FlockLayer3PatternBoxShell rb = b;

                                FlockLayer3PatternHandle newHandle = simulation.StartPatternBoxShell(
                                    rb.Center,
                                    rb.HalfExtents,
                                    rb.Thickness,
                                    cmd.Strength,
                                    cmd.BehaviourMask);

                                if (!newHandle.IsValid) {
                                    allOk = false;
                                    continue;
                                }

                                token.ReplaceHandle(i, newHandle);
                            }

                            break;
                        }

                    default: {
                            // Unsupported runtime kind -> treat as failure
                            allOk = false;
                            break;
                        }
                }
            }

            if (!allOk) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' for profile '{profile.name}' had to restart or skip one or more runtime handles.",
                    this);
            }

            return allOk;
        }

        public bool StopLayer3Pattern(FlockLayer3PatternToken token) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (token == null || !token.IsValid) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopLayer3Pattern called on '{name}' with null or invalid token.",
                    this);
                return false;
            }

            bool anyStopped = false;

            int count = token.HandleCount;
            for (int i = 0; i < count; i += 1) {
                anyStopped |= simulation.StopLayer3Pattern(token.GetHandle(i));
            }

            token.Invalidate();

            if (anyStopped) {
                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"Stopped Layer-3 pattern token with {count} handles on '{name}'.",
                    this);
            }

            return anyStopped;
        }

        public FlockLayer3PatternHandle StartRuntimeSphereShell(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartRuntimeSphereShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return FlockLayer3PatternHandle.Invalid;
            }

            return simulation.StartPatternSphereShell(
                center,
                radius,
                thickness,
                strength,
                behaviourMask);
        }

        public bool UpdateRuntimeSphereShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateRuntimeSphereShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            return simulation.UpdatePatternSphereShell(
                handle,
                center,
                radius,
                thickness,
                strength,
                behaviourMask);
        }

        public FlockLayer3PatternHandle StartRuntimeBoxShell(
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartRuntimeBoxShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return FlockLayer3PatternHandle.Invalid;
            }

            return simulation.StartPatternBoxShell(
                center,
                halfExtents,
                thickness,
                strength,
                behaviourMask);
        }

        public bool UpdateRuntimeBoxShell(
            FlockLayer3PatternHandle handle,
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue) {

            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateRuntimeBoxShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            return simulation.UpdatePatternBoxShell(
                handle,
                center,
                halfExtents,
                thickness,
                strength,
                behaviourMask);
        }

        public bool StopRuntimePattern(FlockLayer3PatternHandle handle) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopRuntimePattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            return simulation.StopLayer3Pattern(handle);
        }

        bool TryBuildRuntimeLayer3FromProfile(
            FlockLayer3PatternProfile profile,
            List<FlockLayer3PatternHandle> outHandles) {

            outHandles.Clear();

            FlockEnvironmentData env = BuildEnvironmentData();

            runtimeL3CmdScratch.Clear();
            runtimeL3SphereScratch.Clear();
            runtimeL3BoxShellScratch.Clear();

            profile.Bake(env, fishTypes, runtimeL3CmdScratch, runtimeL3SphereScratch, runtimeL3BoxShellScratch);

            if (runtimeL3CmdScratch.Count == 0) {
                return false;
            }

            for (int i = 0; i < runtimeL3CmdScratch.Count; i += 1) {
                FlockLayer3PatternCommand cmd = runtimeL3CmdScratch[i];

                if (cmd.Strength <= 0f) {
                    continue;
                }

                switch (cmd.Kind) {
                    case FlockLayer3PatternKind.SphereShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3SphereScratch.Count) {
                                continue;
                            }

                            FlockLayer3PatternSphereShell s = runtimeL3SphereScratch[cmd.PayloadIndex];

                            FlockLayer3PatternHandle handle = simulation.StartPatternSphereShell(
                                s.Center,
                                s.Radius,
                                s.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (handle.IsValid) {
                                outHandles.Add(handle);
                            }

                            break;
                        }

                    case FlockLayer3PatternKind.BoxShell: {
                            if ((uint)cmd.PayloadIndex >= (uint)runtimeL3BoxShellScratch.Count) {
                                continue;
                            }

                            FlockLayer3PatternBoxShell b = runtimeL3BoxShellScratch[cmd.PayloadIndex];

                            FlockLayer3PatternHandle handle = simulation.StartPatternBoxShell(
                                b.Center,
                                b.HalfExtents,
                                b.Thickness,
                                cmd.Strength,
                                cmd.BehaviourMask);

                            if (handle.IsValid) {
                                outHandles.Add(handle);
                            }

                            break;
                        }

                    default: {
                            FlockLog.Warning(
                                this,
                                FlockLogCategory.Controller,
                                $"StartLayer3Pattern: runtime kind '{cmd.Kind}' is not implemented in controller yet.",
                                this);
                            break;
                        }
                }
            }

            return outHandles.Count > 0;
        }

        void BuildLayer3PatternRuntime(
            in FlockEnvironmentData env,
            out FlockLayer3PatternCommand[] commands,
            out FlockLayer3PatternSphereShell[] sphereShells,
            out FlockLayer3PatternBoxShell[] boxShells) {

            if (layer3Patterns == null || layer3Patterns.Length == 0) {
                commands = Array.Empty<FlockLayer3PatternCommand>();
                sphereShells = Array.Empty<FlockLayer3PatternSphereShell>();
                boxShells = Array.Empty<FlockLayer3PatternBoxShell>();
                return;
            }

            var cmdList = new List<FlockLayer3PatternCommand>(layer3Patterns.Length);
            var sphereList = new List<FlockLayer3PatternSphereShell>(layer3Patterns.Length);
            var boxList = new List<FlockLayer3PatternBoxShell>(layer3Patterns.Length);

            for (int i = 0; i < layer3Patterns.Length; i += 1) {
                var profile = layer3Patterns[i];
                if (profile == null) {
                    continue;
                }

                profile.Bake(
                    env,
                    fishTypes,
                    cmdList,
                    sphereList,
                    boxList);
            }

            commands = cmdList.Count > 0 ? cmdList.ToArray() : Array.Empty<FlockLayer3PatternCommand>();
            sphereShells = sphereList.Count > 0 ? sphereList.ToArray() : Array.Empty<FlockLayer3PatternSphereShell>();
            boxShells = boxList.Count > 0 ? boxList.ToArray() : Array.Empty<FlockLayer3PatternBoxShell>();
        }
    }
}
