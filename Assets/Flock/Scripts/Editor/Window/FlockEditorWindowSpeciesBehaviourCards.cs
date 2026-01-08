#if UNITY_EDITOR
using Flock.Scripts.Build.Agents.Fish.Profiles;

using UnityEditor;

namespace Flock.Scripts.Editor.Window
{
    /**
    * <summary>
    * Editor window UI for configuring and inspecting flock systems.
    * This partial renders the BehaviourProfile inspector as a set of consistent cards.
    * </summary>
    */
    public sealed partial class FlockEditorWindow
    {
        private void DrawBehaviourProfileInspectorCards(FishBehaviourProfile behaviourProfile)
        {
            if (behaviourProfile == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(behaviourProfile);
            serializedObject.Update();

            FlockEditorGUI.WithLabelWidth(EditorUI.DefaultLabelWidth, () =>
            {
                DrawBehaviourMovementCard(serializedObject);
                DrawBehaviourNoiseCard(serializedObject);
                DrawBehaviourSizeAndSchoolingCard(serializedObject);
                DrawBehaviourNeighbourhoodCard(serializedObject);
                DrawBehaviourNeighbourSamplingCapsCard(serializedObject);
                DrawBehaviourRuleWeightsCard(serializedObject);
                DrawBehaviourRelationshipsCard(serializedObject);
                DrawBehaviourSplitBehaviourCard(serializedObject);
                DrawBehaviourAttractionCard(serializedObject);
                DrawBehaviourBoundsCard(serializedObject);
                DrawBehaviourGroupingCard(serializedObject);
                DrawBehaviourPreferredDepthCard(serializedObject);
            });

            if (serializedObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(behaviourProfile);
            }
        }

        private void DrawBehaviourMovementCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Movement");
            DrawPropertyNoDecorators(serializedObject.FindProperty("maxSpeed"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("maxAcceleration"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("desiredSpeed"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourNoiseCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Noise");
            DrawPropertyNoDecorators(serializedObject.FindProperty("wanderStrength"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("wanderFrequency"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("groupNoiseStrength"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("groupNoiseDirectionRate"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("groupNoiseSpeedWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("patternWeight"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourSizeAndSchoolingCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Size & Schooling");
            DrawPropertyNoDecorators(serializedObject.FindProperty("bodyRadius"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("schoolingSpacingFactor"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("schoolingOuterFactor"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("schoolingStrength"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("schoolingInnerSoftness"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("schoolingRadialDamping"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("schoolingDeadzoneFraction"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourNeighbourhoodCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Neighbourhood");
            DrawPropertyNoDecorators(serializedObject.FindProperty("neighbourRadius"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("separationRadius"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourNeighbourSamplingCapsCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Neighbour Sampling Caps");
            DrawPropertyNoDecorators(serializedObject.FindProperty("maxNeighbourChecks"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("maxFriendlySamples"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("maxSeparationSamples"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourRuleWeightsCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Rule Weights");
            DrawPropertyNoDecorators(serializedObject.FindProperty("alignmentWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("cohesionWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("separationWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("influenceWeight"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourRelationshipsCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Relationships");
            DrawPropertyNoDecorators(serializedObject.FindProperty("avoidanceWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("neutralWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("attractionResponse"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("avoidResponse"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourSplitBehaviourCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Split Behaviour");
            DrawPropertyNoDecorators(serializedObject.FindProperty("splitPanicThreshold"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("splitLateralWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("splitAccelBoost"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourAttractionCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Attraction");
            DrawPropertyNoDecorators(serializedObject.FindProperty("attractionWeight"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourBoundsCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Bounds");
            DrawPropertyNoDecorators(serializedObject.FindProperty("boundsWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("boundsTangentialDamping"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("boundsInfluenceSuppression"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourGroupingCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Grouping");
            DrawPropertyNoDecorators(serializedObject.FindProperty("groupFlowWeight"));

            DrawPropertyNoDecorators(serializedObject.FindProperty("minGroupSize"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("maxGroupSize"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("minGroupSizeWeight"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("maxGroupSizeWeight"));

            DrawPropertyNoDecorators(serializedObject.FindProperty("groupRadiusMultiplier"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("lonerRadiusMultiplier"));
            DrawPropertyNoDecorators(serializedObject.FindProperty("lonerCohesionBoost"));
            FlockEditorGUI.EndCard();
        }

        private void DrawBehaviourPreferredDepthCard(SerializedObject serializedObject)
        {
            FlockEditorGUI.BeginCard("Preferred Depth");

            SerializedProperty useDepthProperty = serializedObject.FindProperty("usePreferredDepth");
            if (useDepthProperty != null)
            {
                DrawPropertyNoDecorators(useDepthProperty);

                bool isEnabled = useDepthProperty.boolValue;

                using (new EditorGUI.DisabledScope(!isEnabled))
                {
                    DrawPropertyNoDecorators(serializedObject.FindProperty("preferredDepthMin"));
                    DrawPropertyNoDecorators(serializedObject.FindProperty("preferredDepthMax"));
                    DrawPropertyNoDecorators(serializedObject.FindProperty("preferredDepthWeight"));
                    DrawPropertyNoDecorators(serializedObject.FindProperty("depthBiasStrength"));
                    DrawPropertyNoDecorators(serializedObject.FindProperty("depthWinsOverAttractor"));
                    DrawPropertyNoDecorators(serializedObject.FindProperty("preferredDepthEdgeFraction"));
                }
            }

            FlockEditorGUI.EndCard();
        }
    }
}
#endif
