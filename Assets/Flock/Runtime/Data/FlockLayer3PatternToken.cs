using System;
using System.Collections.Generic;
using UnityEngine;

namespace Flock.Runtime.Data {
    /**
     * <summary>
     * Runtime-only token that owns one or more Layer-3 runtime pattern handles.
     * This is the object you keep from StartLayer3Pattern(...) and pass back into
     * UpdateLayer3Pattern(...) / StopLayer3Pattern(...).
     * </summary>
     */
    public sealed class FlockLayer3PatternToken : ScriptableObject {
        [SerializeField]
        [HideInInspector]
        private int handleCount;

        [SerializeField]
        [HideInInspector]
        private bool isValid;

        // Not serialized intentionally (runtime-only). Keep it fast and simple.
        private FlockLayer3PatternHandle[] handles = Array.Empty<FlockLayer3PatternHandle>();

        /**
         * <summary>
         * Gets whether this token currently contains valid handles.
         * </summary>
         */
        public bool IsValid => isValid && handleCount > 0;

        /**
         * <summary>
         * Gets the number of handles currently owned by this token.
         * </summary>
         */
        public int HandleCount => handleCount;

        /**
         * <summary>
         * Returns the handle at the given index, or <see cref="FlockLayer3PatternHandle.Invalid"/> if unavailable.
         * </summary>
         * <param name="index">Handle index.</param>
         * <returns>The handle, or an invalid handle if the token or index is not valid.</returns>
         */
        public FlockLayer3PatternHandle GetHandle(int index) {
            if (!IsValid) {
                return FlockLayer3PatternHandle.Invalid;
            }

            if ((uint)index >= (uint)handleCount) {
                return FlockLayer3PatternHandle.Invalid;
            }

            return handles[index];
        }

        /**
         * <summary>
         * Replaces a handle at the given index if this token is valid.
         * </summary>
         * <param name="index">Handle index.</param>
         * <param name="handle">New handle value.</param>
         */
        public void ReplaceHandle(int index, FlockLayer3PatternHandle handle) {
            if (!isValid) {
                return;
            }

            if ((uint)index >= (uint)handleCount) {
                return;
            }

            handles[index] = handle;
        }

        /**
         * <summary>
         * Invalidates this token and clears the active handle count.
         * </summary>
         */
        public void Invalidate() {
            isValid = false;
            handleCount = 0;
        }

        /**
         * <summary>
         * Copies handles from a scratch list into this token and reuses the internal array when possible.
         * </summary>
         * <param name="source">Source list of handles.</param>
         */
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
