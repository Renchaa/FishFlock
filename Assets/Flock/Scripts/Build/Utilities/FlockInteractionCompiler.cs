using Flock.Scripts.Build.Agents.Fish.Profiles;
using Flock.Scripts.Build.Agents.Fish.Data;
using Flock.Scripts.Build.Debug;

namespace Flock.Scripts.Build.Utilities.Data
{
    /**
     * <summary>
     * Compiles a <see cref="FishInteractionMatrix"/> into per-type scalar weights and 32-bit relationship masks.
     * </summary>
     */
    public static class FlockInteractionCompiler
    {
        private const int RelationMaskBitCount = 32;

        /**
         * <summary>
         * Builds compiled interaction data from the provided fish types and interaction matrix.
         * </summary>
         * <param name="fishTypes">Ordered fish type presets used as indices into the matrix.</param>
         * <param name="matrix">Interaction matrix source.</param>
         * <param name="leadershipWeights">Per-type leadership weights (length = fishTypes.Length).</param>
         * <param name="avoidanceWeights">Per-type avoidance weights (length = fishTypes.Length).</param>
         * <param name="neutralWeights">Per-type neutral weights (length = fishTypes.Length).</param>
         * <param name="friendlyMasks">Per-type friendly bit masks (length = fishTypes.Length, 32-bit).</param>
         * <param name="avoidMasks">Per-type avoid bit masks (length = fishTypes.Length, 32-bit).</param>
         * <param name="neutralMasks">Per-type neutral bit masks (length = fishTypes.Length, 32-bit).</param>
         */
        public static void BuildInteractionData(
            FishTypePreset[] fishTypes,
            FishInteractionMatrix matrix,
            out float[] leadershipWeights,
            out float[] avoidanceWeights,
            out float[] neutralWeights,
            out uint[] friendlyMasks,
            out uint[] avoidMasks,
            out uint[] neutralMasks)
        {
            int fishTypeCount = fishTypes != null ? fishTypes.Length : 0;

            AllocateOutputs(
                fishTypeCount,
                out leadershipWeights,
                out avoidanceWeights,
                out neutralWeights,
                out friendlyMasks,
                out avoidMasks,
                out neutralMasks);

            LogWarnings(fishTypeCount, matrix);

            if (fishTypeCount == 0 || matrix == null)
            {
                return;
            }

            matrix.SyncSizeWithFishTypes();

            CopyPerTypeWeights(fishTypeCount, matrix, leadershipWeights, avoidanceWeights, neutralWeights);
            BuildRelationshipMasks(fishTypeCount, matrix, friendlyMasks, avoidMasks, neutralMasks);
            EnforceMaskSymmetry(fishTypeCount, friendlyMasks, avoidMasks, neutralMasks);
        }

        private static void AllocateOutputs(
            int fishTypeCount,
            out float[] leadershipWeights,
            out float[] avoidanceWeights,
            out float[] neutralWeights,
            out uint[] friendlyMasks,
            out uint[] avoidMasks,
            out uint[] neutralMasks)
        {
            leadershipWeights = new float[fishTypeCount];
            avoidanceWeights = new float[fishTypeCount];
            neutralWeights = new float[fishTypeCount];

            friendlyMasks = new uint[fishTypeCount];
            avoidMasks = new uint[fishTypeCount];
            neutralMasks = new uint[fishTypeCount];
        }

        private static void LogWarnings(int fishTypeCount, FishInteractionMatrix matrix)
        {
            if (fishTypeCount > RelationMaskBitCount)
            {
                FlockLog.WarningFormat(
                    null,
                    FlockLogCategory.Simulation,
                    matrix,
                    "FlockInteractionCompiler: fishTypes length ({0}) exceeds 32; relation masks are 32-bit. Types with index >= 32 will be ignored in relation masks.",
                    fishTypeCount);
            }

            if (fishTypeCount > 0 && matrix == null)
            {
                FlockLog.Warning(
                    null,
                    FlockLogCategory.Simulation,
                    "FlockInteractionCompiler: FishInteractionMatrix is null; interaction weights/masks will remain defaults (zero).",
                    matrix);
            }
        }

        private static void CopyPerTypeWeights(
            int fishTypeCount,
            FishInteractionMatrix matrix,
            float[] leadershipWeights,
            float[] avoidanceWeights,
            float[] neutralWeights)
        {
            for (int fishTypeIndex = 0; fishTypeIndex < fishTypeCount; fishTypeIndex++)
            {
                leadershipWeights[fishTypeIndex] = matrix.GetLeadershipWeight(fishTypeIndex);
                avoidanceWeights[fishTypeIndex] = matrix.GetAvoidanceWeight(fishTypeIndex);
                neutralWeights[fishTypeIndex] = matrix.GetNeutralWeight(fishTypeIndex);
            }
        }

        private static void BuildRelationshipMasks(
            int fishTypeCount,
            FishInteractionMatrix matrix,
            uint[] friendlyMasks,
            uint[] avoidMasks,
            uint[] neutralMasks)
        {
            for (int sourceTypeIndex = 0; sourceTypeIndex < fishTypeCount; sourceTypeIndex++)
            {
                for (int targetTypeIndex = 0; targetTypeIndex < fishTypeCount; targetTypeIndex++)
                {
                    if (!matrix.GetInteraction(sourceTypeIndex, targetTypeIndex))
                    {
                        continue;
                    }

                    if (targetTypeIndex >= RelationMaskBitCount)
                    {
                        continue; // Mask is 32-bit.
                    }

                    uint targetTypeBit = 1u << targetTypeIndex;
                    RelationType relation = matrix.GetRelation(sourceTypeIndex, targetTypeIndex);

                    ApplyRelationBit(
                        relation,
                        sourceTypeIndex,
                        targetTypeBit,
                        friendlyMasks,
                        avoidMasks,
                        neutralMasks);
                }
            }
        }

        private static void ApplyRelationBit(
            RelationType relation,
            int sourceTypeIndex,
            uint targetTypeBit,
            uint[] friendlyMasks,
            uint[] avoidMasks,
            uint[] neutralMasks)
        {
            switch (relation)
            {
                case RelationType.Friendly:
                    friendlyMasks[sourceTypeIndex] |= targetTypeBit;
                    return;

                case RelationType.Avoid:
                    avoidMasks[sourceTypeIndex] |= targetTypeBit;
                    return;

                case RelationType.Neutral:
                    neutralMasks[sourceTypeIndex] |= targetTypeBit;
                    return;
            }
        }

        private static void EnforceMaskSymmetry(
            int fishTypeCount,
            uint[] friendlyMasks,
            uint[] avoidMasks,
            uint[] neutralMasks)
        {
            // Just enforce symmetry to be safe (editor already does it, but whatever).
            for (int firstTypeIndex = 0; firstTypeIndex < fishTypeCount; firstTypeIndex++)
            {
                for (int secondTypeIndex = firstTypeIndex + 1;
                     secondTypeIndex < fishTypeCount && secondTypeIndex < RelationMaskBitCount;
                     secondTypeIndex++)
                {
                    uint firstTypeBit = 1u << firstTypeIndex;
                    uint secondTypeBit = 1u << secondTypeIndex;

                    SymmetrizePair(friendlyMasks, firstTypeIndex, secondTypeIndex, firstTypeBit, secondTypeBit);
                    SymmetrizePair(avoidMasks, firstTypeIndex, secondTypeIndex, firstTypeBit, secondTypeBit);
                    SymmetrizePair(neutralMasks, firstTypeIndex, secondTypeIndex, firstTypeBit, secondTypeBit);
                }
            }
        }

        private static void SymmetrizePair(
            uint[] masks,
            int firstTypeIndex,
            int secondTypeIndex,
            uint firstTypeBit,
            uint secondTypeBit)
        {
            if (((masks[firstTypeIndex] & secondTypeBit) != 0u) || ((masks[secondTypeIndex] & firstTypeBit) != 0u))
            {
                masks[firstTypeIndex] |= secondTypeBit;
                masks[secondTypeIndex] |= firstTypeBit;
            }
        }
    }
}
