#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    /**
     * <summary>
     * Custom inspector for <see cref="FishBehaviourProfile"/> that renders selected serialized fields
     * in a fixed order and gates preferred depth settings behind a toggle.
     * </summary>
     */
    [CustomEditor(typeof(FishBehaviourProfile))]
    public sealed class FishBehaviourProfileEditor : UnityEditor.Editor {

        /** <inheritdoc /> */
        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Movement
            DrawMovementProperties(serializedObject);

            // Noise
            DrawNoiseProperties(serializedObject);

            // Size & Schooling
            DrawSizeAndSchoolingProperties(serializedObject);

            // Neighbourhood
            DrawNeighbourhoodProperties(serializedObject);

            // Rule Weights
            DrawRuleWeightProperties(serializedObject);

            // Relationships
            DrawRelationshipProperties(serializedObject);

            // Split Behaviour
            DrawSplitBehaviourProperties(serializedObject);

            // Attraction
            DrawAttractionProperties(serializedObject);

            // Bounds
            DrawBoundsProperties(serializedObject);

            // Grouping
            DrawGroupingProperties(serializedObject);

            // Preferred Depth
            DrawPreferredDepthProperties(serializedObject);

            serializedObject.ApplyModifiedProperties();
        }

        // Draws a property only if it exists on the serialized object.
        private static void DrawPropertyIfExists(
            SerializedObject targetSerializedObject,
            string propertyName,
            bool includeChildren = false) {
            SerializedProperty serializedProperty = targetSerializedObject.FindProperty(propertyName);
            if (serializedProperty == null) {
                return;
            }

            EditorGUILayout.PropertyField(serializedProperty, includeChildren);
        }

        private static void DrawMovementProperties(SerializedObject targetSerializedObject) {
            DrawPropertyIfExists(targetSerializedObject, "maxSpeed");
            DrawPropertyIfExists(targetSerializedObject, "maxAcceleration");
            DrawPropertyIfExists(targetSerializedObject, "desiredSpeed");
        }

        private static void DrawNoiseProperties(SerializedObject targetSerializedObject) {
            // Noise (same as window)
            DrawPropertyIfExists(targetSerializedObject, "wanderStrength");
            DrawPropertyIfExists(targetSerializedObject, "wanderFrequency");
            DrawPropertyIfExists(targetSerializedObject, "groupNoiseStrength");
            DrawPropertyIfExists(targetSerializedObject, "groupNoiseDirectionRate");
            DrawPropertyIfExists(targetSerializedObject, "groupNoiseSpeedWeight");
            DrawPropertyIfExists(targetSerializedObject, "patternWeight");
        }

        private static void DrawSizeAndSchoolingProperties(SerializedObject targetSerializedObject) {
            DrawPropertyIfExists(targetSerializedObject, "bodyRadius");
            DrawPropertyIfExists(targetSerializedObject, "schoolingSpacingFactor");
            DrawPropertyIfExists(targetSerializedObject, "schoolingOuterFactor");
            DrawPropertyIfExists(targetSerializedObject, "schoolingStrength");
            DrawPropertyIfExists(targetSerializedObject, "schoolingInnerSoftness");
            DrawPropertyIfExists(targetSerializedObject, "schoolingRadialDamping");
            DrawPropertyIfExists(targetSerializedObject, "schoolingDeadzoneFraction");
        }

        private static void DrawNeighbourhoodProperties(SerializedObject targetSerializedObject) {
            DrawPropertyIfExists(targetSerializedObject, "neighbourRadius");
            DrawPropertyIfExists(targetSerializedObject, "separationRadius");

            DrawPropertyIfExists(targetSerializedObject, "maxNeighbourChecks");
            DrawPropertyIfExists(targetSerializedObject, "maxFriendlySamples");
            DrawPropertyIfExists(targetSerializedObject, "maxSeparationSamples");
        }

        private static void DrawRuleWeightProperties(SerializedObject targetSerializedObject) {
            // Rule Weights (includes Influence)
            DrawPropertyIfExists(targetSerializedObject, "alignmentWeight");
            DrawPropertyIfExists(targetSerializedObject, "cohesionWeight");
            DrawPropertyIfExists(targetSerializedObject, "separationWeight");
            DrawPropertyIfExists(targetSerializedObject, "influenceWeight");
        }

        private static void DrawRelationshipProperties(SerializedObject targetSerializedObject) {
            DrawPropertyIfExists(targetSerializedObject, "avoidanceWeight");
            DrawPropertyIfExists(targetSerializedObject, "neutralWeight");
            DrawPropertyIfExists(targetSerializedObject, "attractionResponse");
            DrawPropertyIfExists(targetSerializedObject, "avoidResponse");
        }

        private static void DrawSplitBehaviourProperties(SerializedObject targetSerializedObject) {
            DrawPropertyIfExists(targetSerializedObject, "splitPanicThreshold");
            DrawPropertyIfExists(targetSerializedObject, "splitLateralWeight");
            DrawPropertyIfExists(targetSerializedObject, "splitAccelBoost");
        }

        private static void DrawAttractionProperties(SerializedObject targetSerializedObject) {
            DrawPropertyIfExists(targetSerializedObject, "attractionWeight");
        }

        private static void DrawBoundsProperties(SerializedObject targetSerializedObject) {
            DrawPropertyIfExists(targetSerializedObject, "boundsWeight");
            DrawPropertyIfExists(targetSerializedObject, "boundsTangentialDamping");
            DrawPropertyIfExists(targetSerializedObject, "boundsInfluenceSuppression");
        }

        private static void DrawGroupingProperties(SerializedObject targetSerializedObject) {
            // Grouping (group flow + loner settings first, same as window)
            DrawPropertyIfExists(targetSerializedObject, "groupFlowWeight");
            DrawPropertyIfExists(targetSerializedObject, "minGroupSize");
            DrawPropertyIfExists(targetSerializedObject, "maxGroupSize");
            DrawPropertyIfExists(targetSerializedObject, "minGroupSizeWeight");
            DrawPropertyIfExists(targetSerializedObject, "maxGroupSizeWeight");
            DrawPropertyIfExists(targetSerializedObject, "groupRadiusMultiplier");
            DrawPropertyIfExists(targetSerializedObject, "lonerRadiusMultiplier");
            DrawPropertyIfExists(targetSerializedObject, "lonerCohesionBoost");
        }

        private static void DrawPreferredDepthProperties(SerializedObject targetSerializedObject) {
            // Preferred Depth – same gating + order as window
            SerializedProperty usePreferredDepthProperty = targetSerializedObject.FindProperty("usePreferredDepth");
            if (usePreferredDepthProperty == null) {
                return;
            }

            EditorGUILayout.PropertyField(usePreferredDepthProperty);

            using (new EditorGUI.DisabledScope(!usePreferredDepthProperty.boolValue)) {
                DrawPropertyIfExists(targetSerializedObject, "preferredDepthMin");
                DrawPropertyIfExists(targetSerializedObject, "preferredDepthMax");
                DrawPropertyIfExists(targetSerializedObject, "preferredDepthWeight");
                DrawPropertyIfExists(targetSerializedObject, "depthBiasStrength");
                DrawPropertyIfExists(targetSerializedObject, "depthWinsOverAttractor");
                DrawPropertyIfExists(targetSerializedObject, "preferredDepthEdgeFraction");
            }
        }
    }
}
#endif
