using System;

namespace Flock.Scripts.Build.Agents.Fish.Data {

    /**
     * <summary>
     * Bitmask tags used to categorize fish types.
     * </summary>
     */
    [Flags]
    public enum FishTag {
        None = 0,
        GoldFish = 1 << 0,
        Tuna = 1 << 1,
        Whale = 1 << 2,
        All = ~0,
    }
}
