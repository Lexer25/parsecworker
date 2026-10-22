using System;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Services.Logging;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// Прежний фасад журнала: весь существующий код вызывает Logger.Log&lt;T&gt;("Warning", "..."),
    /// а запись уходит в Microsoft.Extensions.Logging (файл в старом формате + консоль).
    /// </summary>
    public static class Logger
    {
        private static ILoggerFactory _factory;

        /// <summary>Вызывается из Program после сборки хоста.</summary>
        public static void Initialize(ILoggerFactory factory)
        {
            _factory = factory;
        }

        /// <summary>Категория журнала — имя класса, как и раньше.</summary>
        public static void Log<T>(string level, string message)
        {
            Write(typeof(T).Name, level, message);
        }

        /// <summary>Для статических классов: имя категории задаётся строкой.</summary>
        public static void Log(string category, string level, string message)
        {
            Write(category, level, message);
        }

        /// <summary>Строка уровня из appsettings.json → LogLevel.</summary>
        public static LogLevel ParseLevel(string name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "trace":
                    return LogLevel.Trace;
                case "debug":
                    return LogLevel.Debug;
                case "info":
                case "information":
                    return LogLevel.Information;
                case "warn":
                case "warning":
                    return LogLevel.Warning;
                case "error":
                case "exception":
                    return LogLevel.Error;
                case "critical":
                case "fatal":
                    return LogLevel.Critical;
                case "none":
                case "off":
                    return LogLevel.None;
                default:
                    return LogLevel.Warning;
            }
        }

        private static void Write(string category, string level, string message)
        {
            LogLevel mapped = MapLevel(level);
            ILoggerFactory factory = _factory;

            if (factory != null)
            {
                factory.CreateLogger(string.IsNullOrEmpty(category) ? "App" : category).Log(mapped, message);
                return;
            }

            // Хост ещё не собран (ошибка на старте) — пишем в тот же файл напрямую, чтобы не потерять сообщение.
            LegacyFileLoggerProvider.Append(Paths.LogFolder, category, mapped, message, null);
        }

        private static LogLevel MapLevel(string level)
        {
            switch ((level ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "trace":
                    return LogLevel.Trace;
                case "debug":
                    return LogLevel.Debug;
                case "info":
                case "information":
                    return LogLevel.Information;
                case "warn":
                case "warning":
                    return LogLevel.Warning;
                case "error":
                case "exception":
                    return LogLevel.Error;
                case "critical":
                case "fatal":
                    return LogLevel.Critical;
                default:
                    return LogLevel.Information;
            }
        }
    }
}
