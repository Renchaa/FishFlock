#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FishBehaviourProfile))]
    public sealed class FishBehaviourProfileEditor : UnityEditor.Editor {

        static void DrawIfExists(SerializedObject so, string name, bool includeChildren = false) {
            var p = so.FindProperty(name);
            if (p != null) {
                EditorGUILayout.PropertyField(p, includeChildren);
            }
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // Movement
            DrawIfExists(serializedObject, "maxSpeed");
            DrawIfExists(serializedObject, "maxAcceleration");
            DrawIfExists(serializedObject, "desiredSpeed");

            // Noise  (same as window)
            DrawIfExists(serializedObject, "wanderStrength");
            DrawIfExists(serializedObject, "wanderFrequency");
            DrawIfExists(serializedObject, "groupNoiseStrength");
            DrawIfExists(serializedObject, "groupNoiseDirectionRate");
            DrawIfExists(serializedObject, "groupNoiseSpeedWeight");
            DrawIfExists(serializedObject, "patternWeight");

            // Size & Schooling
            DrawIfExists(serializedObject, "bodyRadius");
            DrawIfExists(serializedObject, "schoolingSpacingFactor");
            DrawIfExists(serializedObject, "schoolingOuterFactor");
            DrawIfExists(serializedObject, "schoolingStrength");
            DrawIfExists(serializedObject, "schoolingInnerSoftness");
            DrawIfExists(serializedObject, "schoolingRadialDamping");
            DrawIfExists(serializedObject, "schoolingDeadzoneFraction");

            // Neighbourhood
            DrawIfExists(serializedObject, "neighbourRadius");
            DrawIfExists(serializedObject, "separationRadius");

            // Rule Weights (includes Influence)
            DrawIfExists(serializedObject, "alignmentWeight");
            DrawIfExists(serializedObject, "cohesionWeight");
            DrawIfExists(serializedObject, "separationWeight");
            DrawIfExists(serializedObject, "influenceWeight");

            // Relationships
            DrawIfExists(serializedObject, "avoidanceWeight");
            DrawIfExists(serializedObject, "neutralWeight");
            DrawIfExists(serializedObject, "attractionResponse");
            DrawIfExists(serializedObject, "avoidResponse");

            // Split Behaviour
            DrawIfExists(serializedObject, "splitPanicThreshold");
            DrawIfExists(serializedObject, "splitLateralWeight");
            DrawIfExists(serializedObject, "splitAccelBoost");

            // Attraction
            DrawIfExists(serializedObject, "attractionWeight");

            // Bounds
            DrawIfExists(serializedObject, "boundsWeight");
            DrawIfExists(serializedObject, "boundsTangentialDamping");
            DrawIfExists(serializedObject, "boundsInfluenceSuppression");

            // Grouping (group flow + loner settings first, same as window)
            DrawIfExists(serializedObject, "groupFlowWeight");
            DrawIfExists(serializedObject, "minGroupSize");
            DrawIfExists(serializedObject, "maxGroupSize");
            DrawIfExists(serializedObject, "minGroupSizeWeight");
            DrawIfExists(serializedObject, "maxGroupSizeWeight");
            DrawIfExists(serializedObject, "groupRadiusMultiplier");
            DrawIfExists(serializedObject, "lonerRadiusMultiplier");
            DrawIfExists(serializedObject, "lonerCohesionBoost");

            // Preferred Depth – same gating + order as window
            var useDepth = serializedObject.FindProperty("usePreferredDepth");
            if (useDepth != null) {
                EditorGUILayout.PropertyField(useDepth);

                using (new EditorGUI.DisabledScope(!useDepth.boolValue)) {
                    DrawIfExists(serializedObject, "preferredDepthMin");
                    DrawIfExists(serializedObject, "preferredDepthMax");
                    DrawIfExists(serializedObject, "preferredDepthWeight");
                    DrawIfExists(serializedObject, "depthBiasStrength");
                    DrawIfExists(serializedObject, "depthWinsOverAttractor");
                    DrawIfExists(serializedObject, "preferredDepthEdgeFraction");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

    }
}
#endif
