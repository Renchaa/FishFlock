using UnityEngine;

namespace Flock.Runtime.Logging {
    public static class FlockLog {
        /// <summary>
        /// Global hard clamp for all flock logs.
        /// Typically configured once from FlockController in Awake.
        /// </summary>
        public static FlockLogLevel GlobalLevelsMask = FlockLogLevel.All;
        public static FlockLogCategory GlobalCategoriesMask = FlockLogCategory.All;

        /// <summary>
        /// Set global masks explicitly (e.g. from controller).
        /// </summary>
        public static void SetGlobalMask(FlockLogLevel levels, FlockLogCategory categories) {
            GlobalLevelsMask = levels;
            GlobalCategoriesMask = categories;
        }

        /// <summary>
        /// Convenience: derive global masks from an IFlockLogger instance.
        /// </summary>
        public static void ConfigureFrom(IFlockLogger logger) {
            if (logger == null) {
                return;
            }

            GlobalLevelsMask = logger.EnabledLevels;
            GlobalCategoriesMask = logger.EnabledCategories;
        }

        public static void Log(
            IFlockLogger logger,
            FlockLogCategory category,
            FlockLogLevel level,
            string message,
            Object context = null) {

            // 1) Hard global clamp (controller is source of truth).
            if ((GlobalLevelsMask & level) == 0) {
                return;
            }

            if ((GlobalCategoriesMask & category) == 0) {
                return;
            }

            // 2) Optional per-logger clamp (allows sub-systems to be stricter if they want).
            if (logger != null) {
                if ((logger.EnabledLevels & level) == 0) {
                    return;
                }

                if ((logger.EnabledCategories & category) == 0) {
                    return;
                }
            }

            // 3) Dispatch to Unity log.
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

        // --------------------------------------------------------------------
        // Simple helpers (what you're already using)
        // --------------------------------------------------------------------

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

        // --------------------------------------------------------------------
        // Format helpers (no overload ambiguity, different names)
        // --------------------------------------------------------------------

        public static void InfoFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            string format,
            params object[] args) {

            string message = BuildFormattedMessage(format, args);
            Log(logger, category, FlockLogLevel.Info, message);
        }

        public static void InfoFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            Object context,
            string format,
            params object[] args) {

            string message = BuildFormattedMessage(format, args);
            Log(logger, category, FlockLogLevel.Info, message, context);
        }

        public static void WarningFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            string format,
            params object[] args) {

            string message = BuildFormattedMessage(format, args);
            Log(logger, category, FlockLogLevel.Warning, message);
        }

        public static void WarningFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            Object context,
            string format,
            params object[] args) {

            string message = BuildFormattedMessage(format, args);
            Log(logger, category, FlockLogLevel.Warning, message, context);
        }

        public static void ErrorFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            string format,
            params object[] args) {

            string message = BuildFormattedMessage(format, args);
            Log(logger, category, FlockLogLevel.Error, message);
        }

        public static void ErrorFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            Object context,
            string format,
            params object[] args) {

            string message = BuildFormattedMessage(format, args);
            Log(logger, category, FlockLogLevel.Error, message, context);
        }

        static string BuildFormattedMessage(string format, object[] args) {
            if (string.IsNullOrEmpty(format)) {
                return string.Empty;
            }

            if (args == null || args.Length == 0) {
                return format;
            }

            return string.Format(format, args);
        }
    }
}
