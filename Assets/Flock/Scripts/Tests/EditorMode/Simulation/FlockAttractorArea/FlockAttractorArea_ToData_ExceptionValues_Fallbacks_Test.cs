using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
using Unity.Mathematics;

using UnityEngine;
using NUnit.Framework;
using System.Reflection;

namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockAttractorArea {
    public sealed class FlockAttractorArea_ToData_ExceptionValues_Fallbacks_Test {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ToData_ClampsExceptionValues_AndAppliesShapeRules() {
            // Arrange
            var go = new GameObject("FlockAttractorArea_TestGO");
            go.transform.position = new Vector3(1f, 2f, 3f);
            go.transform.rotation = Quaternion.Euler(10f, 20f, 30f);

            var area = go.AddComponent<Build.Influence.Environment.Attractors.Runtime.FlockAttractorArea > ();

            // Exception values (inputs that must clamp / fallback)
            SetPrivateField(area, "baseStrength", -1.0f);    // => 0
            SetPrivateField(area, "falloffPower", 0.0f);     // => 0.1
            SetPrivateField(area, "cellPriority", -5.0f);    // => 0

            // Pick a usage value (kept 1:1)
            // If your enum differs, change this to a valid member.
            SetPrivateField(area, "usage", AttractorUsage.Individual);

            try {
                // -------- Sphere branch --------
                SetPrivateField(area, "shape", FlockAttractorShape.Sphere);
                SetPrivateField(area, "sphereRadius", -3.0f); // => 0

                FlockAttractorData sphere = area.ToData(affectedTypesMask: 0u); // 0 => uint.MaxValue

                // Base data clamps / fallbacks
                Assert.That(sphere.Position.x, Is.EqualTo(1f));
                Assert.That(sphere.Position.y, Is.EqualTo(2f));
                Assert.That(sphere.Position.z, Is.EqualTo(3f));

                Assert.That(sphere.BaseStrength, Is.EqualTo(0f));
                Assert.That(sphere.FalloffPower, Is.EqualTo(0.1f));
                Assert.That(sphere.CellPriority, Is.EqualTo(0f));
                Assert.That(sphere.AffectedTypesMask, Is.EqualTo(uint.MaxValue));
                Assert.That(sphere.Usage, Is.EqualTo(AttractorUsage.Individual));
                Assert.That(sphere.Shape, Is.EqualTo(FlockAttractorShape.Sphere));

                // Sphere shape rules
                Assert.That(sphere.Radius, Is.EqualTo(0f));
                AssertFloat3Equals(sphere.BoxHalfExtents, float3.zero, "Sphere.BoxHalfExtents");
                AssertQuaternionEquals(sphere.BoxRotation, quaternion.identity, "Sphere.BoxRotation");

                // -------- Box branch --------
                SetPrivateField(area, "shape", FlockAttractorShape.Box);

                // boxSize has negative components => halfExtents clamp to 0 on those axes
                SetPrivateField(area, "boxSize", new Vector3(-6f, -4f, 6f)); // halfExtents => (0,0,3), radius => length => 3

                const uint customMask = 0b1010u;
                FlockAttractorData box = area.ToData(affectedTypesMask: customMask); // non-zero => must remain unchanged

                // Base data still clamped
                Assert.That(box.BaseStrength, Is.EqualTo(0f));
                Assert.That(box.FalloffPower, Is.EqualTo(0.1f));
                Assert.That(box.CellPriority, Is.EqualTo(0f));
                Assert.That(box.AffectedTypesMask, Is.EqualTo(customMask));
                Assert.That(box.Usage, Is.EqualTo(AttractorUsage.Individual));
                Assert.That(box.Shape, Is.EqualTo(FlockAttractorShape.Box));

                // Box shape rules
                AssertFloat3Equals(box.BoxHalfExtents, new float3(0f, 0f, 3f), "Box.BoxHalfExtents");
                Assert.That(box.Radius, Is.EqualTo(3f), "Box.Radius must equal length(BoxHalfExtents)");

                quaternion expectedRot = (quaternion)go.transform.rotation;
                AssertQuaternionEquals(box.BoxRotation, expectedRot, "Box.BoxRotation");
            } finally {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------- helpers ----------------

        private static void SetPrivateField(object target, string fieldName, object value) {
            FieldInfo fi = target.GetType().GetField(fieldName, BF);
            Assert.That(fi, Is.Not.Null, $"Missing field {target.GetType().Name}.{fieldName}");
            fi.SetValue(target, value);
        }

        private static void AssertFloat3Equals(float3 actual, float3 expected, string label) {
            Assert.That(actual.x, Is.EqualTo(expected.x), $"{label}.x mismatch");
            Assert.That(actual.y, Is.EqualTo(expected.y), $"{label}.y mismatch");
            Assert.That(actual.z, Is.EqualTo(expected.z), $"{label}.z mismatch");
        }

        private static void AssertQuaternionEquals(quaternion actual, quaternion expected, string label) {
            Assert.That(actual.value.x, Is.EqualTo(expected.value.x), $"{label}.x mismatch");
            Assert.That(actual.value.y, Is.EqualTo(expected.value.y), $"{label}.y mismatch");
            Assert.That(actual.value.z, Is.EqualTo(expected.value.z), $"{label}.z mismatch");
            Assert.That(actual.value.w, Is.EqualTo(expected.value.w), $"{label}.w mismatch");
        }
    }
}
