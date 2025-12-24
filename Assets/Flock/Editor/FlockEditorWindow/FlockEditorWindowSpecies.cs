#if UNITY_EDITOR
using Flock.Runtime;
using Flock.Runtime.Data;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        private void DrawSpeciesListPanel() {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(FlockEditorUI.SpeciesListPanelWidth))) {
                EditorGUILayout.LabelField("Fish Types (Presets)", EditorStyles.boldLabel);

                if (_setup.FishTypes == null) {
                    _setup.FishTypes = new System.Collections.Generic.List<FishTypePreset>();
                    EditorUtility.SetDirty(_setup);
                }

                _speciesListScroll = EditorGUILayout.BeginScrollView(_speciesListScroll);

                var types = _setup.FishTypes;
                for (int i = 0; i < types.Count; i++) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        var preset = types[i];

                        GUIStyle buttonStyle = (i == _selectedSpeciesIndex)
                            ? EditorStyles.miniButtonMid
                            : EditorStyles.miniButton;

                        string label;
                        if (preset == null) {
                            label = "<Empty Slot>";
                        } else if (!string.IsNullOrEmpty(preset.DisplayName)) {
                            label = preset.DisplayName;
                        } else {
                            label = preset.name;
                        }

                        if (GUILayout.Button(label, buttonStyle)) {
                            if (_selectedSpeciesIndex != i) {
                                _selectedSpeciesIndex = i;
                                RebuildSpeciesEditor();
                            }
                        }

                        types[i] = (FishTypePreset)EditorGUILayout.ObjectField(
                            GUIContent.none,
                            preset,
                            typeof(FishTypePreset),
                            false,
                            GUILayout.Width(FlockEditorUI.SpeciesInlineObjectFieldWidth));

                        if (GUILayout.Button("X", GUILayout.Width(FlockEditorUI.RemoveRowButtonWidth))) {
                            types.RemoveAt(i);
                            EditorUtility.SetDirty(_setup);

                            if (_selectedSpeciesIndex == i) {
                                _selectedSpeciesIndex = -1;
                                DestroySpeciesEditor();
                            } else if (_selectedSpeciesIndex > i) {
                                _selectedSpeciesIndex--;
                            }

                            break;
                        }
                    }
                }

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Add Empty Slot", GUILayout.Width(FlockEditorUI.AddEmptySlotButtonWidth))) {
                        _setup.FishTypes.Add(null);
                        EditorUtility.SetDirty(_setup);
                    }

                    if (GUILayout.Button("Add New Preset", GUILayout.Width(FlockEditorUI.AddNewPresetButtonWidth))) {
                        CreateNewFishTypePreset();
                    }
                }
            }
        }

        private void CreateNewFishTypePreset() {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Fish Type Preset",
                "FishTypePreset",
                "asset",
                "Choose a location for the new FishTypePreset asset");

            if (string.IsNullOrEmpty(path))
                return;

            var preset = ScriptableObject.CreateInstance<FishTypePreset>();
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.FishTypes.Add(preset);
            EditorUtility.SetDirty(_setup);
            _selectedSpeciesIndex = _setup.FishTypes.Count - 1;

            RebuildSpeciesEditor();
            EditorGUIUtility.PingObject(preset);
        }

        private void DrawSpeciesDetailPanel() {
            using (new EditorGUILayout.VerticalScope()) {
                EditorGUILayout.LabelField("Selected Fish Type", EditorStyles.boldLabel);

                if (_setup.FishTypes == null || _setup.FishTypes.Count == 0) {
                    EditorGUILayout.HelpBox(
                        "No fish types in this setup yet.\n\n" +
                        "Use 'Add Empty Slot' or 'Add New Preset' to create entries.",
                        MessageType.Info);
                    return;
                }

                if (_selectedSpeciesIndex < 0 || _selectedSpeciesIndex >= _setup.FishTypes.Count) {
                    EditorGUILayout.HelpBox(
                        "Select a fish type from the list on the left to edit its preset / behaviour.",
                        MessageType.Info);
                    return;
                }

                var preset = _setup.FishTypes[_selectedSpeciesIndex];
                if (preset == null) {
                    EditorGUILayout.HelpBox(
                        "This slot is empty. Assign an existing FishTypePreset or create a new one.",
                        MessageType.Warning);
                    return;
                }

                bool needRebuild = false;

                if (_presetEditor == null || _presetEditor.target != preset) {
                    needRebuild = true;
                } else {
                    var profile = preset.BehaviourProfile;
                    if (profile == null) {
                        if (_behaviourEditor != null && _behaviourEditor.target != null) {
                            needRebuild = true;
                        }
                    } else {
                        if (_behaviourEditor == null || _behaviourEditor.target != profile) {
                            needRebuild = true;
                        }
                    }
                }

                if (needRebuild) {
                    RebuildSpeciesEditor();
                }

                var behaviourProfile = preset.BehaviourProfile;

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();

                    string[] modeLabels = { "Preset", "Behaviour" };
                    _speciesInspectorMode = Mathf.Clamp(_speciesInspectorMode, 0, 1);
                    _speciesInspectorMode = GUILayout.Toolbar(
                        _speciesInspectorMode,
                        modeLabels,
                        GUILayout.Width(FlockEditorUI.InspectorModeToolbarWidth));

                    if (behaviourProfile == null && _speciesInspectorMode == 1) {
                        _speciesInspectorMode = 0;
                    }
                }

                EditorGUILayout.Space();

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

                if (_speciesInspectorMode == 0) {
                    if (_presetEditor != null) {
                        _presetEditor.OnInspectorGUI();
                    }
                } else {
                    if (behaviourProfile == null) {
                        EditorGUILayout.HelpBox(
                            "This FishTypePreset has no BehaviourProfile assigned.\n" +
                            "Assign one in the Preset view first.",
                            MessageType.Info);
                    } else {
                        DrawBehaviourProfileInspectorCards(behaviourProfile);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void RebuildSpeciesEditor() {
            if (_setup == null ||
                _setup.FishTypes == null ||
                _selectedSpeciesIndex < 0 ||
                _selectedSpeciesIndex >= _setup.FishTypes.Count) {

                DestroySpeciesEditor();
                return;
            }

            var preset = _setup.FishTypes[_selectedSpeciesIndex];
            EnsureEditor(ref _presetEditor, preset);

            var profile = preset != null ? preset.BehaviourProfile : null;
            EnsureEditor(ref _behaviourEditor, profile);
        }

        private void DestroySpeciesEditor() {
            DestroyEditor(ref _presetEditor);
            DestroyEditor(ref _behaviourEditor);
        }
    }
}
#endif
