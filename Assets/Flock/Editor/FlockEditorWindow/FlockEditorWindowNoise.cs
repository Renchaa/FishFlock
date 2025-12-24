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
    public sealed partial class FlockEditorWindow {
        void DrawNoiseModeToolbar() {
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

        void DrawNoiseListPanel() {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(FlockEditorUI.NoiseListPanelWidth))) {
                _noiseListScroll = EditorGUILayout.BeginScrollView(_noiseListScroll);

                if (_noiseInspectorMode == 0) {
                    EditorGUILayout.LabelField("Group Noise Pattern", EditorStyles.boldLabel);

                    GroupNoisePatternProfile currentProfile =
                        _setup.GroupNoiseSettings as GroupNoisePatternProfile;

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

                    var patterns = _setup.PatternAssets;
                    int removeIndex = -1;

                    for (int i = 0; i < patterns.Count; i++) {
                        using (new EditorGUILayout.HorizontalScope()) {
                            FlockLayer3PatternProfile asset = patterns[i];

                            GUIStyle rowStyle = (_selectedNoiseIndex == i)
                                ? EditorStyles.miniButtonMid
                                : EditorStyles.miniButton;

                            string name = asset != null ? asset.name : "<Empty Slot>";
                            if (GUILayout.Button(name, rowStyle, GUILayout.Width(FlockEditorUI.ListNameColumnWidth))) {
                                _selectedNoiseIndex = i;
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
                                patterns[i] = asset;
                                EditorUtility.SetDirty(_setup);
                                if (_selectedNoiseIndex == i) {
                                    RebuildNoiseEditors();
                                }
                            }

                            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(FlockEditorUI.RemoveRowButtonWidth))) {
                                removeIndex = i;
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
                            _selectedNoiseIndex--;
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
                                var current = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
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
                                Rect r = GUILayoutUtility.GetLastRect();
                                ShowCreatePatternDropdown(r);
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

        void ShowCreatePatternDropdown(Rect buttonRect) {
            _createPatternDropdownState ??= new AdvancedDropdownState();

            _createPatternDropdown ??= new CreatePatternDropdown(
                _createPatternDropdownState,
                onPicked: CreatePatternAssetOfType);

            _createPatternDropdown.RefreshItems();
            _createPatternDropdown.Show(buttonRect);
        }

        void DrawNoiseDetailPanel() {
            using (new EditorGUILayout.VerticalScope()) {
                _noiseDetailScroll = EditorGUILayout.BeginScrollView(_noiseDetailScroll);

                if (_noiseInspectorMode == 0) {
                    var profile = _setup.GroupNoiseSettings as GroupNoisePatternProfile;
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

                var target = _setup.PatternAssets[_selectedNoiseIndex];
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

            if (string.IsNullOrEmpty(path))
                return;

            var asset = ScriptableObject.CreateInstance<GroupNoisePatternProfile>();
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

        void HandleGroupNoiseObjectPicker() {
            if (_noiseInspectorMode != 0 || _setup == null) return;

            Event e = Event.current;
            if (e == null) return;
            if (e.commandName != "ObjectSelectorClosed") return;
            if (EditorGUIUtility.GetObjectPickerControlID() != FlockEditorUI.GroupNoisePickerControlId) return;

            var picked = EditorGUIUtility.GetObjectPickerObject() as GroupNoisePatternProfile;
            if (picked == _setup.GroupNoiseSettings) return;

            _setup.GroupNoiseSettings = picked;
            EditorUtility.SetDirty(_setup);
            RebuildNoiseEditors();
        }

        void CreatePatternAssetOfType(Type patternType) {
            if (_setup == null || patternType == null) return;

            string defaultName = patternType.Name;
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Pattern Asset",
                defaultName,
                "asset",
                "Choose a location for the new pattern asset");

            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance(patternType) as FlockLayer3PatternProfile;
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

            if (_setup == null) return;

            if (_noiseInspectorMode == 0) {
                EnsureEditor(ref groupNoiseEditor, _setup.GroupNoiseSettings as GroupNoisePatternProfile);
                return;
            }

            if (_setup.PatternAssets == null) return;

            FlockLayer3PatternProfile target = null;
            if (_selectedNoiseIndex >= 0 && _selectedNoiseIndex < _setup.PatternAssets.Count) {
                target = _setup.PatternAssets[_selectedNoiseIndex];
            }

            EnsureEditor(ref _patternAssetEditor, target);
        }

        private void DestroyPatternAssetEditor() {
            DestroyEditor(ref _patternAssetEditor);
        }

        void DrawGroupNoiseInspectorCards(GroupNoisePatternProfile profile) {
            var so = new SerializedObject(profile);
            so.Update();

            SerializedProperty pBaseFrequency = so.FindProperty("baseFrequency");
            SerializedProperty pTimeScale = so.FindProperty("timeScale");
            SerializedProperty pPhaseOffset = so.FindProperty("phaseOffset");
            SerializedProperty pWorldScale = so.FindProperty("worldScale");
            SerializedProperty pSeed = so.FindProperty("seed");

            SerializedProperty pPatternType = so.FindProperty("patternType");

            SerializedProperty pSwirlStrength = so.FindProperty("swirlStrength");
            SerializedProperty pVerticalBias = so.FindProperty("verticalBias");

            SerializedProperty pVortexCenter = so.FindProperty("vortexCenterNorm");
            SerializedProperty pVortexRadius = so.FindProperty("vortexRadius");
            SerializedProperty pVortexTight = so.FindProperty("vortexTightness");

            SerializedProperty pSphereRadius = so.FindProperty("sphereRadius");
            SerializedProperty pSphereThick = so.FindProperty("sphereThickness");
            SerializedProperty pSphereSwirl = so.FindProperty("sphereSwirlStrength");
            SerializedProperty pSphereCenter = so.FindProperty("sphereCenterNorm");

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {

                FlockEditorGUI.BeginCard("Common");
                {
                    DrawPropertyNoDecorators(pBaseFrequency);
                    DrawPropertyNoDecorators(pTimeScale);
                    DrawPropertyNoDecorators(pPhaseOffset);
                    DrawPropertyNoDecorators(pWorldScale);
                    DrawPropertyNoDecorators(pSeed);
                }
                FlockEditorGUI.EndCard();

                FlockEditorGUI.BeginCard("Pattern Type");
                {
                    DrawPropertyNoDecorators(pPatternType);
                }
                FlockEditorGUI.EndCard();

                var patternType = (FlockGroupNoisePatternType)(pPatternType != null ? pPatternType.enumValueIndex : 0);

                switch (patternType) {
                    case FlockGroupNoisePatternType.SimpleSine:
                        FlockEditorGUI.BeginCard("Simple Sine Extras"); {
                            DrawPropertyNoDecorators(pSwirlStrength);
                            DrawPropertyNoDecorators(pVerticalBias);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.VerticalBands:
                        FlockEditorGUI.BeginCard("Vertical Bands Extras"); {
                            DrawPropertyNoDecorators(pSwirlStrength);
                            DrawPropertyNoDecorators(pVerticalBias);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.Vortex:
                        FlockEditorGUI.BeginCard("Vortex Settings"); {
                            DrawPropertyNoDecorators(pVortexCenter);
                            DrawPropertyNoDecorators(pVortexRadius);
                            DrawPropertyNoDecorators(pVortexTight);
                        }
                        FlockEditorGUI.EndCard();
                        break;

                    case FlockGroupNoisePatternType.SphereShell:
                        FlockEditorGUI.BeginCard("Sphere Shell Settings"); {
                            DrawPropertyNoDecorators(pSphereRadius);
                            DrawPropertyNoDecorators(pSphereThick);
                            DrawPropertyNoDecorators(pSphereSwirl);
                            DrawPropertyNoDecorators(pSphereCenter);
                        }
                        FlockEditorGUI.EndCard();
                        break;
                }
            });

            if (so.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(profile);
            }
        }

        void DrawPatternAssetInspectorCards(FlockLayer3PatternProfile target) {
            if (target == null) return;

            var so = new SerializedObject(target);
            so.Update();

            Type rootType = target.GetType();
            string currentSection = null;
            bool sectionOpen = false;

            FlockEditorGUI.WithLabelWidth(FlockEditorUI.DefaultLabelWidth, () => {
                SerializedProperty it = so.GetIterator();
                bool enterChildren = true;

                while (it.NextVisible(enterChildren)) {
                    enterChildren = false;

                    if (it.depth != 0) continue;
                    if (it.propertyPath == "m_Script") continue;

                    if (TryGetHeaderForPropertyPath(rootType, it.propertyPath, out string header)) {
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

                    var prop = it.Copy();

                    GUIContent labelOverride =
                        (!string.IsNullOrEmpty(currentSection) &&
                         string.Equals(prop.displayName, currentSection, StringComparison.Ordinal))
                            ? GUIContent.none
                            : null;

                    DrawPropertyNoDecorators(prop, labelOverride);
                }

                if (sectionOpen) {
                    FlockEditorGUI.EndCard();
                }
            });

            if (so.ApplyModifiedProperties()) {
                EditorUtility.SetDirty(target);
            }
        }

        sealed class CreatePatternDropdown : AdvancedDropdown {
            readonly Action<Type> _onPicked;

            Type[] _types = Array.Empty<Type>();

            readonly Dictionary<int, Type> _idToType =
                new Dictionary<int, Type>(FlockEditorUI.CreatePatternTypeMapInitialCapacity);
            int _nextId = 1;

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

                var root = new AdvancedDropdownItem("Create Layer-3 Pattern");
                var icon = EditorGUIUtility.IconContent("ScriptableObject Icon")?.image as Texture2D;

                var groups = new Dictionary<string, List<Type>>(FlockEditorUI.CreatePatternGroupsInitialCapacity);

                for (int i = 0; i < _types.Length; i++) {
                    var t = _types[i];
                    if (t == null || t.IsAbstract) continue;

                    string group = GetGroupName(t);
                    if (!groups.TryGetValue(group, out var list)) {
                        list = new List<Type>(FlockEditorUI.CreatePatternGroupListInitialCapacity);
                        groups.Add(group, list);
                    }
                    list.Add(t);
                }

                var groupKeys = new List<string>(groups.Keys);
                groupKeys.Sort(StringComparer.OrdinalIgnoreCase);

                bool anyAdded = false;

                foreach (var g in groupKeys) {
                    var groupItem = new AdvancedDropdownItem(g);
                    root.AddChild(groupItem);

                    var list = groups[g];
                    list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(GetPrettyName(a), GetPrettyName(b)));

                    for (int i = 0; i < list.Count; i++) {
                        var t = list[i];

                        int id = _nextId++;
                        _idToType[id] = t;

                        var item = new AdvancedDropdownItem(GetPrettyName(t)) {
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
                if (item == null) return;

                if (_idToType.TryGetValue(item.id, out var t)) {
                    _onPicked?.Invoke(t);
                }
            }

            static string GetGroupName(Type t) {
                string ns = t.Namespace ?? "";
                if (string.IsNullOrEmpty(ns)) return "Patterns";

                int lastDot = ns.LastIndexOf('.');
                return (lastDot >= 0 && lastDot < ns.Length - 1) ? ns.Substring(lastDot + 1) : ns;
            }

            static string GetPrettyName(Type t) {
                string name = t.Name;

                name = name.Replace("PatternProfile", "");
                name = name.Replace("Profile", "");
                name = name.Replace("Flock", "");
                name = name.Replace("Layer3", "");

                name = name.Trim();
                if (string.IsNullOrEmpty(name)) {
                    name = t.Name;
                }

                var sb = new StringBuilder(name.Length + 8);
                for (int i = 0; i < name.Length; i++) {
                    char c = name[i];
                    if (i > 0 && char.IsUpper(c) && char.IsLetterOrDigit(name[i - 1]) && !char.IsUpper(name[i - 1])) {
                        sb.Append(' ');
                    }
                    sb.Append(c);
                }

                return sb.ToString().Trim();
            }
        }
    }
}
#endif
