#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using NUnit.Framework;
using UnityEngine;
namespace Flock.Scripts.Tests.EditorMode.Data.InteractionMatrixCompiler {
    public sealed class InteractionMatrixCompiler_EnforcesSymmetryTests {
        [Test]
        public void BuildInteractionData_EnforcesSymmetry_WhenMatrixDataIsAsymmetric() {
            // Arrange
            const int typeCount = 3;
            var fishTypes = InteractionMatrixTestUtils.CreateFishTypes(typeCount);

            var matrix = ScriptableObject.CreateInstance<FishInteractionMatrix>();
            InteractionMatrixTestUtils.SetPrivateField(matrix, "fishTypes", fishTypes);
            matrix.SyncSizeWithFishTypes();

            // Force asymmetric raw data: enable (0,1) Friendly, but do NOT set (1,0)
            InteractionMatrixTestUtils.SetAsymmetricInteractionAndRelation(
                matrix,
                a: 0,
                b: 1,
                enabled: true,
                relation: FishRelationType.Friendly);

            // Act
            Runtime.Data.FlockInteractionCompiler.BuildInteractionData(
                fishTypes,
                matrix,
                out _,
                out _,
                out _,
                out uint[] compiledFriendly,
                out uint[] compiledAvoid,
                out uint[] compiledNeutral);

            // Assert (compiler symmetrizes masks)
            uint[] expectedFriendly = {
            InteractionMatrixTestUtils.Mask(1),
            InteractionMatrixTestUtils.Mask(0),
            0u
        };

            uint[] expectedAvoid = { 0u, 0u, 0u };
            uint[] expectedNeutral = { 0u, 0u, 0u };

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
