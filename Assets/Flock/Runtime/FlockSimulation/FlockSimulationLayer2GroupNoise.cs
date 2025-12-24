namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;

    public sealed partial class FlockSimulation {
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

        public void SetLayer2GroupNoiseVortex(
            in FlockGroupNoiseCommonSettings common,
            in FlockGroupNoiseVortexPayload payload) {

            activeLayer2GroupNoiseKind = FlockGroupNoisePatternType.Vortex;
            activeLayer2GroupNoiseCommon = common;
            activeLayer2Vortex = payload;

            FlockLog.Info(
                logger,
                FlockLogCategory.Patterns,
                "Layer-2 GroupNoise set to Vortex.",
                null);
        }

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
