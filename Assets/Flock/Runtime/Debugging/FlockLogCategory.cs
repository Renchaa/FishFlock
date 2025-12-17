namespace Flock.Runtime.Logging {
    using System;

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

        // Add more bits later if needed (Editor, UI, etc).
        All = General
            | Controller
            | Simulation
            | Spawner
            | Obstacles
            | Attractors
            | Patterns
    }
}
