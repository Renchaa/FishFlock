#if UNITY_EDITOR
using Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockController;
using Flock.Scripts.Build.Core.Simulation.Runtime.Spawn;
using Flock.Scripts.Build.Agents.Fish.Profiles;

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Flock.Scripts.Editor.Inspectors
{
    /**
     * <summary>
     * Custom inspector for <see cref="FlockMainSpawner"/> that renders spawn configuration arrays with
     * a structured UI and can sync type entries from a <see cref="FlockController"/>.
     * </summary>
     */
    [CustomEditor(typeof(FlockMainSpawner))]
    public sealed class FlockMainSpawnerEditor : UnityEditor.Editor
    {
        private FlockMainSpawner spawner;
        private FlockController controller;

        private void OnEnable()
        {
            spawner = (FlockMainSpawner)target;

            // Controller is usually on the same GameObject or parent.
            controller = spawner.GetComponent<FlockController>()
                ?? spawner.GetComponentInParent<FlockController>();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            DrawSpawnerBody();

            serializedObject.ApplyModifiedProperties();
        }

        /**
         * <summary>
         * Synchronizes all type/count entries in the spawner with the provided controller fish types,
         * preserving existing counts where possible.
         * </summary>
         * <param name="spawner">Spawner to modify.</param>
         * <param name="sourceTypes">Source fish types to mirror.</param>
         */
        public static void SyncTypesFromController(FlockMainSpawner spawner, FishTypePreset[] sourceTypes)
        {
            if (spawner == null)
            {
                return;
            }

            Undo.RecordObject(spawner, "Sync Flock Spawner Types");

            SerializedObject spawnerSerializedObject = new SerializedObject(spawner);
            spawnerSerializedObject.Update();

            SerializedProperty pointSpawnsProperty = spawnerSerializedObject.FindProperty("pointSpawns");
            SerializedProperty seedSpawnsProperty = spawnerSerializedObject.FindProperty("seedSpawns");

            if (pointSpawnsProperty != null && pointSpawnsProperty.isArray)
            {
                for (int index = 0; index < pointSpawnsProperty.arraySize; index += 1)
                {
                    SerializedProperty configProperty = pointSpawnsProperty.GetArrayElementAtIndex(index);
                    if (configProperty == null)
                    {
                        continue;
                    }

                    SerializedProperty typesProperty = configProperty.FindPropertyRelative("types");
                    SyncTypeCountEntries(typesProperty, sourceTypes);
                }
            }

            if (seedSpawnsProperty != null && seedSpawnsProperty.isArray)
            {
                for (int index = 0; index < seedSpawnsProperty.arraySize; index += 1)
                {
                    SerializedProperty configProperty = seedSpawnsProperty.GetArrayElementAtIndex(index);
                    if (configProperty == null)
                    {
                        continue;
                    }

                    SerializedProperty typesProperty = configProperty.FindPropertyRelative("types");
                    SyncTypeCountEntries(typesProperty, sourceTypes);
                }
            }

            spawnerSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(spawner);
        }

        private static void SyncTypeCountEntries(SerializedProperty typesProperty, FishTypePreset[] sourceTypes)
        {
            if (typesProperty == null || !typesProperty.isArray)
            {
                return;
            }

            if (sourceTypes == null || sourceTypes.Length == 0)
            {
                typesProperty.arraySize = 0;
                return;
            }

            Dictionary<FishTypePreset, int> existingCounts =
                new Dictionary<FishTypePreset, int>(typesProperty.arraySize);

            for (int index = 0; index < typesProperty.arraySize; index += 1)
            {
                SerializedProperty entryProperty = typesProperty.GetArrayElementAtIndex(index);
                if (entryProperty == null)
                {
                    continue;
                }

                SerializedProperty presetProperty = entryProperty.FindPropertyRelative("preset");
                SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");

                FishTypePreset preset = (presetProperty != null)
                    ? presetProperty.objectReferenceValue as FishTypePreset
                    : null;

                if (preset == null)
                {
                    continue;
                }

                int count = (countProperty != null) ? countProperty.intValue : 0;

                if (!existingCounts.ContainsKey(preset))
                {
                    existingCounts.Add(preset, count);
                }
            }

            typesProperty.arraySize = sourceTypes.Length;

            for (int index = 0; index < sourceTypes.Length; index += 1)
            {
                FishTypePreset preset = sourceTypes[index];

                SerializedProperty entryProperty = typesProperty.GetArrayElementAtIndex(index);
                SerializedProperty presetProperty = entryProperty.FindPropertyRelative("preset");
                SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");

                if (presetProperty != null)
                {
                    presetProperty.objectReferenceValue = preset;
                }

                if (countProperty != null)
                {
                    countProperty.intValue =
                        (preset != null && existingCounts.TryGetValue(preset, out int preservedCount))
                            ? preservedCount
                            : 0;
                }
            }
        }

        private void DrawSpawnerBody()
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.name == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(iterator, true);
                    }
                    continue;
                }

                if (iterator.name == "pointSpawns" || iterator.name == "seedSpawns")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            EditorGUILayout.Space();

            SerializedProperty pointSpawnsProperty = serializedObject.FindProperty("pointSpawns");
            SerializedProperty seedSpawnsProperty = serializedObject.FindProperty("seedSpawns");

            DrawSpawnConfigs(pointSpawnsProperty);
            EditorGUILayout.Space();
            DrawSpawnConfigs(seedSpawnsProperty);
        }

        private void DrawSpawnConfigs(SerializedProperty arrayProperty)
        {
            if (!arrayProperty.isArray)
            {
                EditorGUILayout.PropertyField(arrayProperty, true);
                return;
            }

            bool isPoint = arrayProperty.name == "pointSpawns";
            string label = isPoint ? "Spawn Points" : "Spawn Seed";

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            int removeIndex = -1;

            for (int index = 0; index < arrayProperty.arraySize; index += 1)
            {
                SerializedProperty configProperty = arrayProperty.GetArrayElementAtIndex(index);
                if (configProperty == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(GUI.skin.box);

                using (new EditorGUILayout.HorizontalScope())
                {
                    UnityEngine.Object sourceObject = TryGetSpawnSourceObject(configProperty);

                    string title = (sourceObject != null)
                        ? sourceObject.name
                        : $"{(isPoint ? "Point" : "Seed")} Spawn [{index}]";

                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20f)))
                    {
                        removeIndex = index;
                    }
                }

                SerializedProperty configIterator = configProperty.Copy();
                SerializedProperty configEnd = configProperty.GetEndProperty();
                bool enterChildren = true;

                while (configIterator.NextVisible(enterChildren)
                       && !SerializedProperty.EqualContents(configIterator, configEnd))
                {
                    enterChildren = false;

                    if (configIterator.name == "types")
                    {
                        DrawTypeCounts(configIterator);
                        continue;
                    }

                    if (configIterator.name == "seed")
                    {
                        SerializedProperty useSeedProperty = configProperty.FindPropertyRelative("useSeed");
                        bool disable = (useSeedProperty != null) && !useSeedProperty.boolValue;

                        using (new EditorGUI.DisabledScope(disable))
                        {
                            EditorGUILayout.PropertyField(configIterator, true);
                        }
                        continue;
                    }

                    EditorGUILayout.PropertyField(configIterator, true);
                }

                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0 && removeIndex < arrayProperty.arraySize)
            {
                arrayProperty.DeleteArrayElementAtIndex(removeIndex);
            }

            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                const float addWidth = 140f;
                const float clearWidth = 70f;

                if (GUILayout.Button(
                        isPoint ? "Add Point Spawn" : "Add Seed Spawn",
                        EditorStyles.miniButton,
                        GUILayout.Width(addWidth)))
                {

                    int newIndex = arrayProperty.arraySize;
                    arrayProperty.InsertArrayElementAtIndex(newIndex);

                    SerializedProperty configProperty = arrayProperty.GetArrayElementAtIndex(newIndex);
                    SerializedProperty typesProperty = configProperty.FindPropertyRelative("types");
                    if (typesProperty != null)
                    {
                        typesProperty.arraySize = 0;
                    }
                }

                if (arrayProperty.arraySize > 0)
                {
                    WithRedBackground(() =>
                    {
                        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(clearWidth)))
                        {
                            arrayProperty.arraySize = 0;
                        }
                    });
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawTypeCounts(SerializedProperty typesProperty)
        {
            if (!typesProperty.isArray)
            {
                return;
            }

            FishTypePreset[] fishTypes = (controller != null) ? controller.FishTypes : null;

            EditorGUILayout.LabelField("Types", EditorStyles.label);
            EditorGUI.indentLevel++;

            if (fishTypes == null || fishTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No FishTypes found on FlockController.\n" +
                    "Configure them in FlockSetup and Apply Setup → Controller first.",
                    MessageType.Info);

                EditorGUI.indentLevel--;
                return;
            }

            string[] names = new string[fishTypes.Length];
            for (int index = 0; index < fishTypes.Length; index += 1)
            {
                FishTypePreset preset = fishTypes[index];

                if (preset == null)
                {
                    names[index] = $"<Null {index}>";
                    continue;
                }

                if (!string.IsNullOrEmpty(preset.DisplayName))
                {
                    names[index] = preset.DisplayName;
                    continue;
                }

                names[index] = preset.name;
            }

            int removeIndex = -1;

            for (int index = 0; index < typesProperty.arraySize; index += 1)
            {
                SerializedProperty entryProperty = typesProperty.GetArrayElementAtIndex(index);
                if (entryProperty == null)
                {
                    continue;
                }

                SerializedProperty presetProperty = entryProperty.FindPropertyRelative("preset");
                SerializedProperty countProperty = entryProperty.FindPropertyRelative("count");

                int currentIndex = 0;

                FishTypePreset currentPreset = (presetProperty != null)
                    ? presetProperty.objectReferenceValue as FishTypePreset
                    : null;

                if (currentPreset != null)
                {
                    for (int fishIndex = 0; fishIndex < fishTypes.Length; fishIndex += 1)
                    {
                        if (fishTypes[fishIndex] == currentPreset)
                        {
                            currentIndex = fishIndex;
                            break;
                        }
                    }
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                const float popupWidth = 140f;
                const float countWidth = 60f;

                int newIndex = EditorGUILayout.Popup(
                    currentIndex,
                    names,
                    EditorStyles.popup,
                    GUILayout.Width(popupWidth));

                if (presetProperty != null
                    && newIndex >= 0
                    && newIndex < fishTypes.Length)
                {
                    presetProperty.objectReferenceValue = fishTypes[newIndex];
                }

                if (countProperty != null)
                {
                    EditorGUILayout.PropertyField(
                        countProperty,
                        GUIContent.none,
                        GUILayout.Width(countWidth));
                }

                if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(18f)))
                {
                    removeIndex = index;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0 && removeIndex < typesProperty.arraySize)
            {
                typesProperty.DeleteArrayElementAtIndex(removeIndex);
            }

            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Add Type", EditorStyles.miniButton, GUILayout.Width(90f)))
                {
                    int newIndex = typesProperty.arraySize;
                    typesProperty.InsertArrayElementAtIndex(newIndex);

                    SerializedProperty newEntry = typesProperty.GetArrayElementAtIndex(newIndex);
                    SerializedProperty presetProperty = newEntry.FindPropertyRelative("preset");
                    SerializedProperty countProperty = newEntry.FindPropertyRelative("count");

                    if (presetProperty != null && fishTypes.Length > 0)
                    {
                        presetProperty.objectReferenceValue = fishTypes[0];
                    }

                    if (countProperty != null)
                    {
                        countProperty.intValue = 0;
                    }
                }

                if (typesProperty.arraySize > 0)
                {
                    WithRedBackground(() =>
                    {
                        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(70f)))
                        {
                            typesProperty.arraySize = 0;
                        }
                    });
                }
            }

            EditorGUI.indentLevel--;
        }

        private static UnityEngine.Object TryGetSpawnSourceObject(SerializedProperty configProperty)
        {
            SerializedProperty property;

            property = configProperty.FindPropertyRelative("spawnPoint");
            if (property != null
                && property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue != null)
            {
                return property.objectReferenceValue;
            }

            property = configProperty.FindPropertyRelative("point");
            if (property != null
                && property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue != null)
            {
                return property.objectReferenceValue;
            }

            property = configProperty.FindPropertyRelative("transform");
            if (property != null
                && property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue != null)
            {
                return property.objectReferenceValue;
            }

            property = configProperty.FindPropertyRelative("spawnTransform");
            if (property != null
                && property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue != null)
            {
                return property.objectReferenceValue;
            }

            property = configProperty.FindPropertyRelative("spawn");
            if (property != null
                && property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue != null)
            {
                return property.objectReferenceValue;
            }

            return null;
        }

        private static void WithRedBackground(System.Action draw)
        {
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1.0f, 0.55f, 0.55f, 1.0f);

            try
            {
                draw?.Invoke();
            }
            finally
            {
                GUI.backgroundColor = previousColor;
            }
        }
    }
}
#endif
