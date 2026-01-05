using UnityEngine;

namespace Flock.Runtime.Logging {
    /**
     * <summary>
     * Central logging facade for the flock runtime.
     * Applies global masks (hard clamp) and optional per-logger masks, then dispatches to Unity logging.
     * </summary>
     */
    public static class FlockLog {
        /**
         * <summary>
         * Global hard clamp for all flock log levels.
         * Typically configured once from <c>FlockController</c> in Awake.
         * </summary>
         */
        public static FlockLogLevel GlobalLevelsMask = FlockLogLevel.All;

        /**
         * <summary>
         * Global hard clamp for all flock log categories.
         * Typically configured once from <c>FlockController</c> in Awake.
         * </summary>
         */
        public static FlockLogCategory GlobalCategoriesMask = FlockLogCategory.All;

        /**
         * <summary>
         * Sets global masks explicitly (e.g. from a controller).
         * </summary>
         * <param name="levels">Enabled log levels mask.</param>
         * <param name="categories">Enabled log categories mask.</param>
         */
        public static void SetGlobalMask(FlockLogLevel levels, FlockLogCategory categories) {
            GlobalLevelsMask = levels;
            GlobalCategoriesMask = categories;
        }

        /**
         * <summary>
         * Derives global masks from an <see cref="IFlockLogger"/> instance.
         * </summary>
         * <param name="logger">Logger providing enabled masks. If null, no changes are applied.</param>
         */
        public static void ConfigureFrom(IFlockLogger logger) {
            if (logger == null) {
                return;
            }

            GlobalLevelsMask = logger.EnabledLevels;
            GlobalCategoriesMask = logger.EnabledCategories;
        }

        /**
         * <summary>
         * Logs a message using global masks first, then optional per-logger masks, then dispatches to Unity logging.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="level">The log level.</param>
         * <param name="message">The message to log.</param>
         * <param name="context">Optional Unity context object.</param>
         */
        public static void Log(
            IFlockLogger logger,
            FlockLogCategory category,
            FlockLogLevel level,
            string message,
            Object context = null) {

            if (!PassesGlobalMasks(category, level)) {
                return;
            }

            if (!PassesLoggerMasks(logger, category, level)) {
                return;
            }

            DispatchUnityLog(level, message, context);
        }

        /**
         * <summary>
         * Logs an info-level message.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="message">The message to log.</param>
         * <param name="context">Optional Unity context object.</param>
         */
        public static void Info(
            IFlockLogger logger,
            FlockLogCategory category,
            string message,
            Object context = null) {

            Log(logger, category, FlockLogLevel.Info, message, context);
        }

        /**
         * <summary>
         * Logs a warning-level message.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="message">The message to log.</param>
         * <param name="context">Optional Unity context object.</param>
         */
        public static void Warning(
            IFlockLogger logger,
            FlockLogCategory category,
            string message,
            Object context = null) {

            Log(logger, category, FlockLogLevel.Warning, message, context);
        }

        /**
         * <summary>
         * Logs an error-level message.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="message">The message to log.</param>
         * <param name="context">Optional Unity context object.</param>
         */
        public static void Error(
            IFlockLogger logger,
            FlockLogCategory category,
            string message,
            Object context = null) {

            Log(logger, category, FlockLogLevel.Error, message, context);
        }

        /**
         * <summary>
         * Logs an info-level formatted message.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="format">Composite format string.</param>
         * <param name="arguments">Format arguments.</param>
         */
        public static void InfoFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            string format,
            params object[] arguments) {

            string message = BuildFormattedMessage(format, arguments);
            Log(logger, category, FlockLogLevel.Info, message);
        }

        /**
         * <summary>
         * Logs an info-level formatted message with a Unity context object.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="context">Unity context object.</param>
         * <param name="format">Composite format string.</param>
         * <param name="arguments">Format arguments.</param>
         */
        public static void InfoFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            Object context,
            string format,
            params object[] arguments) {

            string message = BuildFormattedMessage(format, arguments);
            Log(logger, category, FlockLogLevel.Info, message, context);
        }

        /**
         * <summary>
         * Logs a warning-level formatted message.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="format">Composite format string.</param>
         * <param name="arguments">Format arguments.</param>
         */
        public static void WarningFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            string format,
            params object[] arguments) {

            string message = BuildFormattedMessage(format, arguments);
            Log(logger, category, FlockLogLevel.Warning, message);
        }

        /**
         * <summary>
         * Logs a warning-level formatted message with a Unity context object.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="context">Unity context object.</param>
         * <param name="format">Composite format string.</param>
         * <param name="arguments">Format arguments.</param>
         */
        public static void WarningFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            Object context,
            string format,
            params object[] arguments) {

            string message = BuildFormattedMessage(format, arguments);
            Log(logger, category, FlockLogLevel.Warning, message, context);
        }

        /**
         * <summary>
         * Logs an error-level formatted message.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="format">Composite format string.</param>
         * <param name="arguments">Format arguments.</param>
         */
        public static void ErrorFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            string format,
            params object[] arguments) {

            string message = BuildFormattedMessage(format, arguments);
            Log(logger, category, FlockLogLevel.Error, message);
        }

        /**
         * <summary>
         * Logs an error-level formatted message with a Unity context object.
         * </summary>
         * <param name="logger">Optional per-logger filter. If null, only global masks are applied.</param>
         * <param name="category">The category of the log.</param>
         * <param name="context">Unity context object.</param>
         * <param name="format">Composite format string.</param>
         * <param name="arguments">Format arguments.</param>
         */
        public static void ErrorFormat(
            IFlockLogger logger,
            FlockLogCategory category,
            Object context,
            string format,
            params object[] arguments) {

            string message = BuildFormattedMessage(format, arguments);
            Log(logger, category, FlockLogLevel.Error, message, context);
        }

        private static bool PassesGlobalMasks(FlockLogCategory category, FlockLogLevel level) {
            if ((GlobalLevelsMask & level) == 0) {
                return false;
            }

            if ((GlobalCategoriesMask & category) == 0) {
                return false;
            }

            return true;
        }

        private static bool PassesLoggerMasks(IFlockLogger logger, FlockLogCategory category, FlockLogLevel level) {
            if (logger == null) {
                return true;
            }

            if ((logger.EnabledLevels & level) == 0) {
                return false;
            }

            if ((logger.EnabledCategories & category) == 0) {
                return false;
            }

            return true;
        }

        private static void DispatchUnityLog(FlockLogLevel level, string message, Object context) {
            switch (level) {
                case FlockLogLevel.Info:
                    Debug.Log(message, context);
                    return;

                case FlockLogLevel.Warning:
                    Debug.LogWarning(message, context);
                    return;

                case FlockLogLevel.Error:
                    Debug.LogError(message, context);
                    return;

                default:
                    Debug.Log(message, context);
                    return;
            }
        }

        private static string BuildFormattedMessage(string format, object[] arguments) {
            if (string.IsNullOrEmpty(format)) {
                return string.Empty;
            }

            if (arguments == null || arguments.Length == 0) {
                return format;
            }

            return string.Format(format, arguments);
        }
    }
}
