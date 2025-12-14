#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FishBehaviourProfile))]
    public sealed class FishBehaviourProfileEditor : UnityEditor.Editor {

        // Basic sections
        bool movementExpanded = true;
        bool sizeSchoolingExpanded = true;
        bool neighbourhoodExpanded = true;
        bool ruleWeightsExpanded = true;
        bool influenceExpanded = true;

        // Advanced sections
        bool noiseExpanded = false;
        bool relationshipsExpanded = false;
        bool splitExpanded = false;
        bool attractionBoundsExpanded = false;
        bool groupingExpanded = false;
        bool preferredDepthExpanded = false;

        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override void OnInspectorGUI() {
            serializedObject.Update();

            // BASIC
            DrawMovementSection();
            DrawSizeAndSchoolingSection();
            DrawNeighbourhoodSection();
            DrawRuleWeightsSection();
            DrawInfluenceSection();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Advanced", EditorStyles.boldLabel);

            // ADVANCED
            DrawNoiseSection();
            DrawRelationshipsSection();
            DrawSplitSection();
            DrawAttractionAndBoundsSection();
            DrawGroupingSection();
            DrawPreferredDepthSection();

            serializedObject.ApplyModifiedProperties();
        }

        // ---------------- BASIC SECTIONS ----------------

        void DrawMovementSection() {
            if (!FlockEditorGUI.BeginSection("Movement", ref movementExpanded))
                return;

            EditorGUILayout.PropertyField(P("maxSpeed"));
            EditorGUILayout.PropertyField(P("maxAcceleration"));
            EditorGUILayout.PropertyField(P("desiredSpeed"));

            FlockEditorGUI.EndSection();
        }

        void DrawSizeAndSchoolingSection() {
            if (!FlockEditorGUI.BeginSection("Size & Schooling", ref sizeSchoolingExpanded))
                return;

            EditorGUILayout.PropertyField(P("bodyRadius"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Spacing Band", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("schoolingSpacingFactor"));
            EditorGUILayout.PropertyField(P("schoolingOuterFactor"));
            EditorGUILayout.PropertyField(P("schoolingStrength"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Smoothing", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("schoolingInnerSoftness"));
            EditorGUILayout.PropertyField(P("schoolingRadialDamping"));
            EditorGUILayout.PropertyField(P("schoolingDeadzoneFraction"));

            FlockEditorGUI.EndSection();
        }

        void DrawNeighbourhoodSection() {
            if (!FlockEditorGUI.BeginSection("Neighbourhood", ref neighbourhoodExpanded))
                return;

            EditorGUILayout.PropertyField(P("neighbourRadius"));
            EditorGUILayout.PropertyField(P("separationRadius"));

            FlockEditorGUI.EndSection();
        }

        void DrawRuleWeightsSection() {
            if (!FlockEditorGUI.BeginSection("Rule Weights", ref ruleWeightsExpanded))
                return;

            EditorGUILayout.PropertyField(P("alignmentWeight"));
            EditorGUILayout.PropertyField(P("cohesionWeight"));
            EditorGUILayout.PropertyField(P("separationWeight"));

            FlockEditorGUI.EndSection();
        }

        void DrawInfluenceSection() {
            if (!FlockEditorGUI.BeginSection("Influence", ref influenceExpanded))
                return;

            EditorGUILayout.PropertyField(P("influenceWeight"));

            FlockEditorGUI.EndSection();
        }

        // ---------------- ADVANCED SECTIONS ----------------

        void DrawNoiseSection() {
            if (!FlockEditorGUI.BeginSection("Noise & Group Flow", ref noiseExpanded))
                return;

            EditorGUILayout.LabelField("Per-fish Noise", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("wanderStrength"));
            EditorGUILayout.PropertyField(P("wanderFrequency"));
            EditorGUILayout.PropertyField(P("patternWeight"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Group Noise", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("groupNoiseStrength"));
            EditorGUILayout.PropertyField(P("groupNoiseDirectionRate"), new GUIContent("Direction Rate"));
            EditorGUILayout.PropertyField(P("groupNoiseSpeedWeight"), new GUIContent("Speed Weight"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Group Flow", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("groupFlowWeight"));

            FlockEditorGUI.EndSection();
        }

        void DrawRelationshipsSection() {
            if (!FlockEditorGUI.BeginSection("Relationships", ref relationshipsExpanded))
                return;

            EditorGUILayout.PropertyField(P("avoidanceWeight"));
            EditorGUILayout.PropertyField(P("neutralWeight"));
            EditorGUILayout.PropertyField(P("attractionResponse"));
            EditorGUILayout.PropertyField(P("avoidResponse"));

            FlockEditorGUI.EndSection();
        }

        void DrawSplitSection() {
            if (!FlockEditorGUI.BeginSection("Split Behaviour", ref splitExpanded))
                return;

            EditorGUILayout.PropertyField(P("splitPanicThreshold"));
            EditorGUILayout.PropertyField(P("splitLateralWeight"));
            EditorGUILayout.PropertyField(P("splitAccelBoost"));

            FlockEditorGUI.EndSection();
        }

        void DrawAttractionAndBoundsSection() {
            if (!FlockEditorGUI.BeginSection("Attraction & Bounds", ref attractionBoundsExpanded))
                return;

            EditorGUILayout.LabelField("Attraction", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("attractionWeight"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Bounds", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("boundsWeight"));
            EditorGUILayout.PropertyField(P("boundsTangentialDamping"));
            EditorGUILayout.PropertyField(P("boundsInfluenceSuppression"));

            FlockEditorGUI.EndSection();
        }

        void DrawGroupingSection() {
            if (!FlockEditorGUI.BeginSection("Grouping", ref groupingExpanded))
                return;

            EditorGUILayout.PropertyField(P("minGroupSize"));
            EditorGUILayout.PropertyField(P("maxGroupSize"));
            EditorGUILayout.PropertyField(P("minGroupSizeWeight"));
            EditorGUILayout.PropertyField(P("maxGroupSizeWeight"));

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Radius Multipliers", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(P("groupRadiusMultiplier"));
            EditorGUILayout.PropertyField(P("lonerRadiusMultiplier"));
            EditorGUILayout.PropertyField(P("lonerCohesionBoost"));

            FlockEditorGUI.EndSection();
        }

        void DrawPreferredDepthSection() {
            if (!FlockEditorGUI.BeginSection("Preferred Depth", ref preferredDepthExpanded))
                return;

            SerializedProperty useDepth = P("usePreferredDepth");
            EditorGUILayout.PropertyField(useDepth, new GUIContent("Use Preferred Depth"));

            using (new EditorGUI.DisabledScope(!useDepth.boolValue)) {
                EditorGUILayout.PropertyField(P("preferredDepthMin"), new GUIContent("Depth Min (norm)"));
                EditorGUILayout.PropertyField(P("preferredDepthMax"), new GUIContent("Depth Max (norm)"));
                EditorGUILayout.PropertyField(P("preferredDepthWeight"), new GUIContent("Depth Weight"));

                EditorGUILayout.Space(2f);
                EditorGUILayout.PropertyField(P("depthBiasStrength"), new GUIContent("Bias Strength"));
                EditorGUILayout.PropertyField(P("preferredDepthEdgeFraction"), new GUIContent("Edge Fraction"));
                EditorGUILayout.PropertyField(P("depthWinsOverAttractor"), new GUIContent("Depth Wins Over Attractor"));
            }

            FlockEditorGUI.EndSection();
        }
    }
}
#endif
