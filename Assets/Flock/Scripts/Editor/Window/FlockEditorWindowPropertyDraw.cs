#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Flock.Scripts.Editor.Window {
    /**
    * <summary>
    * Editor-only property drawing helpers for the flock editor window.
    * This partial contains attribute-driven drawer routing and header resolution utilities.
    * </summary>
    */
    public sealed partial class FlockEditorWindow {
        // Routes int/float fields through Unity's attribute-aware drawers (Range/Min) when present.
        private static bool ShouldUseAttributeDrivenDrawer(SerializedProperty property) {
            if (property == null || property.serializedObject == null || property.serializedObject.targetObject == null) {
                return false;
            }

            Type rootType = property.serializedObject.targetObject.GetType();

            if (!TryGetFieldInfoFromPropertyPath(rootType, property.propertyPath, out FieldInfo fieldInfo) || fieldInfo == null) {
                return false;
            }

            if (fieldInfo.GetCustomAttribute<RangeAttribute>(inherit: true) != null) {
                return true;
            }

            if (fieldInfo.GetCustomAttribute<MinAttribute>(inherit: true) != null) {
                return true;
            }

            return false;
        }

        private static void DrawPropertyNoDecorators(SerializedProperty property, GUIContent labelOverride = null) {
            if (property == null) {
                return;
            }

            GUIContent label = labelOverride ?? EditorGUIUtility.TrTextContent(property.displayName);

            switch (property.propertyType) {
                case SerializedPropertyType.Boolean:
                    DrawBooleanProperty(property, label);
                    break;

                case SerializedPropertyType.Integer:
                    DrawIntegerProperty(property, label, labelOverride);
                    break;

                case SerializedPropertyType.Float:
                    DrawFloatProperty(property, label, labelOverride);
                    break;

                case SerializedPropertyType.Enum:
                    DrawEnumProperty(property, label);
                    break;

                case SerializedPropertyType.Vector2:
                    DrawVector2Property(property, label);
                    break;

                case SerializedPropertyType.Vector3:
                    DrawVector3Property(property, label);
                    break;

                default:
                    DrawFallbackProperty(property, labelOverride);
                    break;
            }
        }

        private static void DrawBooleanProperty(SerializedProperty property, GUIContent label) {
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.Toggle(label, property.boolValue);
            if (EditorGUI.EndChangeCheck()) {
                property.boolValue = value;
            }
        }

        private static void DrawIntegerProperty(SerializedProperty property, GUIContent label, GUIContent labelOverride) {
            if (ShouldUseAttributeDrivenDrawer(property)) {
                FlockEditorGUI.PropertyFieldClamped(property, includeChildren: true, labelOverride: labelOverride);
                return;
            }

            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(label, property.intValue);
            if (EditorGUI.EndChangeCheck()) {
                property.intValue = value;
            }
        }

        private static void DrawFloatProperty(SerializedProperty property, GUIContent label, GUIContent labelOverride) {
            if (ShouldUseAttributeDrivenDrawer(property)) {
                FlockEditorGUI.PropertyFieldClamped(property, includeChildren: true, labelOverride: labelOverride);
                return;
            }

            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(label, property.floatValue);
            if (EditorGUI.EndChangeCheck()) {
                property.floatValue = value;
            }
        }

        private static void DrawEnumProperty(SerializedProperty property, GUIContent label) {
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.Popup(label, property.enumValueIndex, property.enumDisplayNames);
            if (EditorGUI.EndChangeCheck()) {
                property.enumValueIndex = value;
            }
        }

        private static void DrawVector2Property(SerializedProperty property, GUIContent label) {
            EditorGUI.BeginChangeCheck();
            Vector2 value = EditorGUILayout.Vector2Field(label, property.vector2Value);
            if (EditorGUI.EndChangeCheck()) {
                property.vector2Value = value;
            }
        }

        private static void DrawVector3Property(SerializedProperty property, GUIContent label) {
            EditorGUI.BeginChangeCheck();
            Vector3 value = EditorGUILayout.Vector3Field(label, property.vector3Value);
            if (EditorGUI.EndChangeCheck()) {
                property.vector3Value = value;
            }
        }

        private static void DrawFallbackProperty(SerializedProperty property, GUIContent labelOverride) {
            FlockEditorGUI.PropertyFieldClamped(property, includeChildren: true, labelOverride: labelOverride);
        }

        private static bool TryGetHeaderForPropertyPath(Type rootType, string propertyPath, out string header) {
            header = null;

            if (!TryGetFieldInfoFromPropertyPath(rootType, propertyPath, out FieldInfo fieldInfo)) {
                return false;
            }

            HeaderAttribute headerAttribute = fieldInfo.GetCustomAttribute<HeaderAttribute>(inherit: true);
            if (headerAttribute == null || string.IsNullOrEmpty(headerAttribute.header)) {
                return false;
            }

            header = headerAttribute.header;
            return true;
        }

        private static bool TryGetFieldInfoFromPropertyPath(Type rootType, string propertyPath, out FieldInfo fieldInfo) {
            fieldInfo = null;

            if (rootType == null || string.IsNullOrEmpty(propertyPath)) {
                return false;
            }

            string[] propertyPathParts = propertyPath.Split('.');
            Type currentType = rootType;
            FieldInfo currentFieldInfo = null;

            for (int partIndex = 0; partIndex < propertyPathParts.Length; partIndex += 1) {
                string part = propertyPathParts[partIndex];

                if (part == "Array") {
                    continue;
                }

                if (part.StartsWith("data[", StringComparison.Ordinal)) {
                    if (currentFieldInfo == null) {
                        return false;
                    }

                    Type fieldType = currentFieldInfo.FieldType;

                    if (fieldType.IsArray) {
                        currentType = fieldType.GetElementType();
                    } else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>)) {
                        currentType = fieldType.GetGenericArguments()[0];
                    } else {
                        currentType = fieldType;
                    }

                    continue;
                }

                currentFieldInfo = FindFieldInHierarchy(currentType, part);
                if (currentFieldInfo == null) {
                    return false;
                }

                currentType = currentFieldInfo.FieldType;
            }

            fieldInfo = currentFieldInfo;
            return fieldInfo != null;
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName) {
            const BindingFlags fieldSearchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            while (type != null) {
                FieldInfo fieldInfo = type.GetField(fieldName, fieldSearchFlags);
                if (fieldInfo != null) {
                    return fieldInfo;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
#endif
