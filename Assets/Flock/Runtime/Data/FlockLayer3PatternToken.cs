// File: Assets/Flock/Runtime/Data/FlockLayer3PatternToken.cs
namespace Flock.Runtime.Data {
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Runtime-only token that owns one or more Layer-3 runtime pattern handles.
    /// This is the object you keep from StartLayer3Pattern(...) and pass back into
    /// UpdateLayer3Pattern(...) / StopLayer3Pattern(...).
    /// </summary>
    public sealed class FlockLayer3PatternToken : ScriptableObject {
        [SerializeField, HideInInspector]
        int handleCount;

        [SerializeField, HideInInspector]
        bool isValid;

        // Not serialized intentionally (runtime-only). Keep it fast and simple.
        FlockLayer3PatternHandle[] handles = Array.Empty<FlockLayer3PatternHandle>();

        public bool IsValid => isValid && handleCount > 0;
        public int HandleCount => handleCount;

        public FlockLayer3PatternHandle GetHandle(int index) {
            if (!IsValid) {
                return FlockLayer3PatternHandle.Invalid;
            }

            if ((uint)index >= (uint)handleCount) {
                return FlockLayer3PatternHandle.Invalid;
            }

            return handles[index];
        }

        public void ReplaceHandle(int index, FlockLayer3PatternHandle handle) {
            if (!isValid) {
                return;
            }

            if ((uint)index >= (uint)handleCount) {
                return;
            }

            handles[index] = handle;
        }

        public void Invalidate() {
            isValid = false;
            handleCount = 0;
        }

        /// <summary>
        /// Copies handles from a scratch list into this token. Reuses internal array if possible.
        /// </summary>
        public void SetHandles(List<FlockLayer3PatternHandle> source) {
            if (source == null || source.Count == 0) {
                Invalidate();
                return;
            }

            int count = source.Count;

            if (handles == null || handles.Length < count) {
                // Grow to next power of two to reduce future reallocations.
                int newSize = Mathf.NextPowerOfTwo(count);
                handles = new FlockLayer3PatternHandle[newSize];
            }

            for (int i = 0; i < count; i += 1) {
                handles[i] = source[i];
            }

            handleCount = count;
            isValid = true;
        }
    }
}
