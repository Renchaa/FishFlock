// REPLACE FILE: Assets/Flock/Runtime/Logging/IFlockLogger.cs
namespace Flock.Scripts.Build.Debug {
    /**
     * <summary>
     * Provides per-instance logging masks for flock subsystems.
     * </summary>
     */
    public interface IFlockLogger {
        /**
         * <summary>
         * Gets the enabled log levels for this logger instance.
         * </summary>
         */
        FlockLogLevel EnabledLevels { get; }

        /**
         * <summary>
         * Gets the enabled log categories for this logger instance.
         * </summary>
         */
        FlockLogCategory EnabledCategories { get; }
    }
}
