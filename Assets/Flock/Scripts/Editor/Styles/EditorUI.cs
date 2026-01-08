#if UNITY_EDITOR
using UnityEngine;

namespace Flock.Scripts.Editor.Window
{
    /**
     * <summary>
     * Single source of truth for IMGUI sizing/spacing/metrics across all Flock editor tooling.
     * Keep only UI-related numbers here (no business logic).
     * </summary>
     */
    public static class
        EditorUI
    {
        // Labeling
        public const float DefaultLabelWidth = 170f;

        // Spacing
        public const float SpaceTiny = 1f;
        public const float SpaceSmall = 2f;
        public const float SpaceMedium = 4f;
        public const float SpaceLarge = 8f;

        // Panels / list columns
        public const float SpeciesListPanelWidth = 280f;
        public const float NoiseListPanelWidth = 320f;

        public const float ListNameColumnWidth = 130f;

        // Toolbars
        public const float NoiseModeToolbarWidth = 240f;
        public const float InspectorModeToolbarWidth = 200f;

        // Inline object fields
        public const float SpeciesInlineObjectFieldWidth = 90f;
        public const float GroupNoiseInlineObjectFieldWidth = 170f;
        public const float PatternInlineObjectFieldWidth = 150f;

        // Common buttons
        public const float FindInSceneButtonWidth = 120f;
        public const float CreateSetupButtonWidth = 120f;

        public const float CreateMatrixAssetButtonWidth = 150f;

        public const float AddEmptySlotButtonWidth = 130f;
        public const float AddNewPresetButtonWidth = 130f;

        public const float RemoveRowButtonWidth = 20f;

        public const float CreateGroupPatternButtonWidth = 160f;
        public const float AddExistingButtonWidth = 140f;

        public const float CreatePatternButtonWidth = 160f;
        public const float AddPatternSlotButtonWidth = 140f;

        // Array drawer metrics (FlockEditorGUI)
        public const float FoldoutGutterWidth = 16f;
        public const float ArraySizeFieldWidth = 52f;
        public const float ArrayHeaderGap = 4f;
        public const float ArrayPlusMinusButtonWidth = 26f;
        public const float ArrayFooterSpace = 2f;

        // Cards / sections (FlockEditorGUI)
        public const float SectionHeaderRowHeight = 20f;
        public const float BeginSectionBodyTopSpace = 2f;
        public const float EndSectionBottomSpace = 4f;

        public const float CardTitleRowHeight = 16f;
        public const float CardTitleLeftInset = 2f;
        public const float CardAfterTitleSpace = 1f;
        public const float CardAfterCardSpace = 4f;

        // RectOffsets (padding/margins used in cached GUIStyles)
        public static readonly RectOffset ArrayElementPadding = new RectOffset(8, 8, 6, 6);
        public static readonly RectOffset ArrayElementMargin = new RectOffset(0, 0, 2, 2);

        public static readonly RectOffset SectionBoxPadding = new RectOffset(8, 8, 4, 6);
        public static readonly RectOffset SectionBoxMargin = new RectOffset(0, 0, 2, 4);

        // Advanced dropdown
        public const float CreatePatternDropdownMinWidth = 320f;
        public const float CreatePatternDropdownMinHeight = 420f;

        public const int CreatePatternTypeMapInitialCapacity = 64;
        public const int CreatePatternGroupsInitialCapacity = 8;
        public const int CreatePatternGroupListInitialCapacity = 8;

        // Editor automation timing
        public const double SceneAutoSyncIntervalSeconds = 0.2d;

        // Control IDs
        public const int GroupNoisePickerControlId = 701231;

        public static RectOffset Copy(RectOffset src) =>
            src == null ? null : new RectOffset(src.left, src.right, src.top, src.bottom);
    }
}
#endif
