#if UNITY_EDITOR
using Flock.Runtime;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
namespace Flock.Scripts.Tests.EditorMode.Data.InteractionMatrixCompiler {
    internal static class InteractionMatrixTestUtils {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static FishTypePreset[] CreateFishTypes(int count) {
            var arr = new FishTypePreset[count];
            for (int i = 0; i < count; i++)
                arr[i] = ScriptableObject.CreateInstance<FishTypePreset>();
            return arr;
        }

        internal static void DestroyFishTypes(FishTypePreset[] fishTypes) {
            if (fishTypes == null) return;
            for (int i = 0; i < fishTypes.Length; i++)
                if (fishTypes[i] != null)
                    Object.DestroyImmediate(fishTypes[i]);
        }

        internal static uint Mask(params int[] indices) {
            uint m = 0u;
            if (indices == null) return m;

            for (int i = 0; i < indices.Length; i++) {
                int idx = indices[i];
                Assert.That(idx, Is.InRange(0, 31), "Mask indices must be 0..31 (uint bitmask).");
                m |= (1u << idx);
            }
            return m;
        }

        internal static void AssertMaskArray(uint[] actual, uint[] expected, string label) {
            Assert.That(actual, Is.Not.Null, $"{label} is null");
            Assert.That(actual.Length, Is.EqualTo(expected.Length), $"{label} length mismatch");

            for (int i = 0; i < expected.Length; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]), $"{label}[{i}] mismatch");
        }

        internal static void SetPrivateField(object target, string fieldName, object value) {
            FieldInfo fi = target.GetType().GetField(fieldName, BF);
            Assert.That(fi, Is.Not.Null, $"Missing private field '{fieldName}' on {target.GetType().Name}.");
            fi.SetValue(target, value);
        }

        internal static void SetAsymmetricInteractionAndRelation(
            FishInteractionMatrix matrix,
            int a,
            int b,
            bool enabled,
            FishRelationType relation) {

            FieldInfo flagsFi = typeof(FishInteractionMatrix).GetField("interactionFlags", BF);
            FieldInfo relFi = typeof(FishInteractionMatrix).GetField("relationTypes", BF);

            Assert.That(flagsFi, Is.Not.Null, "FishInteractionMatrix.interactionFlags field not found.");
            Assert.That(relFi, Is.Not.Null, "FishInteractionMatrix.relationTypes field not found.");

            bool[] flags = (bool[])flagsFi.GetValue(matrix);
            FishRelationType[] rels = (FishRelationType[])relFi.GetValue(matrix);

            Assert.That(flags, Is.Not.Null);
            Assert.That(rels, Is.Not.Null);

            int count = matrix.Count;
            Assert.That(a, Is.InRange(0, count - 1));
            Assert.That(b, Is.InRange(0, count - 1));

            int idxAB = a * count + b;

            flags[idxAB] = enabled;
            rels[idxAB] = relation;

            flagsFi.SetValue(matrix, flags);
            relFi.SetValue(matrix, rels);
        }
    }
}
#endif
