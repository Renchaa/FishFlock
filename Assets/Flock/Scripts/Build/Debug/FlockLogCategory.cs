namespace Flock.Scripts.Build.Debug {
    using System;

    /**
     * <summary>
     * Bitmask categories used to filter flock logging output.
     * </summary>
     */
    [Flags]
    public enum FlockLogCategory {
        None = 0,
        General = 1 << 0,
        Controller = 1 << 1,
        Simulation = 1 << 2,
        Spawner = 1 << 3,
        Obstacles = 1 << 4,
        Attractors = 1 << 5,
        Patterns = 1 << 6,

        All = General
            | Controller
            | Simulation
            | Spawner
            | Obstacles
            | Attractors
            | Patterns
    }
}
