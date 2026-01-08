#if UNITY_EDITOR
using Flock.Scripts.Build.Agents.Fish.Profiles;

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Flock.Scripts.Editor.Window
{
    /**
    * <summary>
    * Editor window UI for configuring and inspecting flock systems.
    * This partial renders the Fish Types (Presets) list panel, detail panel, and editor caching for the selected preset.
    * </summary>
    */
    public sealed partial class FlockEditorWindow
    {
        private void DrawSpeciesListPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(EditorUI.SpeciesListPanelWidth)))
            {
                DrawSpeciesListHeader();

                EnsureFishTypesListInitialized();

                _speciesListScroll = EditorGUILayout.BeginScrollView(_speciesListScroll);
                DrawSpeciesListRows(_setup.FishTypes);
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();

                DrawSpeciesListFooter();
            }
        }

        private void CreateNewFishTypePreset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Fish Type Preset",
                "FishTypePreset",
                "asset",
                "Choose a location for the new FishTypePreset asset");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            FishTypePreset fishTypePreset = ScriptableObject.CreateInstance<FishTypePreset>();
            AssetDatabase.CreateAsset(fishTypePreset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _setup.FishTypes.Add(fishTypePreset);
            EditorUtility.SetDirty(_setup);
            _selectedSpeciesIndex = _setup.FishTypes.Count - 1;

            RebuildSpeciesEditor();
            EditorGUIUtility.PingObject(fishTypePreset);
        }

        private void DrawSpeciesDetailPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Selected Fish Type", EditorStyles.boldLabel);

                if (!TryGetSelectedFishTypePreset(out FishTypePreset fishTypePreset))
                {
                    return;
                }

                EnsureSpeciesEditorsAreBuiltForSelection(fishTypePreset);

                DrawSpeciesInspectorModeToolbar(fishTypePreset);

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                DrawSpeciesInspectorBody(fishTypePreset);
                EditorGUILayout.EndScrollView();
            }
        }

        private void RebuildSpeciesEditor()
        {
            if (_setup == null
                || _setup.FishTypes == null
                || _selectedSpeciesIndex < 0
                || _selectedSpeciesIndex >= _setup.FishTypes.Count)
            {

                DestroySpeciesEditor();
                return;
            }

            FishTypePreset fishTypePreset = _setup.FishTypes[_selectedSpeciesIndex];
            EnsureEditor(ref _presetEditor, fishTypePreset);

            FishBehaviourProfile behaviourProfile = fishTypePreset != null ? fishTypePreset.BehaviourProfile : null;
            EnsureEditor(ref _behaviourEditor, behaviourProfile);
        }

        private void DestroySpeciesEditor()
        {
            DestroyEditor(ref _presetEditor);
            DestroyEditor(ref _behaviourEditor);
        }

        private void DrawSpeciesListHeader()
        {
            EditorGUILayout.LabelField("Fish Types (Presets)", EditorStyles.boldLabel);
        }

        private void EnsureFishTypesListInitialized()
        {
            if (_setup.FishTypes != null)
            {
                return;
            }

            _setup.FishTypes = new List<FishTypePreset>();
            EditorUtility.SetDirty(_setup);
        }

        private void DrawSpeciesListRows(List<FishTypePreset> fishTypePresets)
        {
            for (int index = 0; index < fishTypePresets.Count; index += 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    FishTypePreset fishTypePreset = fishTypePresets[index];

                    GUIStyle buttonStyle = index == _selectedSpeciesIndex
                        ? EditorStyles.miniButtonMid
                        : EditorStyles.miniButton;

                    string label;
                    if (fishTypePreset == null)
                    {
                        label = "<Empty Slot>";
                    }
                    else if (!string.IsNullOrEmpty(fishTypePreset.DisplayName))
                    {
                        label = fishTypePreset.DisplayName;
                    }
                    else
                    {
                        label = fishTypePreset.name;
                    }

                    if (GUILayout.Button(label, buttonStyle))
                    {
                        if (_selectedSpeciesIndex != index)
                        {
                            _selectedSpeciesIndex = index;
                            RebuildSpeciesEditor();
                        }
                    }

                    fishTypePresets[index] = (FishTypePreset)EditorGUILayout.ObjectField(
                        GUIContent.none,
                        fishTypePreset,
                        typeof(FishTypePreset),
                        false,
                        GUILayout.Width(EditorUI.SpeciesInlineObjectFieldWidth));

                    if (GUILayout.Button("X", GUILayout.Width(EditorUI.RemoveRowButtonWidth)))
                    {
                        fishTypePresets.RemoveAt(index);
                        EditorUtility.SetDirty(_setup);

                        if (_selectedSpeciesIndex == index)
                        {
                            _selectedSpeciesIndex = -1;
                            DestroySpeciesEditor();
                        }
                        else if (_selectedSpeciesIndex > index)
                        {
                            _selectedSpeciesIndex -= 1;
                        }

                        break;
                    }
                }
            }
        }

        private void DrawSpeciesListFooter()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Empty Slot", GUILayout.Width(EditorUI.AddEmptySlotButtonWidth)))
                {
                    _setup.FishTypes.Add(null);
                    EditorUtility.SetDirty(_setup);
                }

                if (GUILayout.Button("Add New Preset", GUILayout.Width(EditorUI.AddNewPresetButtonWidth)))
                {
                    CreateNewFishTypePreset();
                }
            }
        }

        private bool TryGetSelectedFishTypePreset(out FishTypePreset fishTypePreset)
        {
            fishTypePreset = null;

            if (_setup.FishTypes == null || _setup.FishTypes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No fish types in this setup yet.\n\n" +
                    "Use 'Add Empty Slot' or 'Add New Preset' to create entries.",
                    MessageType.Info);
                return false;
            }

            if (_selectedSpeciesIndex < 0 || _selectedSpeciesIndex >= _setup.FishTypes.Count)
            {
                EditorGUILayout.HelpBox(
                    "Select a fish type from the list on the left to edit its preset / behaviour.",
                    MessageType.Info);
                return false;
            }

            fishTypePreset = _setup.FishTypes[_selectedSpeciesIndex];
            if (fishTypePreset == null)
            {
                EditorGUILayout.HelpBox(
                    "This slot is empty. Assign an existing FishTypePreset or create a new one.",
                    MessageType.Warning);
                return false;
            }

            return true;
        }

        private void EnsureSpeciesEditorsAreBuiltForSelection(FishTypePreset fishTypePreset)
        {
            bool needRebuild = false;

            if (_presetEditor == null || _presetEditor.target != fishTypePreset)
            {
                needRebuild = true;
            }
            else
            {
                FishBehaviourProfile behaviourProfile = fishTypePreset.BehaviourProfile;
                if (behaviourProfile == null)
                {
                    if (_behaviourEditor != null && _behaviourEditor.target != null)
                    {
                        needRebuild = true;
                    }
                }
                else
                {
                    if (_behaviourEditor == null || _behaviourEditor.target != behaviourProfile)
                    {
                        needRebuild = true;
                    }
                }
            }

            if (needRebuild)
            {
                RebuildSpeciesEditor();
            }
        }

        private void DrawSpeciesInspectorModeToolbar(FishTypePreset fishTypePreset)
        {
            FishBehaviourProfile behaviourProfile = fishTypePreset.BehaviourProfile;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                string[] modeLabels = { "Preset", "Behaviour" };

                int currentModeIndex = Mathf.Clamp((int)CurrentSpeciesInspectorMode, 0, 1);
                int newModeIndex = GUILayout.Toolbar(
                    currentModeIndex,
                    modeLabels,
                    GUILayout.Width(EditorUI.InspectorModeToolbarWidth));

                newModeIndex = Mathf.Clamp(newModeIndex, 0, 1);
                CurrentSpeciesInspectorMode = (SpeciesInspectorMode)newModeIndex;

                if (behaviourProfile == null && CurrentSpeciesInspectorMode == SpeciesInspectorMode.Behaviour)
                {
                    CurrentSpeciesInspectorMode = SpeciesInspectorMode.Preset;
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawSpeciesInspectorBody(FishTypePreset fishTypePreset)
        {
            FishBehaviourProfile behaviourProfile = fishTypePreset.BehaviourProfile;

            if (CurrentSpeciesInspectorMode == SpeciesInspectorMode.Preset)
            {
                if (_presetEditor != null)
                {
                    _presetEditor.OnInspectorGUI();
                }
                return;
            }

            if (behaviourProfile == null)
            {
                EditorGUILayout.HelpBox(
                    "This FishTypePreset has no BehaviourProfile assigned.\n" +
                    "Assign one in the Preset view first.",
                    MessageType.Info);
                return;
            }

            DrawBehaviourProfileInspectorCards(behaviourProfile);
        }
    }
}
#endif
