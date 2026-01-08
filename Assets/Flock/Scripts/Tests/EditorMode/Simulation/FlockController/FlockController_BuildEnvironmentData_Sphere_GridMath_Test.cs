// File: Assets/Tests/EditMode/Data/Environment/FlockController_BuildEnvironmentData_Sphere_GridMath_Test.cs
using System.Reflection;
using Flock.Scripts.Build.Influence.Environment.Bounds.Data;
using Flock.Scripts.Build.Influence.Environment.Data;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
namespace Flock.Scripts.Tests.EditorMode.Simulation.FlockController {

    public sealed class FlockController_BuildEnvironmentData_Sphere_GridMath_Test {
        [Test]
        public void BuildEnvironmentData_Sphere_UsesRadiusAsExtentsAndBuildsGridFromIt() {
            GameObject go = null;

            try {
                go = new GameObject("FlockController_Test");
                var controller = go.AddComponent<Build.Core.Simulation.Runtime.PartialFlockController.FlockController>();

                SetPrivateField(controller, "boundsType", FlockBoundsType.Sphere);
                SetPrivateField(controller, "boundsCenter", Vector3.zero);
                SetPrivateField(controller, "boundsSphereRadius", 7.0f);
                SetPrivateField(controller, "cellSize", 3.0f);
                SetPrivateField(controller, "globalDamping", 0.25f);

                var env = InvokePrivateMethod<FlockEnvironmentData>(controller, "BuildEnvironmentData");

                float r = 7.0f;

                float3 expectedCenter = float3.zero;
                float3 expectedExtents = new float3(r, r, r);
                float3 expectedOrigin = expectedCenter - expectedExtents; // (-7,-7,-7)
                int3 expectedResolution = new int3(5, 5, 5);             // ceil((2r)/cellSize) = ceil(14/3)=5

                AssertFloat3Approx(env.BoundsCenter, expectedCenter);
                AssertFloat3Approx(env.BoundsExtents, expectedExtents);
                Assert.That(env.BoundsRadius, Is.EqualTo(r).Within(1e-5f));

                AssertFloat3Approx(env.GridOrigin, expectedOrigin);
                Assert.That(env.GridResolution, Is.EqualTo(expectedResolution));

                Assert.That(env.CellSize, Is.EqualTo(3.0f).Within(1e-6f));
                Assert.That(env.GlobalDamping, Is.EqualTo(0.25f).Within(1e-6f));
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