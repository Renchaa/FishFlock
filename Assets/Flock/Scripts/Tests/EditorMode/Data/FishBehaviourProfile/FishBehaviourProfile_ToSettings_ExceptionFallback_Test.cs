using Flock.Scripts.Build.Agents.Fish.Data;

using UnityEngine;
using NUnit.Framework;
using System.Reflection;

namespace Flock.Scripts.Tests.EditorMode.Data.FishBehaviourProfile
{
    public sealed class FishBehaviourProfile_ToSettings_ExceptionFallback_Test
    {
        private const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void ToSettings_ClampsAndFallsBack_ForInvalidSerializedValues()
        {
            // Arrange
            var profile = ScriptableObject.CreateInstance<Build.Agents.Fish.Profiles.FishBehaviourProfile>();

            // Movement: desiredSpeed clamped to [0..maxSpeed]
            Set(profile, "maxSpeed", 5.0f);
            Set(profile, "maxAcceleration", -1.0f);   // not clamped in this class
            Set(profile, "desiredSpeed", -1.0f);      // should clamp to 0

            // Neighbourhood + body: bodyRadius fallback to separationRadius; min floor 0.01
            Set(profile, "neighbourRadius", -1.0f);   // not clamped in this class
            Set(profile, "separationRadius", 0.0f);
            Set(profile, "bodyRadius", 0.0f);         // fallback to separationRadius (0), then floor to 0.01

            // Relationship defaults: clamp non-negative + masks zero
            Set(profile, "avoidanceWeight", -1.0f);
            Set(profile, "neutralWeight", -2.0f);
            Set(profile, "attractionWeight", -3.0f);
            Set(profile, "avoidResponse", -4.0f);

            // Group flow weight clamped non-negative
            Set(profile, "groupFlowWeight", -1.0f);

            // Grouping: min/max size clamps; multipliers clamp + loner >= group
            Set(profile, "minGroupSize", -10);
            Set(profile, "maxGroupSize", -5);
            Set(profile, "groupRadiusMultiplier", -1.0f);   // -> 0.1
            Set(profile, "lonerRadiusMultiplier", 0.05f);   // < group => should become group (0.1)
            Set(profile, "lonerCohesionBoost", -2.0f);      // -> 0

            // Preferred depth: clamp to [0..1], swap if inverted, weight disabled if usePreferredDepth=false
            Set(profile, "usePreferredDepth", false);
            Set(profile, "preferredDepthMin", 1.2f);        // clamp -> 1
            Set(profile, "preferredDepthMax", -0.2f);       // clamp -> 0, then swap => [0..1]
            Set(profile, "preferredDepthWeight", -10.0f);   // ignored because disabled
            Set(profile, "depthWinsOverAttractor", false);

            // Noise clamps
            Set(profile, "groupNoiseSpeedWeight", 2.0f);    // clamp01 -> 1

            // Schooling clamps
            Set(profile, "schoolingSpacingFactor", 0.1f);   // -> 0.5
            Set(profile, "schoolingOuterFactor", 0.2f);     // -> 1
            Set(profile, "schoolingDeadzoneFraction", 1.0f);// -> 0.5
            Set(profile, "schoolingStrength", -1.0f);       // -> 0
            Set(profile, "schoolingInnerSoftness", -1.0f);  // clamp01 -> 0
            Set(profile, "schoolingRadialDamping", -1.0f);  // -> 0

            // Bounds clamps
            Set(profile, "boundsWeight", -1.0f);              // -> 0
            Set(profile, "boundsTangentialDamping", -1.0f);   // -> 0
            Set(profile, "boundsInfluenceSuppression", -1.0f);// -> 0

            // Performance caps clamps
            Set(profile, "maxNeighbourChecks", -1);         // -> 0
            Set(profile, "maxFriendlySamples", -2);         // -> 0
            Set(profile, "maxSeparationSamples", -3);       // -> 0

            // Preferred depth edge clamp
            Set(profile, "preferredDepthEdgeFraction", 2.0f);// -> 0.5

            try
            {
                // Act
                FlockBehaviourSettings s = profile.ToSettings();

                // Assert: desiredSpeed clamp
                Assert.That(s.DesiredSpeed, Is.EqualTo(0.0f));

                // Assert: body radius fallback + floor
                Assert.That(s.BodyRadius, Is.EqualTo(0.01f));

                // Assert: non-negative relationship defaults + masks
                Assert.That(s.AvoidanceWeight, Is.EqualTo(0.0f));
                Assert.That(s.NeutralWeight, Is.EqualTo(0.0f));
                Assert.That(s.AttractionWeight, Is.EqualTo(0.0f));
                Assert.That(s.AvoidResponse, Is.EqualTo(0.0f));
                Assert.That(s.AvoidMask, Is.EqualTo(0u));
                Assert.That(s.NeutralMask, Is.EqualTo(0u));

                // Assert: group flow clamp
                Assert.That(s.GroupFlowWeight, Is.EqualTo(0.0f));

                // Assert: grouping clamps + loner >= group
                Assert.That(s.MinGroupSize, Is.EqualTo(1));
                Assert.That(s.MaxGroupSize, Is.EqualTo(0));
                Assert.That(s.GroupRadiusMultiplier, Is.EqualTo(0.1f));
                Assert.That(s.LonerRadiusMultiplier, Is.EqualTo(0.1f));
                Assert.That(s.LonerCohesionBoost, Is.EqualTo(0.0f));

                // Assert: preferred depth clamp + swap + disable gate
                Assert.That(s.UsePreferredDepth, Is.EqualTo((byte)0));
                Assert.That(s.PreferredDepthMin, Is.EqualTo(0.0f));
                Assert.That(s.PreferredDepthMax, Is.EqualTo(1.0f));
                Assert.That(s.PreferredDepthMinNorm, Is.EqualTo(0.0f));
                Assert.That(s.PreferredDepthMaxNorm, Is.EqualTo(1.0f));
                Assert.That(s.PreferredDepthWeight, Is.EqualTo(0.0f));
                Assert.That(s.DepthWinsOverAttractor, Is.EqualTo((byte)0));

                // Assert: noise + schooling clamps
                Assert.That(s.GroupNoiseSpeedWeight, Is.EqualTo(1.0f));
                Assert.That(s.SchoolingSpacingFactor, Is.EqualTo(0.5f));
                Assert.That(s.SchoolingOuterFactor, Is.EqualTo(1.0f));
                Assert.That(s.SchoolingDeadzoneFraction, Is.EqualTo(0.5f));
                Assert.That(s.SchoolingStrength, Is.EqualTo(0.0f));
                Assert.That(s.SchoolingInnerSoftness, Is.EqualTo(0.0f));
                Assert.That(s.SchoolingRadialDamping, Is.EqualTo(0.0f));

                // Assert: bounds clamps
                Assert.That(s.BoundsWeight, Is.EqualTo(0.0f));
                Assert.That(s.BoundsTangentialDamping, Is.EqualTo(0.0f));
                Assert.That(s.BoundsInfluenceSuppression, Is.EqualTo(0.0f));

                // Assert: performance caps clamps
                Assert.That(s.MaxNeighbourChecks, Is.EqualTo(0));
                Assert.That(s.MaxFriendlySamples, Is.EqualTo(0));
                Assert.That(s.MaxSeparationSamples, Is.EqualTo(0));

                // Assert: preferred depth edge clamp
                Assert.That(s.PreferredDepthEdgeFraction, Is.EqualTo(0.5f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static void Set(object target, string fieldName, object value)
        {
            FieldInfo fi = target.GetType().GetField(fieldName, BF);
            Assert.That(fi, Is.Not.Null, $"Missing field {target.GetType().Name}.{fieldName}");
            fi.SetValue(target, value);
        }
    }
}
