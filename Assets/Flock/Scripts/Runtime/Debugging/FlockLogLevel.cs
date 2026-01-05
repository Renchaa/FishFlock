// REPLACE FILE: Assets/Flock/Runtime/Logging/FlockLogLevel.cs
namespace Flock.Runtime.Logging {
    using System;

    /**
     * <summary>
     * Bitmask levels used to filter flock logging output.
     * </summary>
     */
    [Flags]
    public enum FlockLogLevel {
        None = 0,
        Info = 1 << 0,
        Warning = 1 << 1,
        Error = 1 << 2,
        All = Info | Warning | Error
    }
}
