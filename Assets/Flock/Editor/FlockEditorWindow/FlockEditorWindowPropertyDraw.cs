#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        static bool ShouldUseAttributeDrivenDrawer(SerializedProperty p) {
            if (p == null || p.serializedObject == null || p.serializedObject.targetObject == null) {
                return false;
            }

            var rootType = p.serializedObject.targetObject.GetType();

            if (!TryGetFieldInfoFromPropertyPath(rootType, p.propertyPath, out FieldInfo fi) || fi == null) {
                return false;
            }

            if (fi.GetCustomAttribute<RangeAttribute>(inherit: true) != null) return true;
            if (fi.GetCustomAttribute<MinAttribute>(inherit: true) != null) return true;

            return false;
        }

        static void DrawPropertyNoDecorators(SerializedProperty p, GUIContent labelOverride = null) {
            if (p == null) return;

            GUIContent label = labelOverride ?? EditorGUIUtility.TrTextContent(p.displayName);

            switch (p.propertyType) {
                case SerializedPropertyType.Boolean: {
                        EditorGUI.BeginChangeCheck();
                        bool v = EditorGUILayout.Toggle(label, p.boolValue);
                        if (EditorGUI.EndChangeCheck()) p.boolValue = v;
                        break;
                    }

                case SerializedPropertyType.Integer: {
                        if (ShouldUseAttributeDrivenDrawer(p)) {
                            FlockEditorGUI.PropertyFieldClamped(p, includeChildren: true, labelOverride: labelOverride);
                            break;
                        }

                        EditorGUI.BeginChangeCheck();
                        int v = EditorGUILayout.IntField(label, p.intValue);
                        if (EditorGUI.EndChangeCheck()) p.intValue = v;
                        break;
                    }

                case SerializedPropertyType.Float: {
                        if (ShouldUseAttributeDrivenDrawer(p)) {
                            FlockEditorGUI.PropertyFieldClamped(p, includeChildren: true, labelOverride: labelOverride);
                            break;
                        }

                        EditorGUI.BeginChangeCheck();
                        float v = EditorGUILayout.FloatField(label, p.floatValue);
                        if (EditorGUI.EndChangeCheck()) p.floatValue = v;
                        break;
                    }

                case SerializedPropertyType.Enum: {
                        EditorGUI.BeginChangeCheck();
                        int v = EditorGUILayout.Popup(label, p.enumValueIndex, p.enumDisplayNames);
                        if (EditorGUI.EndChangeCheck()) p.enumValueIndex = v;
                        break;
                    }

                case SerializedPropertyType.Vector2: {
                        EditorGUI.BeginChangeCheck();
                        Vector2 v = EditorGUILayout.Vector2Field(label, p.vector2Value);
                        if (EditorGUI.EndChangeCheck()) p.vector2Value = v;
                        break;
                    }

                case SerializedPropertyType.Vector3: {
                        EditorGUI.BeginChangeCheck();
                        Vector3 v = EditorGUILayout.Vector3Field(label, p.vector3Value);
                        if (EditorGUI.EndChangeCheck()) p.vector3Value = v;
                        break;
                    }

                default:
                    FlockEditorGUI.PropertyFieldClamped(p, includeChildren: true, labelOverride: labelOverride);
                    break;
            }
        }

        static bool TryGetHeaderForPropertyPath(Type rootType, string propertyPath, out string header) {
            header = null;
            if (!TryGetFieldInfoFromPropertyPath(rootType, propertyPath, out FieldInfo fi)) {
                return false;
            }

            var h = fi.GetCustomAttribute<HeaderAttribute>(inherit: true);
            if (h == null || string.IsNullOrEmpty(h.header)) {
                return false;
            }

            header = h.header;
            return true;
        }

        static bool TryGetFieldInfoFromPropertyPath(Type rootType, string propertyPath, out FieldInfo field) {
            field = null;
            if (rootType == null || string.IsNullOrEmpty(propertyPath)) return false;

            string[] parts = propertyPath.Split('.');
            Type currentType = rootType;
            FieldInfo currentField = null;

            for (int i = 0; i < parts.Length; i++) {
                string part = parts[i];

                if (part == "Array") continue;

                if (part.StartsWith("data[", StringComparison.Ordinal)) {
                    if (currentField == null) return false;

                    Type ft = currentField.FieldType;
                    if (ft.IsArray) currentType = ft.GetElementType();
                    else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                        currentType = ft.GetGenericArguments()[0];
                    else
                        currentType = ft;

                    continue;
                }

                currentField = FindFieldInHierarchy(currentType, part);
                if (currentField == null) return false;

                currentType = currentField.FieldType;
            }

            field = currentField;
            return field != null;
        }

        static FieldInfo FindFieldInHierarchy(Type t, string name) {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            while (t != null) {
                var f = t.GetField(name, Flags);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }
    }
}
#endif
