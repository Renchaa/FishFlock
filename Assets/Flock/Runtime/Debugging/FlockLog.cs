// REPLACE FILE: Assets/Flock/Runtime/Logging/FlockLog.cs
using UnityEngine;

namespace Flock.Runtime.Logging {
    public static class FlockLog {
        public static void Log(
            IFlockLogger logger,
            FlockLogCategory category,
            FlockLogLevel level,
            string message,
            Object context = null) {
            if (logger == null) {
                return;
            }

            if ((logger.EnabledLevels & level) == 0) {
                return;
            }

            if ((logger.EnabledCategories & category) == 0) {
                return;
            }

            switch (level) {
                case FlockLogLevel.Info:
                    Debug.Log(message, context);
                    break;

                case FlockLogLevel.Warning:
                    Debug.LogWarning(message, context);
                    break;

                case FlockLogLevel.Error:
                    Debug.LogError(message, context);
                    break;

                default:
                    Debug.Log(message, context);
                    break;
            }
        }

        public static void Info(
            IFlockLogger logger,
            FlockLogCategory category,
            string message,
            Object context = null) {
            Log(
                logger,
                category,
                FlockLogLevel.Info,
                message,
                context);
        }

        public static void Warning(
            IFlockLogger logger,
            FlockLogCategory category,
            string message,
            Object context = null) {
            Log(
                logger,
                category,
                FlockLogLevel.Warning,
                message,
                context);
        }

        public static void Error(
            IFlockLogger logger,
            FlockLogCategory category,
            string message,
            Object context = null) {
            Log(
                logger,
                category,
                FlockLogLevel.Error,
                message,
                context);
        }
    }
}
