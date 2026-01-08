// File: Assets/Tests/EditMode/Data/Environment/FlockController_BuildEnvironmentData_Box_GridMath_Test.cs
using System.Reflection;
using Flock.Scripts.Build.Influence.Environment.Bounds.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockController {
    public sealed class FlockController_BuildEnvironmentData_Box_GridMath_Test {
        [Test]
        public void BuildEnvironmentData_Box_ComputesGridOriginResolutionAndRadius() {
            GameObject go = null;

            try {
                go = new GameObject("FlockController_Test");
                var controller = go.AddComponent<Build.Core.Simulation.Runtime.PartialFlockController.FlockController>();

                SetPrivateField(controller, "boundsType", FlockBoundsType.Box);
                SetPrivateField(controller, "boundsCenter", new Vector3(1f, 2f, 3f));
                SetPrivateField(controller, "boundsExtents", new Vector3(10f, 5f, 2f));
                SetPrivateField(controller, "cellSize", 2.5f);
                SetPrivateField(controller, "globalDamping", 0.7f);

                var env = InvokePrivateMethod<FlockEnvironmentData>(controller, "BuildEnvironmentData");

                float3 expectedCenter = new float3(1f, 2f, 3f);
                float3 expectedExtents = new float3(10f, 5f, 2f);

                float3 expectedOrigin = expectedCenter - expectedExtents; // (-9, -3, 1)
                int3 expectedResolution = new int3(8, 4, 2);             // ceil((2*extents)/cellSize)

                AssertFloat3Approx(env.BoundsCenter, expectedCenter);
                AssertFloat3Approx(env.BoundsExtents, expectedExtents);

                float expectedRadius = math.length(expectedExtents);
                Assert.That(env.BoundsRadius, Is.EqualTo(expectedRadius).Within(1e-5f));

                AssertFloat3Approx(env.GridOrigin, expectedOrigin);
                Assert.That(env.GridResolution, Is.EqualTo(expectedResolution));

                Assert.That(env.CellSize, Is.EqualTo(2.5f).Within(1e-6f));
                Assert.That(env.GlobalDamping, Is.EqualTo(0.7f).Within(1e-6f));
            } finally {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value) {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Private field not found: {fieldName}");
            field.SetValue(target, value);
        }

        private static T InvokePrivateMethod<T>(object target, string methodName, params object[] args) {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Private method not found: {methodName}");
            return (T)method.Invoke(target, args);
        }

        private static void AssertFloat3Approx(float3 actual, float3 expected, float eps = 1e-5f) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(eps));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(eps));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(eps));
        }
    }
}