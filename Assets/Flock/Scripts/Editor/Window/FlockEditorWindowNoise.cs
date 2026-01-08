#if UNITY_EDITOR
using Flock.Scripts.Build.Influence.Noise.Profiles;
using Flock.Scripts.Build.Influence.PatternVolume.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Flock.Scripts.Editor.Window {
    /**
    * <summary>
    * Noise / pattern tooling UI for the flock editor window.
    * This partial contains the Noise / Patterns toolbar, list panel, detail panel, and pattern creation dropdown.
    * </summary>
    */
    public sealed partial class FlockEditorWindow {
        // Toolbar: mode switch between Group Noise and Pattern Assets.
        private void DrawNoiseModeToolbar() {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Noise / Patterns", EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();

            int selectedModeIndex = Mathf.Clamp((int)_noiseInspectorMode, 0, 1);
            int newModeIndex = GUILayout.Toolbar(
                selectedModeIndex,
                new[] { "Group Noise", "Pattern Assets" },
                GUILayout.Width(EditorUI.NoiseModeToolbarWidth));

            if (EditorGUI.EndChangeCheck()) {
                _noiseInspectorMode = (NoiseInspectorMode)newModeIndex;
                RebuildNoiseEditors();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(EditorUI.SpaceMedium);
        }


        private void DrawNoiseListPanel() {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(EditorUI.NoiseListPanelWidth))) {
                _noiseListScroll = EditorGUILayout.BeginScrollView(_noiseListScroll);

                DrawNoiseListScrollContent();

                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(EditorUI.SpaceMedium);

                DrawNoiseListFooterButtons();
            }
        }

        private void DrawNoiseListScrollContent() {
            if (_noiseInspectorMode == NoiseInspectorMode.GroupNoise) {
                EditorGUILayout.LabelField("Group Noise Pattern", EditorStyles.boldLabel);

                GroupNoisePatternProfile currentProfile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                using (new EditorGUILayout.HorizontalScope()) {
                    string displayLabel = currentProfile != null ? currentProfile.name : "<None>";
                    GUILayout.Label(displayLabel, GUILayout.Width(EditorUI.ListNameColumnWidth));

                    EditorGUI.BeginChangeCheck();
                    currentProfile = (GroupNoisePatternProfile)EditorGUILayout.ObjectField(
                        GUIContent.none,
                        currentProfile,
                        typeof(GroupNoisePatternProfile),
                        false,
                        GUILayout.Width(EditorUI.GroupNoiseInlineObjectFieldWidth));

                    if (EditorGUI.EndChangeCheck()) {
                        _setup.GroupNoiseSettings = currentProfile;
                        EditorUtility.SetDirty(_setup);
                        RebuildNoiseEditors();
                    }
                }

                return;
            }

            EditorGUILayout.LabelField("Pattern Assets", EditorStyles.boldLabel);

            if (_setup.PatternAssets == null) {
                _setup.PatternAssets = new List<PatternVolumeFlockProfile>();
                EditorUtility.SetDirty(_setup);
            }

            List<PatternVolumeFlockProfile> patterns = _setup.PatternAssets;
            int removeIndex = -1;

            for (int index = 0; index < patterns.Count; index += 1) {
                using (new EditorGUILayout.HorizontalScope()) {
                    PatternVolumeFlockProfile asset = patterns[index];

                    GUIStyle rowStyle = _selectedNoiseIndex == index
                        ? EditorStyles.miniButtonMid
                        : EditorStyles.miniButton;

                    string assetName = asset != null ? asset.name : "<Empty Slot>";
                    if (GUILayout.Button(assetName, rowStyle, GUILayout.Width(EditorUI.ListNameColumnWidth))) {
                        _selectedNoiseIndex = index;
                        RebuildNoiseEditors();
                    }

                    EditorGUI.BeginChangeCheck();
                    asset = (PatternVolumeFlockProfile)EditorGUILayout.ObjectField(
                        GUIContent.none,
                        asset,
                        typeof(PatternVolumeFlockProfile),
                        false,
                        GUILayout.Width(EditorUI.PatternInlineObjectFieldWidth));

                    if (EditorGUI.EndChangeCheck()) {
                        patterns[index] = asset;
                        EditorUtility.SetDirty(_setup);

                        if (_selectedNoiseIndex == index) {
                            RebuildNoiseEditors();
                        }
                    }

                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(EditorUI.RemoveRowButtonWidth))) {
                        removeIndex = index;
                    }
                }
            }

            if (removeIndex >= 0 && removeIndex < patterns.Count) {
                patterns.RemoveAt(removeIndex);
                EditorUtility.SetDirty(_setup);

                if (_selectedNoiseIndex == removeIndex) {
                    _selectedNoiseIndex = -1;
                    RebuildNoiseEditors();
                } else if (_selectedNoiseIndex > removeIndex) {
                    _selectedNoiseIndex -= 1;
                }
            }
        }

        private void DrawNoiseListFooterButtons() {
            using (new EditorGUILayout.HorizontalScope()) {
                if (_noiseInspectorMode == NoiseInspectorMode.GroupNoise) {
                    using (new EditorGUI.DisabledScope(_setup == null)) {
                        if (GUILayout.Button("Create Group Pattern", GUILayout.Width(EditorUI.CreateGroupPatternButtonWidth))) {
                            CreateGroupNoisePatternAsset();
                        }

                        if (GUILayout.Button("Add Existing", GUILayout.Width(EditorUI.AddExistingButtonWidth))) {
                            GroupNoisePatternProfile currentProfile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                            EditorGUIUtility.ShowObjectPicker<GroupNoisePatternProfile>(
                                currentProfile,
                                false,
                                "",
                                EditorUI.GroupNoisePickerControlId);
                        }
                    }

                    return;
                }

                using (new EditorGUI.DisabledScope(_setup == null)) {
                    if (EditorGUILayout.DropdownButton(new GUIContent("Create Pattern"), FocusType.Passive, GUILayout.Width(EditorUI.CreatePatternButtonWidth))) {
                        Rect buttonRect = GUILayoutUtility.GetLastRect();
                        ShowCreatePatternDropdown(buttonRect);
                    }
                }

                if (GUILayout.Button("Add Pattern Slot", GUILayout.Width(EditorUI.AddPatternSlotButtonWidth))) {
                    _setup.PatternAssets.Add(null);
                    _selectedNoiseIndex = _setup.PatternAssets.Count - 1;
                    EditorUtility.SetDirty(_setup);
                    RebuildNoiseEditors();
                }
            }
        }

        private void ShowCreatePatternDropdown(Rect buttonRect) {
            _createPatternDropdownState ??= new AdvancedDropdownState();

            _createPatternDropdown ??= new CreatePatternDropdown(
                _createPatternDropdownState,
                onPicked: CreatePatternAssetOfType);

            _createPatternDropdown.RefreshItems();
            _createPatternDropdown.Show(buttonRect);
        }

        private void DrawNoiseDetailPanel() {
            using (new EditorGUILayout.VerticalScope()) {
                _noiseDetailScroll = EditorGUILayout.BeginScrollView(_noiseDetailScroll);

                if (_noiseInspectorMode == 0) {
                    DrawGroupNoiseDetailPanelContent();
                } else {
                    DrawPatternAssetsDetailPanelContent();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawGroupNoiseDetailPanelContent() {
            GroupNoisePatternProfile profile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
            if (profile == null) {
                EditorGUILayout.HelpBox("Assign or create a GroupNoisePatternProfile to edit.", MessageType.Info);
                return;
            }

            DrawGroupNoiseInspectorCards(profile);
        }

        private void DrawPatternAssetsDetailPanelContent() {
            if (_setup.PatternAssets == null || _setup.PatternAssets.Count == 0) {
                EditorGUILayout.HelpBox(
                    "No pattern assets registered.\nUse 'Create Pattern' or 'Add Pattern Slot'.",
                    MessageType.Info);

                return;
            }

            if (_selectedNoiseIndex < 0 || _selectedNoiseIndex >= _setup.PatternAssets.Count) {
                EditorGUILayout.HelpBox("Select a pattern asset from the list on the left.", MessageType.Info);
                return;
            }

            PatternVolumeFlockProfile target = _setup.PatternAssets[_selectedNoiseIndex];
            if (target == null) {
                EditorGUILayout.HelpBox(
                    "This slot is empty. Assign an existing pattern asset or create a new one.",
                    MessageType.Info);

                return;
            }

            DrawPatternAssetInspectorCards(target);
        }

        private void CreateGroupNoisePatternAsset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Group Noise Pattern",
                "GroupNoisePattern",
                "asset",
                "Choose a location for the GroupNoisePatternProfile asset");

            if (string.IsNullOrEmpty(path)) {
                return;
            }

            GroupNoisePatternProfile asset = ScriptableObject.CreateInstance<GroupNoisePatternProfile>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.GroupNoiseSettings = asset;
            EditorUtility.SetDirty(_setup);

            _selectedNoiseIndex = -1;
            RebuildNoiseEditors();

            EditorGUIUtility.PingObject(asset);
        }

        private void DestroyGroupNoiseEditor() {
            DestroyEditor(ref groupNoiseEditor);
        }

        private void HandleGroupNoiseObjectPicker() {
            if (_noiseInspectorMode != NoiseInspectorMode.GroupNoise || _setup == null) {
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent == null) {
                return;
            }

            if (currentEvent.commandName != "ObjectSelectorClosed") {
                return;
            }

            if (EditorGUIUtility.GetObjectPickerControlID() != EditorUI.GroupNoisePickerControlId) {
                return;
            }

            GroupNoisePatternProfile pickedProfile = EditorGUIUtility.GetObjectPickerObject() as GroupNoisePatternProfile;
            if (pickedProfile == _setup.GroupNoiseSettings) {
                return;
            }

            _setup.GroupNoiseSettings = pickedProfile;
            EditorUtility.SetDirty(_setup);
            RebuildNoiseEditors();
        }

        private void CreatePatternAssetOfType(Type patternType) {
            if (_setup == null || patternType == null) {
                return;
            }

            string defaultName = patternType.Name;
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Pattern Asset",
                defaultName,
                "asset",
                "Choose a location for the new pattern asset");

            if (string.IsNullOrEmpty(path)) {
                return;
            }

            PatternVolumeFlockProfile asset = ScriptableObject.CreateInstance(patternType) as PatternVolumeFlockProfile;
            if (asset == null) {
                EditorUtility.DisplayDialog("Create Pattern", "Failed to create asset instance.", "OK");
                return;
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.PatternAssets ??= new List<PatternVolumeFlockProfile>();
            _setup.PatternAssets.Add(asset);

            _selectedNoiseIndex = _setup.PatternAssets.Count - 1;
            EditorUtility.SetDirty(_setup);

            RebuildNoiseEditors();
            EditorGUIUtility.PingObject(asset);
        }

        private void RebuildNoiseEditors() {
            DestroyEditor(ref groupNoiseEditor);
            DestroyEditor(ref _patternAssetEditor);

            if (_setup == null) {
                return;
            }

            if (_noiseInspectorMode == NoiseInspectorMode.GroupNoise) {
                EnsureEditor(ref groupNoiseEditor, _setup.GroupNoiseSettings as GroupNoisePatternProfile);
                return;
            }

            if (_setup.PatternAssets == null) {
                return;
            }

            PatternVolumeFlockProfile target = null;
            if (_selectedNoiseIndex >= 0 && _selectedNoiseIndex < _setup.PatternAssets.Count) {
                target = _setup.PatternAssets[_selectedNoiseIndex];
            }

            EnsureEditor(ref _patternAssetEditor, target);
        }

        private void DestroyPatternAssetEditor() {
            DestroyEditor(ref _patternAssetEditor);
        }

        private void DrawGroupNoiseInspectorCards(GroupNoisePatternProfile profile) {
            SerializedObject serializedObject = new SerializedObject(profile);
            serializedObject.Update();

            // Common / selection.
            SerializedProperty baseFrequencyProperty = serializedObject.FindProperty("baseFrequency");
            SerializedProperty timeScaleProperty = serializedObject.FindProperty("timeScale");
            SerializedProperty phaseOffsetProperty = serializedObject.FindProperty("phaseOffset");
            SerializedProperty worldScaleProperty = serializedObject.FindProperty("worldScale");
            SerializedProperty seedProperty = serializedObject.FindProperty("seed");
            SerializedProperty patternTypeProperty = serializedObject.FindProperty("patternType");

            // Pattern-specific extras.
            SerializedProperty swirlStrengthProperty = serializedObject.FindProperty("swirlStrength");
            SerializedProperty verticalBiasProperty = serializedObject.FindProperty("verticalBias");

            SerializedProperty vortexCenterProperty = serializedObject.FindProperty("vortexCenterNorm");
            SerializedProperty vortexRadiusProperty = serializedObject.FindProperty("vortexRadius");
            SerializedProperty vortexTightnessProperty = serializedObject.FindProperty("vortexTightness");

            SerializedProperty sphereRadiusProperty = serializedObject.FindProperty("sphereRadius");
            SerializedProperty sphereThicknessProperty = serializedObject.FindProperty("sphereThickness");
            SerializedProperty sphereSwirlStrengthProperty = serializedObject.FindProperty("sphereSwirlStrength");
            SerializedProperty sphereCenterProperty = serializedObject.FindProperty("sphereCenterNorm");

            FlockEditorGUI.WithLabelWidth(EditorUI.DefaultLabelWidth, () => {
                FlockEditorGUI.BeginCard("Common");
                {
                    DrawPropertyNoDecorators(baseFrequencyProperty);
                    DrawPropertyNoDecorators(timeScaleProperty);
                    DrawPropertyNoDecorators(phaseOffsetProperty);
                    DrawPropertyNoDecorators(worldScaleProperty);
                    DrawPropertyNoDecorators(seedProperty);
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Pattern Type");
                {
                    DrawPropertyNoDecorators(patternTypeProperty);
                }
                FlockEditorGUI.EndCard();

                FlockGroupNoisePatternType patternType =
                    (FlockGroupNoisePatternType)(patternTypeProperty != null ? patternTypeProperty.enumValueIndex : 0);

                switch (patternType) {
                    case FlockGroupNoisePatternType.SimpleSine:
                        FlockEditorGUI.BeginCard("Simple Sine Extras"); {
                            DrawPropertyNoDecorators(swirlStrengthProperty);
                            DrawPropertyNoDecorators(verticalBiasProperty);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.VerticalBands:
                        FlockEditorGUI.BeginCard("Vertical Bands Extras"); {
                            DrawPropertyNoDecorators(swirlStrengthProperty);
                            DrawPropertyNoDecorators(verticalBiasProperty);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.Vortex:
                        FlockEditorGUI.BeginCard("Vortex Settings"); {
                            DrawPropertyNoDecorators(vortexCenterProperty);
                            DrawPropertyNoDecorators(vortexRadiusProperty);
                            DrawPropertyNoDecorators(vortexTightnessProperty);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.SphereShell:
                        FlockEditorGUI.BeginCard("Sphere Shell Settings"); {
                            DrawPropertyNoDecorators(sphereRadiusProperty);
                            DrawPropertyNoDecorators(sphereThicknessProperty);
                            DrawPropertyNoDecorators(sphereSwirlStrengthProperty);
                            DrawPropertyNoDecorators(sphereCenterProperty);
                        }
                        FlockEditorGUI.EndCard();
                        break;
                }
            });

            if (serializedObject.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(profile);
            }
        }

        private void DrawPatternAssetInspectorCards(PatternVolumeFlockProfile target) {
            if (target == null) {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(target);
            serializedObject.Update();

            Type rootType = target.GetType();

            string currentSection = null;
            bool sectionOpen = false;

            FlockEditorGUI.WithLabelWidth(EditorUI.DefaultLabelWidth, () => {
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;

                while (iterator.NextVisible(enterChildren)) {
                    enterChildren = false;

                    if (iterator.depth != 0) {
                        continue;
                    }

                    if (iterator.propertyPath == "m_Script") {
                        continue;
                    }

                    if (TryGetHeaderForPropertyPath(rootType, iterator.propertyPath, out string header)) {
                        if (!sectionOpen || !string.Equals(currentSection, header, StringComparison.Ordinal)) {
                            if (sectionOpen) {
                                FlockEditorGUI.EndCard();
                            }

                            currentSection = header;
                            FlockEditorGUI.BeginCard(currentSection);
                            sectionOpen = true;
                        }
                    } else if (!sectionOpen) {
                        currentSection = "Settings";
                        FlockEditorGUI.BeginCard(currentSection);
                        sectionOpen = true;
                    }

                    SerializedProperty property = iterator.Copy();

                    GUIContent labelOverride =
                        !string.IsNullOrEmpty(currentSection) &&
                        string.Equals(property.displayName, currentSection, StringComparison.Ordinal)
                            ? GUIContent.none
                            : null;

                    DrawPropertyNoDecorators(property, labelOverride);
                }

                if (sectionOpen) {
                    FlockEditorGUI.EndCard();
                }
            });

            if (serializedObject.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(target);
            }
        }

        private sealed class CreatePatternDropdown : AdvancedDropdown {
            private readonly Action<Type> onPickedCallback;

            private readonly Dictionary<int, Type> identifierToType =
                new Dictionary<int, Type>(EditorUI.CreatePatternTypeMapInitialCapacity);

            private Type[] patternTypes = Array.Empty<Type>();

            private int nextIdentifier = 1;

            public CreatePatternDropdown(AdvancedDropdownState state, Action<Type> onPicked)
                : base(state) {
                onPickedCallback = onPicked;

                minimumSize = new Vector2(
                    EditorUI.CreatePatternDropdownMinWidth,
                    EditorUI.CreatePatternDropdownMinHeight);
            }

            public void RefreshItems() {
                patternTypes = TypeCache.GetTypesDerivedFrom<PatternVolumeFlockProfile>().ToArray();
            }

            protected override AdvancedDropdownItem BuildRoot() {
                identifierToType.Clear();
                nextIdentifier = 1;

                AdvancedDropdownItem rootItem = new AdvancedDropdownItem("Create Layer-3 Pattern");
                Texture2D iconTexture = EditorGUIUtility.IconContent("ScriptableObject Icon")?.image as Texture2D;

                Dictionary<string, List<Type>> groups =
                    new Dictionary<string, List<Type>>(EditorUI.CreatePatternGroupsInitialCapacity);

                for (int index = 0; index < patternTypes.Length; index += 1) {
                    Type type = patternTypes[index];
                    if (type == null || type.IsAbstract) {
                        continue;
                    }

                    string groupName = GetGroupName(type);

                    if (!groups.TryGetValue(groupName, out List<Type> groupTypes)) {
                        groupTypes = new List<Type>(EditorUI.CreatePatternGroupListInitialCapacity);
                        groups.Add(groupName, groupTypes);
                    }

                    groupTypes.Add(type);
                }

                List<string> groupNames = new List<string>(groups.Keys);
                groupNames.Sort(StringComparer.OrdinalIgnoreCase);

                bool anyAdded = false;

                foreach (string groupName in groupNames) {
                    AdvancedDropdownItem groupItem = new AdvancedDropdownItem(groupName);
                    rootItem.AddChild(groupItem);

                    List<Type> groupTypes = groups[groupName];
                    groupTypes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(GetPrettyName(a), GetPrettyName(b)));

                    for (int index = 0; index < groupTypes.Count; index += 1) {
                        Type type = groupTypes[index];

                        int identifier = nextIdentifier;
                        nextIdentifier += 1;

                        identifierToType[identifier] = type;

                        AdvancedDropdownItem item = new AdvancedDropdownItem(GetPrettyName(type)) {
                            id = identifier,
                            icon = iconTexture
                        };

                        groupItem.AddChild(item);
                        anyAdded = true;
                    }
                }

                if (!anyAdded) {
                    rootItem.AddChild(new AdvancedDropdownItem("No concrete pattern profile types found"));
                }

                return rootItem;
            }

            protected override void ItemSelected(AdvancedDropdownItem item) {
                if (item == null) {
                    return;
                }

                if (identifierToType.TryGetValue(item.id, out Type type)) {
                    onPickedCallback?.Invoke(type);
                }
            }

            private static string GetGroupName(Type type) {
                string namespaceName = type.Namespace ?? string.Empty;
                if (string.IsNullOrEmpty(namespaceName)) {
                    return "Patterns";
                }

                int lastDotIndex = namespaceName.LastIndexOf('.');
                return lastDotIndex >= 0 && lastDotIndex < namespaceName.Length - 1
                    ? namespaceName.Substring(lastDotIndex + 1)
                    : namespaceName;
            }

            private static string GetPrettyName(Type type) {
                string name = type.Name;

                name = name.Replace("PatternProfile", "");
                name = name.Replace("Profile", "");
                name = name.Replace("Flock", "");
                name = name.Replace("Layer3", "");

                name = name.Trim();
                if (string.IsNullOrEmpty(name)) {
                    name = type.Name;
                }

                StringBuilder stringBuilder = new StringBuilder(name.Length + 8);

                for (int index = 0; index < name.Length; index += 1) {
                    char character = name[index];

                    if (index > 0
                        && char.IsUpper(character)
                        && char.IsLetterOrDigit(name[index - 1])
                        && !char.IsUpper(name[index - 1])) {
                        stringBuilder.Append(' ');
                    }

                    stringBuilder.Append(character);
                }

                return stringBuilder.ToString().Trim();
            }
        }
    }
}
#endif
