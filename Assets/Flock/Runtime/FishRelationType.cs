// File: Assets/Flock/Runtime/FishRelationType.cs
namespace Flock.Runtime {
    /// <summary>
    /// Relationship between two fish types in the interaction matrix.
    /// </summary>
    public enum FishRelationType {
        Neutral = 0,     // no special relation
        Friendly = 1, // flock together / leadership logic
        Avoid = 2,    // explicitly avoid this type
    }
}
