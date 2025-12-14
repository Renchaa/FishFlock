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

            DrawSpawnConfigs(pointSpawnsProp);   // Point spawns
            EditorGUILayout.Space();
            DrawSpawnConfigs(seedSpawnsProp);  // Seed spawns
        }


        void DrawSpawnConfigs(SerializedProperty arrayProp) {
            if (!arrayProp.isArray) {
                EditorGUILayout.PropertyField(arrayProp, true);
                return;
            }

            bool isPoint = arrayProp.name == "pointSpawns";
            string label = isPoint ? "Point Spawns" : "Seed Spawns";

            // Simple section header (no foldout anymore)
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            int removeIndex = -1;

            // Draw existing spawn configs
            for (int i = 0; i < arrayProp.arraySize; i++) {
                SerializedProperty cfgProp = arrayProp.GetArrayElementAtIndex(i);
                if (cfgProp == null) {
                    continue;
                }

                EditorGUILayout.BeginVertical(GUI.skin.box);

                // Row header + small remove button on the right
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(
                        $"{(isPoint ? "Point" : "Seed")} Spawn [{i}]",
                        EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20f))) {
                        removeIndex = i;
                    }
                }

                // Draw all fields of the config except 'types' (handled separately)
                SerializedProperty cfgIter = cfgProp.Copy();
                SerializedProperty cfgEnd = cfgProp.GetEndProperty();
                bool enterChildren = true;

                while (cfgIter.NextVisible(enterChildren) &&
                       !SerializedProperty.EqualContents(cfgIter, cfgEnd)) {
                    enterChildren = false;

                    if (cfgIter.name == "types") {
                        // Custom drawer for per-type (preset + count)
                        DrawTypeCounts(cfgIter);
                    } else if (cfgIter.name == "seed") {
                        // Gray out seed when useSeed is false
                        SerializedProperty useSeedProp = cfgProp.FindPropertyRelative("useSeed");
                        bool disable = useSeedProp != null && !useSeedProp.boolValue;

                        using (new EditorGUI.DisabledScope(disable)) {
                            EditorGUILayout.PropertyField(cfgIter, true);
                        }
                    } else {
                        EditorGUILayout.PropertyField(cfgIter, true);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0 && removeIndex < arrayProp.arraySize) {
                arrayProp.DeleteArrayElementAtIndex(removeIndex);
            }

            // Bottom-right Add / Clear buttons (small)
            EditorGUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();

                const float addWidth = 140f;
                const float clearWidth = 70f;

                if (GUILayout.Button(
                        isPoint ? "Add Point Spawn" : "Add Seed Spawn",
                        EditorStyles.miniButton,
                        GUILayout.Width(addWidth))) {

                    int newIndex = arrayProp.arraySize;
                    arrayProp.InsertArrayElementAtIndex(newIndex);

                    SerializedProperty cfg = arrayProp.GetArrayElementAtIndex(newIndex);
                    SerializedProperty typesProp = cfg.FindPropertyRelative("types");
                    if (typesProp != null) {
                        typesProp.arraySize = 0;
                    }
                }

                if (arrayProp.arraySize > 0) {
                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(clearWidth))) {
                        arrayProp.arraySize = 0;
                    }
                }
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

            int removeIndex = -1;

            // One row per entry
            for (int i = 0; i < typesProp.arraySize; i++) {
                SerializedProperty entryProp = typesProp.GetArrayElementAtIndex(i);
                if (entryProp == null) {
                    continue;
                }

                SerializedProperty presetProp = entryProp.FindPropertyRelative("preset");
                SerializedProperty countProp = entryProp.FindPropertyRelative("count");

                // Resolve current index for popup
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

                EditorGUILayout.BeginHorizontal();

                // Push everything to the right
                GUILayout.FlexibleSpace();

                const float popupWidth = 140f;
                const float countWidth = 60f;

                // Smaller, right-aligned popup
                int newIndex = EditorGUILayout.Popup(
                    currentIndex,
                    names,
                    EditorStyles.popup,
                    GUILayout.Width(popupWidth));

                if (presetProp != null &&
                    newIndex >= 0 &&
                    newIndex < fishTypes.Length) {
                    presetProp.objectReferenceValue = fishTypes[newIndex];
                }

                // Count field
                if (countProp != null) {
                    EditorGUILayout.PropertyField(
                        countProp,
                        GUIContent.none,
                        GUILayout.Width(countWidth));
                }

                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(18f))) {
                    removeIndex = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0 && removeIndex < typesProp.arraySize) {
                typesProp.DeleteArrayElementAtIndex(removeIndex);
            }

            // Bottom-right Add / Clear for types
            EditorGUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Add Type", EditorStyles.miniButton, GUILayout.Width(90f))) {
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
                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(70f))) {
                        typesProp.arraySize = 0;
                    }
                }
            }

            EditorGUI.indentLevel--;
        }
    }
}
#endif
