#if UNITY_EDITOR
using Flock.Runtime;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    [CustomEditor(typeof(FlockMainSpawner))]
    public sealed class FlockMainSpawnerEditor : UnityEditor.Editor {
        FlockMainSpawner spawner;
        FlockController controller;

        void OnEnable() {
            spawner = (FlockMainSpawner)target;

            // Controller is usually on the same GO or parent
            controller = spawner.GetComponent<FlockController>()
                        ?? spawner.GetComponentInParent<FlockController>();
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            DrawControllerSyncHeader();

            EditorGUILayout.Space();

            DrawSpawnerBody();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawControllerSyncHeader() {
            if (controller == null) {
                EditorGUILayout.HelpBox(
                    "No FlockController found on this GameObject or its parents.\n" +
                    "Type lists cannot be synced from FlockSetup.",
                    MessageType.Warning);
                return;
            }

            var fishTypes = controller.FishTypes;
            if (fishTypes == null || fishTypes.Length == 0) {
                EditorGUILayout.HelpBox(
                    "FlockController has no fish types.\n" +
                    "Configure fish types in FlockSetup and apply to the controller.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Fish Types Source: FlockSetup → FlockController", EditorStyles.miniBoldLabel);

            if (GUILayout.Button("Sync All Spawn Type Lists From Controller")) {
                spawner.EditorSyncTypesFrom(fishTypes);
                EditorUtility.SetDirty(spawner);
            }
        }

        void DrawSpawnerBody() {
            // Draw everything EXCEPT pointSpawns / seedSpawns here.
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren)) {
                enterChildren = false;

                // Script field – keep but make it read-only
                if (iterator.name == "m_Script") {
                    using (new EditorGUI.DisabledScope(true)) {
                        EditorGUILayout.PropertyField(iterator, true);
                    }
                    continue;
                }

                // Skip our custom-handled arrays here
                if (iterator.name == "pointSpawns" || iterator.name == "seedSpawns") {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            EditorGUILayout.Space();

            // Now explicitly draw the two spawn blocks with our custom UI
            SerializedProperty pointSpawnsProp = serializedObject.FindProperty("pointSpawns");
            SerializedProperty seedSpawnsProp = serializedObject.FindProperty("seedSpawns");

            DrawSpawnConfigs(pointSpawnsProp, true);   // Point spawns
            EditorGUILayout.Space();
            DrawSpawnConfigs(seedSpawnsProp, false);  // Seed spawns
        }


        void DrawSpawnConfigs(SerializedProperty arrayProp, bool isPoint) {
            if (arrayProp == null) {
                return;
            }

            string header = isPoint ? "Point Spawns" : "Seed Spawns";
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            // Add / Clear row
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button($"Add {(isPoint ? "Point" : "Seed")} Spawn")) {
                    int newIndex = arrayProp.arraySize;
                    arrayProp.InsertArrayElementAtIndex(newIndex);

                    SerializedProperty cfg = arrayProp.GetArrayElementAtIndex(newIndex);
                    // ensure the types array starts empty
                    SerializedProperty typesProp = cfg.FindPropertyRelative("types");
                    if (typesProp != null) {
                        typesProp.arraySize = 0;
                    }
                }

                if (arrayProp.arraySize > 0) {
                    if (GUILayout.Button("Clear All", GUILayout.Width(80f))) {
                        arrayProp.arraySize = 0;
                    }
                }
            }

            int removeIndex = -1;

            for (int i = 0; i < arrayProp.arraySize; i++) {
                SerializedProperty cfgProp = arrayProp.GetArrayElementAtIndex(i);
                if (cfgProp == null) {
                    continue;
                }

                EditorGUILayout.BeginVertical(GUI.skin.box);

                // small header + remove button
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(
                        $"{(isPoint ? "Point" : "Seed")} Spawn [{i}]",
                        EditorStyles.miniBoldLabel);

                    if (GUILayout.Button("X", GUILayout.Width(20f))) {
                        removeIndex = i;
                    }
                }

                // Draw fields of PointSpawnConfig / SeedSpawnConfig
                SerializedProperty cfgIter = cfgProp.Copy();
                SerializedProperty cfgEnd = cfgProp.GetEndProperty();
                bool enterChildren = true;

                while (cfgIter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(cfgIter, cfgEnd)) {
                    enterChildren = false;

                    if (cfgIter.name == "types") {
                        DrawTypeCounts(cfgIter);        // our custom per-type UI
                    } else {
                        EditorGUILayout.PropertyField(cfgIter, true);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0 && removeIndex < arrayProp.arraySize) {
                arrayProp.DeleteArrayElementAtIndex(removeIndex);
            }

            EditorGUI.indentLevel--;
        }

        void DrawTypeCounts(SerializedProperty typesProp) {
            if (!typesProp.isArray) {
                return;
            }

            // Source list – canonical fish types from controller / setup
            FishTypePreset[] fishTypes = controller != null ? controller.FishTypes : null;

            EditorGUILayout.LabelField("Type Counts", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;

            if (fishTypes == null || fishTypes.Length == 0) {
                EditorGUILayout.HelpBox(
                    "No FishTypes found on FlockController.\n" +
                    "Configure them in FlockSetup and Apply Setup → Controller first.",
                    MessageType.Info);
                EditorGUI.indentLevel--;
                return;
            }

            // Quick names array for popup
            string[] names = new string[fishTypes.Length];
            for (int i = 0; i < fishTypes.Length; i++) {
                FishTypePreset preset = fishTypes[i];
                if (preset == null) {
                    names[i] = $"<Null {i}>";
                } else if (!string.IsNullOrEmpty(preset.DisplayName)) {
                    names[i] = preset.DisplayName;
                } else {
                    names[i] = preset.name;
                }
            }

            // Add / Clear buttons
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Add Type")) {
                    int newIndex = typesProp.arraySize;
                    typesProp.InsertArrayElementAtIndex(newIndex);

                    SerializedProperty newEntry = typesProp.GetArrayElementAtIndex(newIndex);
                    SerializedProperty presetProp = newEntry.FindPropertyRelative("preset");
                    SerializedProperty countProp = newEntry.FindPropertyRelative("count");

                    if (presetProp != null && fishTypes.Length > 0) {
                        presetProp.objectReferenceValue = fishTypes[0];
                    }

                    if (countProp != null) {
                        countProp.intValue = 0;
                    }
                }

                if (typesProp.arraySize > 0) {
                    if (GUILayout.Button("Clear", GUILayout.Width(60f))) {
                        typesProp.arraySize = 0;
                    }
                }
            }

            int removeIndex = -1;

            // One row per entry
            for (int i = 0; i < typesProp.arraySize; i++) {
                SerializedProperty entryProp = typesProp.GetArrayElementAtIndex(i);
                if (entryProp == null) {
                    continue;
                }

                SerializedProperty presetProp = entryProp.FindPropertyRelative("preset");
                SerializedProperty countProp = entryProp.FindPropertyRelative("count");

                EditorGUILayout.BeginHorizontal();

                // Resolve current index
                int currentIndex = 0;
                FishTypePreset currentPreset = presetProp != null
                    ? presetProp.objectReferenceValue as FishTypePreset
                    : null;

                if (currentPreset != null) {
                    for (int j = 0; j < fishTypes.Length; j++) {
                        if (fishTypes[j] == currentPreset) {
                            currentIndex = j;
                            break;
                        }
                    }
                }

                // Dropdown for FishTypePreset
                int newIndex = EditorGUILayout.Popup(currentIndex, names);
                if (presetProp != null && newIndex >= 0 && newIndex < fishTypes.Length) {
                    presetProp.objectReferenceValue = fishTypes[newIndex];
                }

                // Count field
                if (countProp != null) {
                    EditorGUILayout.PropertyField(countProp, GUIContent.none, GUILayout.Width(80f));
                }

                if (GUILayout.Button("X", GUILayout.Width(20f))) {
                    removeIndex = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0 && removeIndex < typesProp.arraySize) {
                typesProp.DeleteArrayElementAtIndex(removeIndex);
            }

            EditorGUI.indentLevel--;
        }
    }
}
#endif
