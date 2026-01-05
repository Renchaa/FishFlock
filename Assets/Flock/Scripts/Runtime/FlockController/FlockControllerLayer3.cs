using System;
using System.Collections.Generic;
using Flock.Runtime.Data;
using Flock.Runtime.Logging;
using Unity.Mathematics;
using UnityEngine;

namespace Flock.Runtime {
    /**
     * <summary>
     * Runtime controller that owns flock simulation state, configuration, and per-frame updates.
     * </summary>
     */
    public sealed partial class FlockController {
        private readonly List<FlockLayer3PatternCommand> runtimeL3CmdScratch = new List<FlockLayer3PatternCommand>(4);
        private readonly List<FlockLayer3PatternSphereShell> runtimeL3SphereScratch = new List<FlockLayer3PatternSphereShell>(4);
        private readonly List<FlockLayer3PatternBoxShell> runtimeL3BoxShellScratch = new List<FlockLayer3PatternBoxShell>(4);
        private readonly List<FlockLayer3PatternHandle> runtimeL3HandleScratch = new List<FlockLayer3PatternHandle>(4);

        /**
         * <summary>
         * Starts a Layer-3 pattern from the provided profile and returns a token that can be updated or stopped.
         * </summary>
         * <param name="profile">The profile to bake into runtime Layer-3 handles.</param>
         * <returns>A token representing the running runtime handles, or null on failure.</returns>
         */
        public FlockLayer3PatternToken StartLayer3Pattern(FlockLayer3PatternProfile profile) {
            if (!TryValidateStartLayer3PatternInputs(profile)) {
                return null;
            }

            runtimeL3HandleScratch.Clear();

            if (!TryBuildRuntimeLayer3FromProfile(profile, runtimeL3HandleScratch)) {
                LogStartLayer3PatternProducedNoCommands(profile);
                return null;
            }

            return CreateLayer3PatternToken(profile);
        }

        /**
         * <summary>
         * Re-bakes the provided profile and updates the runtime handles stored in the token.
         * </summary>
         * <param name="token">The token representing the running runtime handles.</param>
         * <param name="profile">The profile to bake and apply.</param>
         * <returns>True if all handles were updated successfully; false otherwise.</returns>
         */
        public bool UpdateLayer3Pattern(FlockLayer3PatternToken token, FlockLayer3PatternProfile profile) {
            if (!TryValidateUpdateLayer3PatternInputs(token, profile)) {
                return false;
            }

            BakeLayer3ProfileToScratch(profile);

            if (runtimeL3CmdScratch.Count == 0) {
                return StopLayer3PatternBecauseNoCommands(token, profile);
            }

            if (token.HandleCount != runtimeL3CmdScratch.Count) {
                return RebuildLayer3PatternToken(token, profile);
            }

            return TryUpdateLayer3TokenHandles(token, profile);
        }

        /**
         * <summary>
         * Stops a running Layer-3 pattern token and invalidates the token.
         * </summary>
         * <param name="token">The token to stop.</param>
         * <returns>True if any runtime handles were stopped; false otherwise.</returns>
         */
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

            int handleCount = token.HandleCount;
            for (int index = 0; index < handleCount; index += 1) {
                anyStopped |= simulation.StopLayer3Pattern(token.GetHandle(index));
            }

            token.Invalidate();

            if (anyStopped) {
                FlockLog.Info(
                    this,
                    FlockLogCategory.Controller,
                    $"Stopped Layer-3 pattern token with {handleCount} handles on '{name}'.",
                    this);
            }

            return anyStopped;
        }

        /**
         * <summary>
         * Starts a runtime sphere-shell pattern directly in the simulation.
         * </summary>
         */
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

            return simulation.StartPatternSphereShell(center, radius, thickness, strength, behaviourMask);
        }

        /**
         * <summary>
         * Updates a runtime sphere-shell pattern directly in the simulation.
         * </summary>
         */
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

            return simulation.UpdatePatternSphereShell(handle, center, radius, thickness, strength, behaviourMask);
        }

        /**
         * <summary>
         * Starts a runtime box-shell pattern directly in the simulation.
         * </summary>
         */
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

            return simulation.StartPatternBoxShell(center, halfExtents, thickness, strength, behaviourMask);
        }

        /**
         * <summary>
         * Updates a runtime box-shell pattern directly in the simulation.
         * </summary>
         */
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

            return simulation.UpdatePatternBoxShell(handle, center, halfExtents, thickness, strength, behaviourMask);
        }

        /**
         * <summary>
         * Stops a runtime Layer-3 pattern handle directly in the simulation.
         * </summary>
         */
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

        private bool TryValidateStartLayer3PatternInputs(FlockLayer3PatternProfile profile) {
            if (simulation == null || !simulation.IsCreated) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (profile == null) {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' with null profile.",
                    this);
                return false;
            }

            return true;
        }

        private bool TryValidateUpdateLayer3PatternInputs(FlockLayer3PatternToken token, FlockLayer3PatternProfile profile) {
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

            return true;
        }

        private void BakeLayer3ProfileToScratch(FlockLayer3PatternProfile profile) {
            FlockEnvironmentData environmentData = BuildEnvironmentData();

            runtimeL3CmdScratch.Clear();
            runtimeL3SphereScratch.Clear();
            runtimeL3BoxShellScratch.Clear();

            profile.Bake(environmentData, fishTypes, runtimeL3CmdScratch, runtimeL3SphereScratch, runtimeL3BoxShellScratch);
        }

        private bool StopLayer3PatternBecauseNoCommands(FlockLayer3PatternToken token, FlockLayer3PatternProfile profile) {
            StopLayer3Pattern(token);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"UpdateLayer3Pattern on '{name}' stopped token because profile '{profile.name}' baked no commands.",
                this);

            return true;
        }

        private bool RebuildLayer3PatternToken(FlockLayer3PatternToken token, FlockLayer3PatternProfile profile) {
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

        private bool TryUpdateLayer3TokenHandles(FlockLayer3PatternToken token, FlockLayer3PatternProfile profile) {
            bool allOk = true;

            for (int index = 0; index < runtimeL3CmdScratch.Count; index += 1) {
                if (!TryUpdateLayer3Command(token, index, runtimeL3CmdScratch[index])) {
                    allOk = false;
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

        private bool TryUpdateLayer3Command(FlockLayer3PatternToken token, int index, FlockLayer3PatternCommand command) {
            FlockLayer3PatternHandle handle = token.GetHandle(index);

            if (command.Strength <= 0f) {
                simulation.StopLayer3Pattern(handle);
                return false;
            }

            switch (command.Kind) {
                case FlockLayer3PatternKind.SphereShell:
                    return TryUpdateSphereShellCommand(token, index, handle, command);

                case FlockLayer3PatternKind.BoxShell:
                    return TryUpdateBoxShellCommand(token, index, handle, command);

                default:
                    return false;
            }
        }

        private bool TryUpdateSphereShellCommand(
            FlockLayer3PatternToken token,
            int index,
            FlockLayer3PatternHandle handle,
            FlockLayer3PatternCommand command) {
            if ((uint)command.PayloadIndex >= (uint)runtimeL3SphereScratch.Count) {
                return false;
            }

            FlockLayer3PatternSphereShell sphereShell = runtimeL3SphereScratch[command.PayloadIndex];

            bool ok = simulation.UpdatePatternSphereShell(
                handle,
                sphereShell.Center,
                sphereShell.Radius,
                sphereShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (ok) {
                return true;
            }

            simulation.StopLayer3Pattern(handle);

            FlockLayer3PatternHandle newHandle = simulation.StartPatternSphereShell(
                sphereShell.Center,
                sphereShell.Radius,
                sphereShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (!newHandle.IsValid) {
                return false;
            }

            token.ReplaceHandle(index, newHandle);
            return true;
        }

        private bool TryUpdateBoxShellCommand(
            FlockLayer3PatternToken token,
            int index,
            FlockLayer3PatternHandle handle,
            FlockLayer3PatternCommand command) {
            if ((uint)command.PayloadIndex >= (uint)runtimeL3BoxShellScratch.Count) {
                return false;
            }

            FlockLayer3PatternBoxShell boxShell = runtimeL3BoxShellScratch[command.PayloadIndex];

            bool ok = simulation.UpdatePatternBoxShell(
                handle,
                boxShell.Center,
                boxShell.HalfExtents,
                boxShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (ok) {
                return true;
            }

            simulation.StopLayer3Pattern(handle);

            FlockLayer3PatternHandle newHandle = simulation.StartPatternBoxShell(
                boxShell.Center,
                boxShell.HalfExtents,
                boxShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (!newHandle.IsValid) {
                return false;
            }

            token.ReplaceHandle(index, newHandle);
            return true;
        }

        private void LogStartLayer3PatternProducedNoCommands(FlockLayer3PatternProfile profile) {
            FlockLog.Warning(
                this,
                FlockLogCategory.Controller,
                $"StartLayer3Pattern on '{name}' for profile '{profile.name}' produced no valid runtime commands.",
                this);
        }

        private FlockLayer3PatternToken CreateLayer3PatternToken(FlockLayer3PatternProfile profile) {
            FlockLayer3PatternToken token = ScriptableObject.CreateInstance<FlockLayer3PatternToken>();
            token.hideFlags = HideFlags.DontSave;
            token.SetHandles(runtimeL3HandleScratch);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Started Layer-3 pattern '{profile.name}' with {runtimeL3HandleScratch.Count} runtime handles on '{name}'.",
                this);

            return token;
        }

        private bool TryBuildRuntimeLayer3FromProfile(
            FlockLayer3PatternProfile profile,
            List<FlockLayer3PatternHandle> outHandles) {
            outHandles.Clear();

            BakeLayer3ProfileToScratch(profile);

            if (runtimeL3CmdScratch.Count == 0) {
                return false;
            }

            AppendRuntimeLayer3Handles(outHandles);
            return outHandles.Count > 0;
        }

        private void AppendRuntimeLayer3Handles(List<FlockLayer3PatternHandle> outHandles) {
            for (int index = 0; index < runtimeL3CmdScratch.Count; index += 1) {
                FlockLayer3PatternCommand command = runtimeL3CmdScratch[index];
                if (command.Strength <= 0f) {
                    continue;
                }

                if (!TryStartRuntimeHandleFromCommand(command, out FlockLayer3PatternHandle handle)) {
                    continue;
                }

                if (handle.IsValid) {
                    outHandles.Add(handle);
                }
            }
        }

        private bool TryStartRuntimeHandleFromCommand(FlockLayer3PatternCommand command, out FlockLayer3PatternHandle handle) {
            handle = FlockLayer3PatternHandle.Invalid;

            switch (command.Kind) {
                case FlockLayer3PatternKind.SphereShell:
                    return TryStartSphereShellHandle(command, out handle);

                case FlockLayer3PatternKind.BoxShell:
                    return TryStartBoxShellHandle(command, out handle);

                default:
                    FlockLog.Warning(
                        this,
                        FlockLogCategory.Controller,
                        $"StartLayer3Pattern: runtime kind '{command.Kind}' is not implemented in controller yet.",
                        this);
                    return false;
            }
        }

        private bool TryStartSphereShellHandle(FlockLayer3PatternCommand command, out FlockLayer3PatternHandle handle) {
            handle = FlockLayer3PatternHandle.Invalid;

            if ((uint)command.PayloadIndex >= (uint)runtimeL3SphereScratch.Count) {
                return false;
            }

            FlockLayer3PatternSphereShell sphereShell = runtimeL3SphereScratch[command.PayloadIndex];

            handle = simulation.StartPatternSphereShell(
                sphereShell.Center,
                sphereShell.Radius,
                sphereShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            return true;
        }

        private bool TryStartBoxShellHandle(FlockLayer3PatternCommand command, out FlockLayer3PatternHandle handle) {
            handle = FlockLayer3PatternHandle.Invalid;

            if ((uint)command.PayloadIndex >= (uint)runtimeL3BoxShellScratch.Count) {
                return false;
            }

            FlockLayer3PatternBoxShell boxShell = runtimeL3BoxShellScratch[command.PayloadIndex];

            handle = simulation.StartPatternBoxShell(
                boxShell.Center,
                boxShell.HalfExtents,
                boxShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            return true;
        }

        private void BuildLayer3PatternRuntime(
            in FlockEnvironmentData environmentData,
            out FlockLayer3PatternCommand[] commands,
            out FlockLayer3PatternSphereShell[] sphereShells,
            out FlockLayer3PatternBoxShell[] boxShells) {
            if (!TryCreateLayer3RuntimeLists(out List<FlockLayer3PatternCommand> commandList,
                    out List<FlockLayer3PatternSphereShell> sphereShellList,
                    out List<FlockLayer3PatternBoxShell> boxShellList)) {
                commands = Array.Empty<FlockLayer3PatternCommand>();
                sphereShells = Array.Empty<FlockLayer3PatternSphereShell>();
                boxShells = Array.Empty<FlockLayer3PatternBoxShell>();
                return;
            }

            BakeLayer3Profiles(environmentData, commandList, sphereShellList, boxShellList);
            ConvertLayer3RuntimeListsToArrays(commandList, sphereShellList, boxShellList, out commands, out sphereShells, out boxShells);
        }

        private bool TryCreateLayer3RuntimeLists(
            out List<FlockLayer3PatternCommand> commandList,
            out List<FlockLayer3PatternSphereShell> sphereShellList,
            out List<FlockLayer3PatternBoxShell> boxShellList) {
            commandList = null;
            sphereShellList = null;
            boxShellList = null;

            if (layer3Patterns == null || layer3Patterns.Length == 0) {
                return false;
            }

            int capacity = layer3Patterns.Length;
            commandList = new List<FlockLayer3PatternCommand>(capacity);
            sphereShellList = new List<FlockLayer3PatternSphereShell>(capacity);
            boxShellList = new List<FlockLayer3PatternBoxShell>(capacity);

            return true;
        }

        private void BakeLayer3Profiles(
            in FlockEnvironmentData environmentData,
            List<FlockLayer3PatternCommand> commandList,
            List<FlockLayer3PatternSphereShell> sphereShellList,
            List<FlockLayer3PatternBoxShell> boxShellList) {
            for (int index = 0; index < layer3Patterns.Length; index += 1) {
                FlockLayer3PatternProfile profile = layer3Patterns[index];
                if (profile == null) {
                    continue;
                }

                profile.Bake(environmentData, fishTypes, commandList, sphereShellList, boxShellList);
            }
        }

        private static void ConvertLayer3RuntimeListsToArrays(
            List<FlockLayer3PatternCommand> commandList,
            List<FlockLayer3PatternSphereShell> sphereShellList,
            List<FlockLayer3PatternBoxShell> boxShellList,
            out FlockLayer3PatternCommand[] commands,
            out FlockLayer3PatternSphereShell[] sphereShells,
            out FlockLayer3PatternBoxShell[] boxShells) {
            commands = commandList.Count > 0 ? commandList.ToArray() : Array.Empty<FlockLayer3PatternCommand>();
            sphereShells = sphereShellList.Count > 0 ? sphereShellList.ToArray() : Array.Empty<FlockLayer3PatternSphereShell>();
            boxShells = boxShellList.Count > 0 ? boxShellList.ToArray() : Array.Empty<FlockLayer3PatternBoxShell>();
        }
    }
}
