namespace Flock.Scripts.Build.Influence.PatternVolume.Data {

    /**
     * <summary>
     * Runtime command entry that references a Layer-3 pattern payload.
     * </summary>
     */
    public struct PatternVolumeCommand {
        // Command
        public PatternVolumeKind Kind;
        public int PayloadIndex;
        public float Strength;
        public uint BehaviourMask;
    }
}
