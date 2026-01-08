#if UNITY_EDITOR
using UnityEngine;

namespace Flock.Scripts.Editor.Window
{
    /**
    * <summary>
    * Editor window for flock tooling. This partial contains shared UnityEditor.Editor lifetime helpers.
    * </summary>
    */
    public sealed partial class FlockEditorWindow
    {
        // Destroys a cached UnityEditor.Editor instance safely and clears the reference.
        private static void DestroyEditor(ref UnityEditor.Editor editor)
        {
            if (editor == null)
            {
                return;
            }

            Object.DestroyImmediate(editor);
            editor = null;
        }

        // Ensures the cached UnityEditor.Editor matches the provided target object (recreates if needed).
        private static void EnsureEditor(ref UnityEditor.Editor editor, Object targetObject)
        {
            if (targetObject == null)
            {
                DestroyEditor(ref editor);
                return;
            }

            if (editor != null && editor.target == targetObject)
            {
                return;
            }

            DestroyEditor(ref editor);
            editor = UnityEditor.Editor.CreateEditor(targetObject);
        }
    }
}
#endif
