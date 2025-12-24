#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Flock.Editor {
    public sealed partial class FlockEditorWindow {
        static void DestroyEditor(ref UnityEditor.Editor ed) {
            if (ed == null) return;
            Object.DestroyImmediate(ed);
            ed = null;
        }

        static void EnsureEditor(ref UnityEditor.Editor ed, Object target) {
            if (target == null) {
                DestroyEditor(ref ed);
                return;
            }

            if (ed != null && ed.target == target) {
                return;
            }

            DestroyEditor(ref ed);
            ed = UnityEditor.Editor.CreateEditor(target);
        }
    }
}
#endif
