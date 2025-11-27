// ==========================================
// 1) NEW ENUM: FlockAttractorUsage
// File: Assets/Flock/Runtime/Data/FlockAttractorUsage.cs
// ==========================================
namespace Flock.Runtime.Data {
    /// <summary>
    /// How an attractor is intended to be used.
    /// Individual  = sampled per-agent.
    /// Group       = sampled per school / group centre (future step).
    /// </summary>
    public enum FlockAttractorUsage {
        Individual = 0,
        Group = 1,
    }
}
