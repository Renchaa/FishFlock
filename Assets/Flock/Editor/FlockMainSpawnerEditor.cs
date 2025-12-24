#if UNITY_EDITOR
using Flock.Runtime;
using System.Collections.Generic;
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


            EditorGUILayout.Space();

            DrawSpawnerBody();

            serializedObject.ApplyModifiedProperties();
        }

            public static void SyncTypesFromController(FlockMainSpawner spawner, FishTypePreset[] sourceTypes) {
            if (spawner == null) {
                return;
            }

            Undo.RecordObject(spawner, "Sync Flock Spawner Types");

            var so = new SerializedObject(spawner);
            so.Update();

            SerializedProperty pointSpawnsProp = so.FindProperty("pointSpawns");
            SerializedProperty seedSpawnsProp = so.FindProperty("seedSpawns");

            if (pointSpawnsProp != null && pointSpawnsProp.isArray) {
                for (int i = 0; i < pointSpawnsProp.arraySize; i += 1) {
                    SerializedProperty cfgProp = pointSpawnsProp.GetArrayElementAtIndex(i);
                    if (cfgProp == null) {
                        continue;
                    }

                    SerializedProperty typesProp = cfgProp.FindPropertyRelative("types");
                    SyncTypeCountEntries(typesProp, sourceTypes);
                }
            }

            if (seedSpawnsProp != null && seedSpawnsProp.isArray) {
                for (int i = 0; i < seedSpawnsProp.arraySize; i += 1) {
                    SerializedProperty cfgProp = seedSpawnsProp.GetArrayElementAtIndex(i);
                    if (cfgProp == null) {
                        continue;
                    }

                    SerializedProperty typesProp = cfgProp.FindPropertyRelative("types");
                    SyncTypeCountEntries(typesProp, sourceTypes);
                }
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(spawner);
        }

        static void SyncTypeCountEntries(SerializedProperty typesProp, FishTypePreset[] sourceTypes) {
            if (typesProp == null || !typesProp.isArray) {
                return;
            }

            if (sourceTypes == null || sourceTypes.Length == 0) {
                typesProp.arraySize = 0;
                return;
            }

            // Preserve existing counts by preset reference
            var existingCounts = new Dictionary<FishTypePreset, int>(typesProp.arraySize);
            for (int i = 0; i < typesProp.arraySize; i += 1) {
                SerializedProperty entryProp = typesProp.GetArrayElementAtIndex(i);
                if (entryProp == null) {
                    continue;
                }

                SerializedProperty presetProp = entryProp.FindPropertyRelative("preset");
                SerializedProperty countProp = entryProp.FindPropertyRelative("count");

                var preset = presetProp != null
                    ? presetProp.objectReferenceValue as FishTypePreset
                    : null;

                if (preset == null) {
                    continue;
                }

                int count = (countProp != null) ? countProp.intValue : 0;

                // First one wins (same as your prior logic effectively)
                if (!existingCounts.ContainsKey(preset)) {
                    existingCounts.Add(preset, count);
                }
            }

            // Rebuild array to match controller FishTypes exactly
            typesProp.arraySize = sourceTypes.Length;

            for (int i = 0; i < sourceTypes.Length; i += 1) {
                FishTypePreset preset = sourceTypes[i];

                SerializedProperty entryProp = typesProp.GetArrayElementAtIndex(i);
                SerializedProperty presetProp = entryProp.FindPropertyRelative("preset");
                SerializedProperty countProp = entryProp.FindPropertyRelative("count");

                if (presetProp != null) {
                    presetProp.objectReferenceValue = preset;
                }

                if (countProp != null) {
                    countProp.intValue =
                        (preset != null && existingCounts.TryGetValue(preset, out int preserved))
                            ? preserved
                            : 0;
                }
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
            string label = isPoint ? "Spawn Points" : "Spawn Seed";

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            int removeIndex = -1;

            for (int i = 0; i < arrayProp.arraySize; i++) {
                SerializedProperty cfgProp = arrayProp.GetArrayElementAtIndex(i);
                if (cfgProp == null) {
                    continue;
                }

                EditorGUILayout.BeginVertical(GUI.skin.box);

                using (new EditorGUILayout.HorizontalScope()) {
                    // Title = referenced spawn GO name (if present), else fallback label
                    UnityEngine.Object src = TryGetSpawnSourceObject(cfgProp);
                    string title = (src != null)
                        ? src.name
                        : $"{(isPoint ? "Point" : "Seed")} Spawn [{i}]";

                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20f))) {
                        removeIndex = i;
                    }
                }

                SerializedProperty cfgIter = cfgProp.Copy();
                SerializedProperty cfgEnd = cfgProp.GetEndProperty();
                bool enterChildren = true;

                while (cfgIter.NextVisible(enterChildren) &&
                       !SerializedProperty.EqualContents(cfgIter, cfgEnd)) {
                    enterChildren = false;

                    if (cfgIter.name == "types") {
                        DrawTypeCounts(cfgIter);
                    } else if (cfgIter.name == "seed") {
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
                    WithRedBackground(() => {
                        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(clearWidth))) {
                            arrayProp.arraySize = 0;
                        }
                    });
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

            EditorGUILayout.LabelField("Types", EditorStyles.label);
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
                    WithRedBackground(() => {
                        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(70f))) {
                            typesProp.arraySize = 0;
                        }
                    });
                }
            }

            EditorGUI.indentLevel--;
        }

        static UnityEngine.Object TryGetSpawnSourceObject(SerializedProperty cfgProp) {
            // Try the most likely field names first; safe if they don't exist.
            SerializedProperty p;

            p = cfgProp.FindPropertyRelative("spawnPoint");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null) return p.objectReferenceValue;

            p = cfgProp.FindPropertyRelative("point");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null) return p.objectReferenceValue;

            p = cfgProp.FindPropertyRelative("transform");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null) return p.objectReferenceValue;

            p = cfgProp.FindPropertyRelative("spawnTransform");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null) return p.objectReferenceValue;

            p = cfgProp.FindPropertyRelative("spawn");
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null) return p.objectReferenceValue;

            return null;
        }

        static void WithRedBackground(System.Action draw) {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1.0f, 0.55f, 0.55f, 1.0f); // reddish
            try { draw?.Invoke(); } finally { GUI.backgroundColor = prev; }
        }

    }
}
#endif
