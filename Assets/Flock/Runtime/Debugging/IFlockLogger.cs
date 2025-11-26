// REPLACE FILE: Assets/Flock/Runtime/Logging/IFlockLogger.cs
namespace Flock.Runtime.Logging {
    public interface IFlockLogger {
        FlockLogLevel EnabledLevels { get; }
        FlockLogCategory EnabledCategories { get; }
    }
}
