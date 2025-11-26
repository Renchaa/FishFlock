// File: Assets/Flock/Runtime/Data/FlockInteractionCompiler.cs
namespace Flock.Runtime.Data {
    using Flock.Runtime;
    using UnityEngine;

    public static class FlockInteractionCompiler {
        public static void BuildInteractionData(
            FishTypePreset[] fishTypes,
            FishInteractionMatrix matrix,
            out float[] leadershipWeights,
            out float[] avoidanceWeights,
            out float[] neutralWeights,
            out uint[] friendlyMasks,
            out uint[] avoidMasks,
            out uint[] neutralMasks) {

            int n = fishTypes != null ? fishTypes.Length : 0;

            leadershipWeights = new float[n];
            avoidanceWeights = new float[n];
            neutralWeights = new float[n];
            friendlyMasks = new uint[n];
            avoidMasks = new uint[n];
            neutralMasks = new uint[n];

            if (n == 0 || matrix == null) {
                return;
            }

            // keep matrix internals in sync
            matrix.SyncSizeWithFishTypes();

            // per-type weights
            for (int i = 0; i < n; i++) {
                leadershipWeights[i] = matrix.GetLeadershipWeight(i);
                avoidanceWeights[i] = matrix.GetAvoidanceWeight(i);
                neutralWeights[i] = matrix.GetNeutralWeight(i);
            }

            // relationship → bit masks
            for (int a = 0; a < n; a++) {
                for (int b = 0; b < n; b++) {
                    if (!matrix.GetInteraction(a, b)) {
                        continue;
                    }

                    if (b >= 32) {
                        continue; // mask is 32-bit
                    }

                    uint bitB = 1u << b;
                    FishRelationType relation = matrix.GetRelation(a, b);

                    switch (relation) {
                        case FishRelationType.Friendly:
                            friendlyMasks[a] |= bitB;
                            break;
                        case FishRelationType.Avoid:
                            avoidMasks[a] |= bitB;
                            break;
                        case FishRelationType.Neutral:
                            neutralMasks[a] |= bitB;
                            break;
                    }
                }
            }

            // just enforce symmetry to be safe (editor already does it, but whatever)
            for (int a = 0; a < n; a++) {
                for (int b = a + 1; b < n && b < 32; b++) {
                    uint bitA = 1u << a;
                    uint bitB = 1u << b;

                    // Friendly
                    if (((friendlyMasks[a] & bitB) != 0u) || ((friendlyMasks[b] & bitA) != 0u)) {
                        friendlyMasks[a] |= bitB;
                        friendlyMasks[b] |= bitA;
                    }

                    // Avoid
                    if (((avoidMasks[a] & bitB) != 0u) || ((avoidMasks[b] & bitA) != 0u)) {
                        avoidMasks[a] |= bitB;
                        avoidMasks[b] |= bitA;
                    }

                    // Neutral
                    if (((neutralMasks[a] & bitB) != 0u) || ((neutralMasks[b] & bitA) != 0u)) {
                        neutralMasks[a] |= bitB;
                        neutralMasks[b] |= bitA;
                    }
                }
            }
        }
    }
}
