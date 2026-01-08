using Flock.Scripts.Build.Debug;
using Flock.Scripts.Build.Influence.Noise.Data;
using Flock.Scripts.Build.Influence.Noise.Profiles;

namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockSimulation {

    /**
     * <summary>
     * Core flock simulation runtime. Owns native buffers, maintains simulation state, and schedules the per-frame job graph.
     * This partial definition contains the public Layer-2 group-noise configuration API.
     * </summary>
     */
    public sealed partial class FlockSimulation {
        /**
         * <summary>
         * Sets the active Layer-2 group-noise pattern to <see cref="FlockGroupNoisePatternType.SimpleSine"/>.
         * </summary>
         * <param name="common">Common group-noise settings applied to the active Layer-2 pattern.</param>
         * <param name="payload">Simple-sine pattern payload for the active Layer-2 pattern.</param>
         */
        public void SetLayer2GroupNoiseSimpleSine(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseSimpleSinePayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.SimpleSine;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2SimpleSine = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to SimpleSine.",
                null);
        }

        /**
         * <summary>
         * Sets the active Layer-2 group-noise pattern to <see cref="FlockGroupNoisePatternType.VerticalBands"/>.
         * </summary>
         * <param name="common">Common group-noise settings applied to the active Layer-2 pattern.</param>
         * <param name="payload">Vertical-bands pattern payload for the active Layer-2 pattern.</param>
         */
        public void SetLayer2GroupNoiseVerticalBands(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseVerticalBandsPayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.VerticalBands;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2VerticalBands = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to VerticalBands.",
                null);
        }

        /**
         * <summary>
         * Sets the active Layer-2 group-noise pattern to <see cref="FlockGroupNoisePatternType.Vortex"/>.
         * </summary>
         * <param name="common">Common group-noise settings applied to the active Layer-2 pattern.</param>
         * <param name="payload">Vortex pattern payload for the active Layer-2 pattern.</param>
         */
        public void SetLayer2GroupNoiseVortex(
            in FlockGroupNoiseCommonSettings common,
            in GroupNoiseVortexPayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.Vortex;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2Vortex = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to Vortex.",
                null);
        }

        /**
         * <summary>
         * Sets the active Layer-2 group-noise pattern to <see cref="FlockGroupNoisePatternType.SphereShell"/>.
         * </summary>
         * <param name="common">Common group-noise settings applied to the active Layer-2 pattern.</param>
         * <param name="payload">Sphere-shell pattern payload for the active Layer-2 pattern.</param>
         */
        public void SetLayer2GroupNoiseSphereShell(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseSphereShellPayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.SphereShell;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2SphereShell = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to SphereShell.",
                null);
        }
    }

}
