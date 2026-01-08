using Flock.Scripts.Build.Influence.PatternVolume.Profiles;
using Flock.Scripts.Build.Influence.PatternVolume.Data;
using Flock.Scripts.Build.Influence.Environment.Data;

using System;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using Flock.Scripts.Build.Debug;

namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController
{
    /**
     * <summary>
     * Runtime controller that owns flock simulation state, configuration, and per-frame updates.
     * </summary>
     */
    public sealed partial class FlockController
    {
        private readonly List<PatternVolumeCommand> runtimePatternVolumeCmdScratch = new List<PatternVolumeCommand>(4);
        private readonly List<PatternVolumeSphereShell> runtimePatternVolumeSphereScratch = new List<PatternVolumeSphereShell>(4);
        private readonly List<PatternVolumeBoxShell> runtimePatternVolumeBoxShellScratch = new List<PatternVolumeBoxShell>(4);
        private readonly List<PatternVolumeHandle> runtimePatternVolumeScratch = new List<PatternVolumeHandle>(4);

        /**
         * <summary>
         * Starts a Layer-3 pattern from the provided profile and returns a token that can be updated or stopped.
         * </summary>
         * <param name="profile">The profile to bake into runtime Layer-3 handles.</param>
         * <returns>A token representing the running runtime handles, or null on failure.</returns>
         */
        public PatternVolumeToken StartPatternVolume(PatternVolumeFlockProfile profile)
        {
            if (!TryValidateStartPatternVolumeInputs(profile))
            {
                return null;
            }

            runtimePatternVolumeScratch.Clear();

            if (!TryBuildRuntimePatternVolumeFromProfile(profile, runtimePatternVolumeScratch))
            {
                LogStartPatternVolumeProducedNoCommands(profile);
                return null;
            }

            return CreatePatternVolumeToken(profile);
        }

        /**
         * <summary>
         * Re-bakes the provided profile and updates the runtime handles stored in the token.
         * </summary>
         * <param name="token">The token representing the running runtime handles.</param>
         * <param name="profile">The profile to bake and apply.</param>
         * <returns>True if all handles were updated successfully; false otherwise.</returns>
         */
        public bool UpdatePatternVolume(PatternVolumeToken token, PatternVolumeFlockProfile profile)
        {
            if (!TryValidateUpdatePatternVolumeInputs(token, profile))
            {
                return false;
            }

            BakePaternVolumeProfileToScratch(profile);

            if (runtimePatternVolumeCmdScratch.Count == 0)
            {
                return StopPatternVolumeBecauseNoCommands(token, profile);
            }

            if (token.HandleCount != runtimePatternVolumeCmdScratch.Count)
            {
                return RebuildPatternVolumeToken(token, profile);
            }

            return TryUpdatePatternVolumeTokenHandles(token, profile);
        }

        /**
         * <summary>
         * Stops a running Layer-3 pattern token and invalidates the token.
         * </summary>
         * <param name="token">The token to stop.</param>
         * <returns>True if any runtime handles were stopped; false otherwise.</returns>
         */
        public bool StopPatternVolume(PatternVolumeToken token)
        {
            if (simulation == null || !simulation.IsCreated)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (token == null || !token.IsValid)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopLayer3Pattern called on '{name}' with null or invalid token.",
                    this);
                return false;
            }

            bool anyStopped = false;

            int handleCount = token.HandleCount;
            for (int index = 0; index < handleCount; index += 1)
            {
                anyStopped |= simulation.StopPatternVolume(token.GetHandle(index));
            }

            token.Invalidate();

            if (anyStopped)
            {
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
        public PatternVolumeHandle StartRuntimeSphereShell(
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue)
        {
            if (simulation == null || !simulation.IsCreated)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartRuntimeSphereShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return PatternVolumeHandle.Invalid;
            }

            return simulation.StartPatternSphereShell(center, radius, thickness, strength, behaviourMask);
        }

        /**
         * <summary>
         * Updates a runtime sphere-shell pattern directly in the simulation.
         * </summary>
         */
        public bool UpdateRuntimeSphereShell(
            PatternVolumeHandle handle,
            float3 center,
            float radius,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue)
        {
            if (simulation == null || !simulation.IsCreated)
            {
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
        public PatternVolumeHandle StartRuntimeBoxShell(
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue)
        {
            if (simulation == null || !simulation.IsCreated)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartRuntimeBoxShell called on '{name}' but simulation is not created or already disposed.",
                    this);
                return PatternVolumeHandle.Invalid;
            }

            return simulation.StartPatternBoxShell(center, halfExtents, thickness, strength, behaviourMask);
        }

        /**
         * <summary>
         * Updates a runtime box-shell pattern directly in the simulation.
         * </summary>
         */
        public bool UpdateRuntimeBoxShell(
            PatternVolumeHandle handle,
            float3 center,
            float3 halfExtents,
            float thickness = -1f,
            float strength = 1f,
            uint behaviourMask = uint.MaxValue)
        {
            if (simulation == null || !simulation.IsCreated)
            {
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
        public bool StopRuntimePattern(PatternVolumeHandle handle)
        {
            if (simulation == null || !simulation.IsCreated)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StopRuntimePattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            return simulation.StopPatternVolume(handle);
        }

        private bool TryValidateStartPatternVolumeInputs(PatternVolumeFlockProfile profile)
        {
            if (simulation == null || !simulation.IsCreated)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (profile == null)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"StartLayer3Pattern called on '{name}' with null profile.",
                    this);
                return false;
            }

            return true;
        }

        private bool TryValidateUpdatePatternVolumeInputs(PatternVolumeToken token, PatternVolumeFlockProfile profile)
        {
            if (simulation == null || !simulation.IsCreated)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' but simulation is not created or already disposed.",
                    this);
                return false;
            }

            if (token == null)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' with null token.",
                    this);
                return false;
            }

            if (profile == null)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern called on '{name}' with null profile.",
                    this);
                return false;
            }

            return true;
        }

        private void BakePaternVolumeProfileToScratch(PatternVolumeFlockProfile profile)
        {
            FlockEnvironmentData environmentData = BuildEnvironmentData();

            runtimePatternVolumeCmdScratch.Clear();
            runtimePatternVolumeSphereScratch.Clear();
            runtimePatternVolumeBoxShellScratch.Clear();

            profile.Bake(environmentData, fishTypes, runtimePatternVolumeCmdScratch, runtimePatternVolumeSphereScratch, runtimePatternVolumeBoxShellScratch);
        }

        private bool StopPatternVolumeBecauseNoCommands(PatternVolumeToken token, PatternVolumeFlockProfile profile)
        {
            StopPatternVolume(token);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"UpdateLayer3Pattern on '{name}' stopped token because profile '{profile.name}' baked no commands.",
                this);

            return true;
        }

        private bool RebuildPatternVolumeToken(PatternVolumeToken token, PatternVolumeFlockProfile profile)
        {
            StopPatternVolume(token);

            runtimePatternVolumeScratch.Clear();

            if (!TryBuildRuntimePatternVolumeFromProfile(profile, runtimePatternVolumeScratch))
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' failed to rebuild token for profile '{profile.name}'.",
                    this);
                return false;
            }

            token.SetHandles(runtimePatternVolumeScratch);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"UpdateLayer3Pattern on '{name}' rebuilt token for profile '{profile.name}' with {runtimePatternVolumeScratch.Count} handles.",
                this);

            return true;
        }

        private bool TryUpdatePatternVolumeTokenHandles(PatternVolumeToken token, PatternVolumeFlockProfile profile)
        {
            bool allOk = true;

            for (int index = 0; index < runtimePatternVolumeCmdScratch.Count; index += 1)
            {
                if (!TryUpdatePatternVolumeCommand(token, index, runtimePatternVolumeCmdScratch[index]))
                {
                    allOk = false;
                }
            }

            if (!allOk)
            {
                FlockLog.Warning(
                    this,
                    FlockLogCategory.Controller,
                    $"UpdateLayer3Pattern on '{name}' for profile '{profile.name}' had to restart or skip one or more runtime handles.",
                    this);
            }

            return allOk;
        }

        private bool TryUpdatePatternVolumeCommand(PatternVolumeToken token, int index, PatternVolumeCommand command)
        {
            PatternVolumeHandle handle = token.GetHandle(index);

            if (command.Strength <= 0f)
            {
                simulation.StopPatternVolume(handle);
                return false;
            }

            switch (command.Kind)
            {
                case PatternVolumeKind.SphereShell:
                    return TryUpdateSphereShellCommand(token, index, handle, command);

                case PatternVolumeKind.BoxShell:
                    return TryUpdateBoxShellCommand(token, index, handle, command);

                default:
                    return false;
            }
        }

        private bool TryUpdateSphereShellCommand(
            PatternVolumeToken token,
            int index,
            PatternVolumeHandle handle,
            PatternVolumeCommand command)
        {
            if ((uint)command.PayloadIndex >= (uint)runtimePatternVolumeSphereScratch.Count)
            {
                return false;
            }

            PatternVolumeSphereShell sphereShell = runtimePatternVolumeSphereScratch[command.PayloadIndex];

            bool ok = simulation.UpdatePatternSphereShell(
                handle,
                sphereShell.Center,
                sphereShell.Radius,
                sphereShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (ok)
            {
                return true;
            }

            simulation.StopPatternVolume(handle);

            PatternVolumeHandle newHandle = simulation.StartPatternSphereShell(
                sphereShell.Center,
                sphereShell.Radius,
                sphereShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (!newHandle.IsValid)
            {
                return false;
            }

            token.ReplaceHandle(index, newHandle);
            return true;
        }

        private bool TryUpdateBoxShellCommand(
            PatternVolumeToken token,
            int index,
            PatternVolumeHandle handle,
            PatternVolumeCommand command)
        {
            if ((uint)command.PayloadIndex >= (uint)runtimePatternVolumeBoxShellScratch.Count)
            {
                return false;
            }

            PatternVolumeBoxShell boxShell = runtimePatternVolumeBoxShellScratch[command.PayloadIndex];

            bool ok = simulation.UpdatePatternBoxShell(
                handle,
                boxShell.Center,
                boxShell.HalfExtents,
                boxShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (ok)
            {
                return true;
            }

            simulation.StopPatternVolume(handle);

            PatternVolumeHandle newHandle = simulation.StartPatternBoxShell(
                boxShell.Center,
                boxShell.HalfExtents,
                boxShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            if (!newHandle.IsValid)
            {
                return false;
            }

            token.ReplaceHandle(index, newHandle);
            return true;
        }

        private void LogStartPatternVolumeProducedNoCommands(PatternVolumeFlockProfile profile)
        {
            FlockLog.Warning(
                this,
                FlockLogCategory.Controller,
                $"StartLayer3Pattern on '{name}' for profile '{profile.name}' produced no valid runtime commands.",
                this);
        }

        private PatternVolumeToken CreatePatternVolumeToken(PatternVolumeFlockProfile profile)
        {
            PatternVolumeToken token = ScriptableObject.CreateInstance<PatternVolumeToken>();
            token.hideFlags = HideFlags.DontSave;
            token.SetHandles(runtimePatternVolumeScratch);

            FlockLog.Info(
                this,
                FlockLogCategory.Controller,
                $"Started Layer-3 pattern '{profile.name}' with {runtimePatternVolumeScratch.Count} runtime handles on '{name}'.",
                this);

            return token;
        }

        private bool TryBuildRuntimePatternVolumeFromProfile(
            PatternVolumeFlockProfile profile,
            List<PatternVolumeHandle> outHandles)
        {
            outHandles.Clear();

            BakePaternVolumeProfileToScratch(profile);

            if (runtimePatternVolumeCmdScratch.Count == 0)
            {
                return false;
            }

            AppendRuntimePatternVolumeHandles(outHandles);
            return outHandles.Count > 0;
        }

        private void AppendRuntimePatternVolumeHandles(List<PatternVolumeHandle> outHandles)
        {
            for (int index = 0; index < runtimePatternVolumeCmdScratch.Count; index += 1)
            {
                PatternVolumeCommand command = runtimePatternVolumeCmdScratch[index];
                if (command.Strength <= 0f)
                {
                    continue;
                }

                if (!TryStartRuntimeHandleFromCommand(command, out PatternVolumeHandle handle))
                {
                    continue;
                }

                if (handle.IsValid)
                {
                    outHandles.Add(handle);
                }
            }
        }

        private bool TryStartRuntimeHandleFromCommand(PatternVolumeCommand command, out PatternVolumeHandle handle)
        {
            handle = PatternVolumeHandle.Invalid;

            switch (command.Kind)
            {
                case PatternVolumeKind.SphereShell:
                    return TryStartSphereShellHandle(command, out handle);

                case PatternVolumeKind.BoxShell:
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

        private bool TryStartSphereShellHandle(PatternVolumeCommand command, out PatternVolumeHandle handle)
        {
            handle = PatternVolumeHandle.Invalid;

            if ((uint)command.PayloadIndex >= (uint)runtimePatternVolumeSphereScratch.Count)
            {
                return false;
            }

            PatternVolumeSphereShell sphereShell = runtimePatternVolumeSphereScratch[command.PayloadIndex];

            handle = simulation.StartPatternSphereShell(
                sphereShell.Center,
                sphereShell.Radius,
                sphereShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            return true;
        }

        private bool TryStartBoxShellHandle(PatternVolumeCommand command, out PatternVolumeHandle handle)
        {
            handle = PatternVolumeHandle.Invalid;

            if ((uint)command.PayloadIndex >= (uint)runtimePatternVolumeBoxShellScratch.Count)
            {
                return false;
            }

            PatternVolumeBoxShell boxShell = runtimePatternVolumeBoxShellScratch[command.PayloadIndex];

            handle = simulation.StartPatternBoxShell(
                boxShell.Center,
                boxShell.HalfExtents,
                boxShell.Thickness,
                command.Strength,
                command.BehaviourMask);

            return true;
        }

        private void BuildPatternVoulmeRuntime(
            in FlockEnvironmentData environmentData,
            out PatternVolumeCommand[] commands,
            out PatternVolumeSphereShell[] sphereShells,
            out PatternVolumeBoxShell[] boxShells)
        {
            if (!TryCreatePatternVolumeRuntimeLists(out List<PatternVolumeCommand> commandList,
                    out List<PatternVolumeSphereShell> sphereShellList,
                    out List<PatternVolumeBoxShell> boxShellList))
            {
                commands = Array.Empty<PatternVolumeCommand>();
                sphereShells = Array.Empty<PatternVolumeSphereShell>();
                boxShells = Array.Empty<PatternVolumeBoxShell>();
                return;
            }

            BakePatternProfiles(environmentData, commandList, sphereShellList, boxShellList);
            ConvertPatternProfilesRuntimeListsToArrays(commandList, sphereShellList, boxShellList, out commands, out sphereShells, out boxShells);
        }

        private bool TryCreatePatternVolumeRuntimeLists(
            out List<PatternVolumeCommand> commandList,
            out List<PatternVolumeSphereShell> sphereShellList,
            out List<PatternVolumeBoxShell> boxShellList)
        {
            commandList = null;
            sphereShellList = null;
            boxShellList = null;

            if (layer3Patterns == null || layer3Patterns.Length == 0)
            {
                return false;
            }

            int capacity = layer3Patterns.Length;
            commandList = new List<PatternVolumeCommand>(capacity);
            sphereShellList = new List<PatternVolumeSphereShell>(capacity);
            boxShellList = new List<PatternVolumeBoxShell>(capacity);

            return true;
        }

        private void BakePatternProfiles(
            in FlockEnvironmentData environmentData,
            List<PatternVolumeCommand> commandList,
            List<PatternVolumeSphereShell> sphereShellList,
            List<PatternVolumeBoxShell> boxShellList)
        {
            for (int index = 0; index < layer3Patterns.Length; index += 1)
            {
                PatternVolumeFlockProfile profile = layer3Patterns[index];
                if (profile == null)
                {
                    continue;
                }

                profile.Bake(environmentData, fishTypes, commandList, sphereShellList, boxShellList);
            }
        }

        private void ConvertPatternProfilesRuntimeListsToArrays(
            List<PatternVolumeCommand> commandList,
            List<PatternVolumeSphereShell> sphereShellList,
            List<PatternVolumeBoxShell> boxShellList,
            out PatternVolumeCommand[] commands,
            out PatternVolumeSphereShell[] sphereShells,
            out PatternVolumeBoxShell[] boxShells)
        {
            commands = commandList.Count > 0 ? commandList.ToArray() : Array.Empty<PatternVolumeCommand>();
            sphereShells = sphereShellList.Count > 0 ? sphereShellList.ToArray() : Array.Empty<PatternVolumeSphereShell>();
            boxShells = boxShellList.Count > 0 ? boxShellList.ToArray() : Array.Empty<PatternVolumeBoxShell>();
        }
    }
}
