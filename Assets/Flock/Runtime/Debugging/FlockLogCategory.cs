// REPLACE FILE: Assets/Flock/Runtime/Logging/FlockLogCategory.cs
namespace Flock.Runtime.Logging {
    using System;

    [Flags]
    public enum FlockLogCategory {
        None = 0,
        General = 1 << 0,
        Controller = 1 << 1,
        Simulation = 1 << 2,
        All = General | Controller | Simulation
    }
}
