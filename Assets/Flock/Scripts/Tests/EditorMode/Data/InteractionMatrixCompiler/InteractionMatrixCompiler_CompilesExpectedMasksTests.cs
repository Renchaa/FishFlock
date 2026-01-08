#if UNITY_EDITOR
using Flock.Scripts.Build.Agents.Fish.Profiles;
using Flock.Scripts.Build.Agents.Fish.Data;
using NUnit.Framework;
using UnityEngine;
using Flock.Scripts.Build.Utilities.Data;

namespace Flock.Scripts.Tests.EditorMode.Data.InteractionMatrixCompiler {
    public sealed class InteractionMatrixCompiler_CompilesExpectedMasksTests {
        [Test]
        public void BuildInteractionData_CompilesExpectedMasks_FromSymmetricMatrix() {
            // Arrange
            const int typeCount = 4; // must be <= 32 for mask bits
            var fishTypes = InteractionMatrixTestUtils.CreateFishTypes(typeCount);

            var matrix = ScriptableObject.CreateInstance<FishInteractionMatrix>();
            InteractionMatrixTestUtils.SetPrivateField(matrix, "fishTypes", fishTypes);
            matrix.SyncSizeWithFishTypes();

            // 0 <-> 1 Friendly
            // 0 <-> 2 Avoid
            // 1 <-> 2 Neutral
            // 2 <-> 3 Friendly
            matrix.SetSymmetricInteraction(0, 1, true);
            matrix.SetSymmetricRelation(0, 1, RelationType.Friendly);

            matrix.SetSymmetricInteraction(0, 2, true);
            matrix.SetSymmetricRelation(0, 2, RelationType.Avoid);

            matrix.SetSymmetricInteraction(1, 2, true);
            matrix.SetSymmetricRelation(1, 2, RelationType.Neutral);

            matrix.SetSymmetricInteraction(2, 3, true);
            matrix.SetSymmetricRelation(2, 3, RelationType.Friendly);

            uint[] expectedFriendly = {
            InteractionMatrixTestUtils.Mask(1),
            InteractionMatrixTestUtils.Mask(0),
            InteractionMatrixTestUtils.Mask(3),
            InteractionMatrixTestUtils.Mask(2),
        };

            uint[] expectedAvoid = {
            InteractionMatrixTestUtils.Mask(2),
            0u,
            InteractionMatrixTestUtils.Mask(0),
            0u,
        };

            uint[] expectedNeutral = {
            0u,
            InteractionMatrixTestUtils.Mask(2),
            InteractionMatrixTestUtils.Mask(1),
            0u,
        };

            // Act
            FlockInteractionCompiler.BuildInteractionData(
                fishTypes,
                matrix,
                out _,
                out _,
                out _,
                out uint[] compiledFriendly,
                out uint[] compiledAvoid,
                out uint[] compiledNeutral);

            // Assert
            InteractionMatrixTestUtils.AssertMaskArray(compiledFriendly, expectedFriendly, "compiledFriendlyMasks");
            InteractionMatrixTestUtils.AssertMaskArray(compiledAvoid, expectedAvoid, "compiledAvoidMasks");
            InteractionMatrixTestUtils.AssertMaskArray(compiledNeutral, expectedNeutral, "compiledNeutralMasks");

            // Cleanup
            Object.DestroyImmediate(matrix);
            InteractionMatrixTestUtils.DestroyFishTypes(fishTypes);
        }
    }
}
#endif
