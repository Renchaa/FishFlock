using System;

namespace Flock.Scripts.Build.Debug
{
    /**
     * <summary>
     * Bitmask levels used to filter flock logging output.
     * </summary>
     */
    [Flags]
    public enum FlockLogLevel
    {
        None = 0,
        Info = 1 << 0,
        Warning = 1 << 1,
        Error = 1 << 2,
        All = Info | Warning | Error
    }
}
