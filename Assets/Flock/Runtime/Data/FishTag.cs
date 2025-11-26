namespace Flock.Runtime.Data {
    using System;

    [Flags]
    public enum FishTag {
        None = 0,
        GoldFish = 1 << 0,
        Tuna = 1 << 1,
        Whale = 1 << 2,
        All = ~0
    }
}