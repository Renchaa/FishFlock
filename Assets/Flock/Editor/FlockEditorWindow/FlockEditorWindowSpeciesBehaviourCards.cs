#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        void DrawBehaviourProfileInspectorCards(FishBehaviourProfile target) {
            if (target == null) return;

            var so = new SerializedObject(target);
            so.Update();

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {

                FlockEditorGUI.BeginCard("Movement");
                {
                    DrawPropertyNoDecorators(so.FindProperty("maxSpeed"));
                    DrawPropertyNoDecorators(so.FindProperty("maxAcceleration"));
                    DrawPropertyNoDecorators(so.FindProperty("desiredSpeed"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Noise");
                {
                    DrawPropertyNoDecorators(so.FindProperty("wanderStrength"));
                    DrawPropertyNoDecorators(so.FindProperty("wanderFrequency"));
                    DrawPropertyNoDecorators(so.FindProperty("groupNoiseStrength"));
                    DrawPropertyNoDecorators(so.FindProperty("groupNoiseDirectionRate"));
                    DrawPropertyNoDecorators(so.FindProperty("groupNoiseSpeedWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("patternWeight"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Size & Schooling");
                {
                    DrawPropertyNoDecorators(so.FindProperty("bodyRadius"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingSpacingFactor"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingOuterFactor"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingStrength"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingInnerSoftness"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingRadialDamping"));
                    DrawPropertyNoDecorators(so.FindProperty("schoolingDeadzoneFraction"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Neighbourhood");
                {
                    DrawPropertyNoDecorators(so.FindProperty("neighbourRadius"));
                    DrawPropertyNoDecorators(so.FindProperty("separationRadius"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Neighbour Sampling Caps");
                {
                    DrawPropertyNoDecorators(so.FindProperty("maxNeighbourChecks"));
                    DrawPropertyNoDecorators(so.FindProperty("maxFriendlySamples"));
                    DrawPropertyNoDecorators(so.FindProperty("maxSeparationSamples"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Rule Weights");
                {
                    DrawPropertyNoDecorators(so.FindProperty("alignmentWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("cohesionWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("separationWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("influenceWeight"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Relationships");
                {
                    DrawPropertyNoDecorators(so.FindProperty("avoidanceWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("neutralWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("attractionResponse"));
                    DrawPropertyNoDecorators(so.FindProperty("avoidResponse"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Split Behaviour");
                {
                    DrawPropertyNoDecorators(so.FindProperty("splitPanicThreshold"));
                    DrawPropertyNoDecorators(so.FindProperty("splitLateralWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("splitAccelBoost"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Attraction");
                {
                    DrawPropertyNoDecorators(so.FindProperty("attractionWeight"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Bounds");
                {
                    DrawPropertyNoDecorators(so.FindProperty("boundsWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("boundsTangentialDamping"));
                    DrawPropertyNoDecorators(so.FindProperty("boundsInfluenceSuppression"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Grouping");
                {
                    DrawPropertyNoDecorators(so.FindProperty("groupFlowWeight"));

                    DrawPropertyNoDecorators(so.FindProperty("minGroupSize"));
                    DrawPropertyNoDecorators(so.FindProperty("maxGroupSize"));
                    DrawPropertyNoDecorators(so.FindProperty("minGroupSizeWeight"));
                    DrawPropertyNoDecorators(so.FindProperty("maxGroupSizeWeight"));

                    DrawPropertyNoDecorators(so.FindProperty("groupRadiusMultiplier"));
                    DrawPropertyNoDecorators(so.FindProperty("lonerRadiusMultiplier"));
                    DrawPropertyNoDecorators(so.FindProperty("lonerCohesionBoost"));
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Preferred Depth");
                {
                    var useDepth = so.FindProperty("usePreferredDepth");
                    if (useDepth != null) {
                        DrawPropertyNoDecorators(useDepth);
                        bool enabled = useDepth.boolValue;

                        using (new EditorGUI.DisabledScope(!enabled)) {
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthMin"));
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthMax"));
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthWeight"));
                            DrawPropertyNoDecorators(so.FindProperty("depthBiasStrength"));
                            DrawPropertyNoDecorators(so.FindProperty("depthWinsOverAttractor"));
                            DrawPropertyNoDecorators(so.FindProperty("preferredDepthEdgeFraction"));
                        }
                    }
                }
                FlockEditorGUI.EndCard();
            });

            if (so.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif
