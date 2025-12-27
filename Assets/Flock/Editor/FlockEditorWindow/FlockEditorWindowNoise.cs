#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Flock.Editor {
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
            _noiseInspectorMode = GUILayout.Toolbar(
                Mathf.Clamp(_noiseInspectorMode, 0, 1),
                new[] { "Group Noise", "Pattern Assets" },
                GUILayout.Width(FlockEditorUI.NoiseModeToolbarWidth));

            if (EditorGUI.EndChangeCheck()) {
                RebuildNoiseEditors();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(FlockEditorUI.SpaceMedium);
        }

        private void DrawNoiseListPanel() {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(FlockEditorUI.NoiseListPanelWidth))) {
                _noiseListScroll = EditorGUILayout.BeginScrollView(_noiseListScroll);

                if (_noiseInspectorMode == 0) {
                    EditorGUILayout.LabelField("Group Noise Pattern", EditorStyles.boldLabel);

                    GroupNoisePatternProfile currentProfile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                    using (new EditorGUILayout.HorizontalScope()) {
                        string label = currentProfile != null ? currentProfile.name : "<None>";
                        GUILayout.Label(label, GUILayout.Width(FlockEditorUI.ListNameColumnWidth));

                        EditorGUI.BeginChangeCheck();
                        currentProfile = (GroupNoisePatternProfile)EditorGUILayout.ObjectField(
                            GUIContent.none,
                            currentProfile,
                            typeof(GroupNoisePatternProfile),
                            false,
                            GUILayout.Width(FlockEditorUI.GroupNoiseInlineObjectFieldWidth));

                        if (EditorGUI.EndChangeCheck()) {
                            _setup.GroupNoiseSettings = currentProfile;
                            EditorUtility.SetDirty(_setup);
                            RebuildNoiseEditors();
                        }
                    }
                } else {
                    EditorGUILayout.LabelField("Pattern Assets", EditorStyles.boldLabel);

                    if (_setup.PatternAssets == null) {
                        _setup.PatternAssets = new List<FlockLayer3PatternProfile>();
                        EditorUtility.SetDirty(_setup);
                    }

                    List<FlockLayer3PatternProfile> patterns = _setup.PatternAssets;
                    int removeIndex = -1;

                    for (int index = 0; index < patterns.Count; index += 1) {
                        using (new EditorGUILayout.HorizontalScope()) {
                            FlockLayer3PatternProfile asset = patterns[index];

                            GUIStyle rowStyle = _selectedNoiseIndex == index
                                ? EditorStyles.miniButtonMid
                                : EditorStyles.miniButton;

                            string assetName = asset != null ? asset.name : "<Empty Slot>";
                            if (GUILayout.Button(assetName, rowStyle, GUILayout.Width(FlockEditorUI.ListNameColumnWidth))) {
                                _selectedNoiseIndex = index;
                                RebuildNoiseEditors();
                            }

                            EditorGUI.BeginChangeCheck();
                            asset = (FlockLayer3PatternProfile)EditorGUILayout.ObjectField(
                                GUIContent.none,
                                asset,
                                typeof(FlockLayer3PatternProfile),
                                false,
                                GUILayout.Width(FlockEditorUI.PatternInlineObjectFieldWidth));

                            if (EditorGUI.EndChangeCheck()) {
                                patterns[index] = asset;
                                EditorUtility.SetDirty(_setup);

                                if (_selectedNoiseIndex == index) {
                                    RebuildNoiseEditors();
                                }
                            }

                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(FlockEditorUI.RemoveRowButtonWidth))) {
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

                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(FlockEditorUI.SpaceMedium);

                using (new EditorGUILayout.HorizontalScope()) {
                    if (_noiseInspectorMode == 0) {
                        using (new EditorGUI.DisabledScope(_setup == null)) {
                            if (GUILayout.Button("Create Group Pattern", GUILayout.Width(FlockEditorUI.CreateGroupPatternButtonWidth))) {
                                CreateGroupNoisePatternAsset();
                            }

                            if (GUILayout.Button("Add Existing", GUILayout.Width(FlockEditorUI.AddExistingButtonWidth))) {
                                GroupNoisePatternProfile current = _setup.GroupNoiseSettings as GroupNoisePatternProfile;

                                EditorGUIUtility.ShowObjectPicker<GroupNoisePatternProfile>(
                                    current,
                                    false,
                                    "",
                                    FlockEditorUI.GroupNoisePickerControlId);
                            }
                        }
                    } else {
                        using (new EditorGUI.DisabledScope(_setup == null)) {
                            if (EditorGUILayout.DropdownButton(new GUIContent("Create Pattern"), FocusType.Passive, GUILayout.Width(FlockEditorUI.CreatePatternButtonWidth))) {
                                Rect buttonRect = GUILayoutUtility.GetLastRect();
                                ShowCreatePatternDropdown(buttonRect);
                            }
                        }

                        if (GUILayout.Button("Add Pattern Slot", GUILayout.Width(FlockEditorUI.AddPatternSlotButtonWidth))) {
                            _setup.PatternAssets.Add(null);
                            _selectedNoiseIndex = _setup.PatternAssets.Count - 1;
                            EditorUtility.SetDirty(_setup);
                            RebuildNoiseEditors();
                        }
                    }
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
                    GroupNoisePatternProfile profile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
                    if (profile == null) {
                        EditorGUILayout.HelpBox(
                            "Assign or create a GroupNoisePatternProfile to edit.",
                            MessageType.Info);

                        EditorGUILayout.EndScrollView();
                        return;
                    }

                    DrawGroupNoiseInspectorCards(profile);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                if (_setup.PatternAssets == null || _setup.PatternAssets.Count == 0) {
                    EditorGUILayout.HelpBox(
                        "No pattern assets registered.\nUse 'Create Pattern' or 'Add Pattern Slot'.",
                        MessageType.Info);

                    EditorGUILayout.EndScrollView();
                    return;
                }

                if (_selectedNoiseIndex < 0 || _selectedNoiseIndex >= _setup.PatternAssets.Count) {
                    EditorGUILayout.HelpBox(
                        "Select a pattern asset from the list on the left.",
                        MessageType.Info);

                    EditorGUILayout.EndScrollView();
                    return;
                }

                FlockLayer3PatternProfile target = _setup.PatternAssets[_selectedNoiseIndex];
                if (target == null) {
                    EditorGUILayout.HelpBox(
                        "This slot is empty. Assign an existing pattern asset or create a new one.",
                        MessageType.Info);

                    EditorGUILayout.EndScrollView();
                    return;
                }

                DrawPatternAssetInspectorCards(target);

                EditorGUILayout.EndScrollView();
            }
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

        private void RebuildGroupNoiseEditor() {
            if (_setup == null) {
                DestroyEditor(ref groupNoiseEditor);
                return;
            }

            EnsureEditor(ref groupNoiseEditor, _setup.GroupNoiseSettings as GroupNoisePatternProfile);
        }

        private void DestroyGroupNoiseEditor() {
            DestroyEditor(ref groupNoiseEditor);
        }

        private void HandleGroupNoiseObjectPicker() {
            if (_noiseInspectorMode != 0 || _setup == null) {
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent == null) {
                return;
            }

            if (currentEvent.commandName != "ObjectSelectorClosed") {
                return;
            }

            if (EditorGUIUtility.GetObjectPickerControlID() != FlockEditorUI.GroupNoisePickerControlId) {
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

            FlockLayer3PatternProfile asset = ScriptableObject.CreateInstance(patternType) as FlockLayer3PatternProfile;
            if (asset == null) {
                EditorUtility.DisplayDialog("Create Pattern", "Failed to create asset instance.", "OK");
                return;
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.PatternAssets ??= new List<FlockLayer3PatternProfile>();
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

            if (_noiseInspectorMode == 0) {
                EnsureEditor(ref groupNoiseEditor, _setup.GroupNoiseSettings as GroupNoisePatternProfile);
                return;
            }

            if (_setup.PatternAssets == null) {
                return;
            }

            FlockLayer3PatternProfile target = null;
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

            // Extras.
            SerializedProperty swirlStrengthProperty = serializedObject.FindProperty("swirlStrength");
            SerializedProperty verticalBiasProperty = serializedObject.FindProperty("verticalBias");

            SerializedProperty vortexCenterProperty = serializedObject.FindProperty("vortexCenterNorm");
            SerializedProperty vortexRadiusProperty = serializedObject.FindProperty("vortexRadius");
            SerializedProperty vortexTightnessProperty = serializedObject.FindProperty("vortexTightness");

            SerializedProperty sphereRadiusProperty = serializedObject.FindProperty("sphereRadius");
            SerializedProperty sphereThicknessProperty = serializedObject.FindProperty("sphereThickness");
            SerializedProperty sphereSwirlStrengthProperty = serializedObject.FindProperty("sphereSwirlStrength");
            SerializedProperty sphereCenterProperty = serializedObject.FindProperty("sphereCenterNorm");

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {
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

                FlockGroupNoisePatternType patternType = (FlockGroupNoisePatternType)(patternTypeProperty != null ? patternTypeProperty.enumValueIndex : 0);

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

        private void DrawPatternAssetInspectorCards(FlockLayer3PatternProfile target) {
            if (target == null) {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(target);
            serializedObject.Update();

            Type rootType = target.GetType();
            string currentSection = null;
            bool sectionOpen = false;

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {
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
            private readonly Action<Type> _onPicked;

            private readonly Dictionary<int, Type> _idToType =
                new Dictionary<int, Type>(FlockEditorUI.CreatePatternTypeMapInitialCapacity);

            private Type[] _types = Array.Empty<Type>();

            private int _nextId = 1;

            public CreatePatternDropdown(AdvancedDropdownState state, Action<Type> onPicked)
                : base(state) {
                _onPicked = onPicked;

                minimumSize = new Vector2(
                    FlockEditorUI.CreatePatternDropdownMinWidth,
                    FlockEditorUI.CreatePatternDropdownMinHeight);
            }

            public void RefreshItems() {
                _types = TypeCache.GetTypesDerivedFrom<FlockLayer3PatternProfile>()
                    .ToArray();
            }

            protected override AdvancedDropdownItem BuildRoot() {
                _idToType.Clear();
                _nextId = 1;

                AdvancedDropdownItem root = new AdvancedDropdownItem("Create Layer-3 Pattern");
                Texture2D icon = EditorGUIUtility.IconContent("ScriptableObject Icon")?.image as Texture2D;

                Dictionary<string, List<Type>> groups = new Dictionary<string, List<Type>>(FlockEditorUI.CreatePatternGroupsInitialCapacity);

                for (int index = 0; index < _types.Length; index += 1) {
                    Type type = _types[index];
                    if (type == null || type.IsAbstract) {
                        continue;
                    }

                    string group = GetGroupName(type);
                    if (!groups.TryGetValue(group, out List<Type> list)) {
                        list = new List<Type>(FlockEditorUI.CreatePatternGroupListInitialCapacity);
                        groups.Add(group, list);
                    }

                    list.Add(type);
                }

                List<string> groupKeys = new List<string>(groups.Keys);
                groupKeys.Sort(StringComparer.OrdinalIgnoreCase);

                bool anyAdded = false;

                foreach (string groupKey in groupKeys) {
                    AdvancedDropdownItem groupItem = new AdvancedDropdownItem(groupKey);
                    root.AddChild(groupItem);

                    List<Type> list = groups[groupKey];
                    list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(GetPrettyName(a), GetPrettyName(b)));

                    for (int index = 0; index < list.Count; index += 1) {
                        Type type = list[index];

                        int id = _nextId;
                        _nextId += 1;

                        _idToType[id] = type;

                        AdvancedDropdownItem item = new AdvancedDropdownItem(GetPrettyName(type)) {
                            id = id,
                            icon = icon
                        };

                        groupItem.AddChild(item);
                        anyAdded = true;
                    }
                }

                if (!anyAdded) {
                    root.AddChild(new AdvancedDropdownItem("No concrete pattern profile types found"));
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item) {
                if (item == null) {
                    return;
                }

                if (_idToType.TryGetValue(item.id, out Type type)) {
                    _onPicked?.Invoke(type);
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
