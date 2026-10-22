using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Services;

namespace ParsecIntegrationClient.Services.Logging
{
    /// <summary>
    /// Файловый журнал службы в прежнем виде: файл Log_At_ГГГГ_М_Д.txt в каталоге Log:FolderPath
    /// и строка формата "21.6.2026 14:30:0 LOG: Warning-MainJob Message: ...".
    /// Формат сохранён, потому что по нему разбираются старые журналы и state.json.
    /// </summary>
    public sealed class LegacyFileLoggerProvider : ILoggerProvider
    {
        private static readonly object SyncRoot = new object();
        private readonly string _folder;

        public LegacyFileLoggerProvider(string folder, int retainedFileCount)
        {
            _folder = string.IsNullOrWhiteSpace(folder) ? Paths.LogFolder : folder;
            CleanupOldFiles(retainedFileCount);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new LegacyFileLogger(categoryName, _folder);
        }

        public void Dispose()
        {
        }

        internal static string FilePath(string folder, DateTime now)
        {
            return Path.Combine(folder, string.Format("Log_At_{0}_{1}_{2}.txt", now.Year, now.Month, now.Day));
        }

        /// <summary>Одна строка журнала. Вызывается и из фасада Logger, когда хост ещё не собран.</summary>
        public static void Append(string folder, string category, LogLevel level, string message, Exception exception)
        {
            DateTime now = DateTime.Now;

            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2} {3}:{4}:{5} LOG: {6}-{7} Message: {8}",
                now.Day,
                now.Month,
                now.Year,
                now.Hour,
                now.Minute,
                now.Second,
                LevelWord(level),
                ShortenCategory(category),
                (message ?? string.Empty).PadRight(50));

            if (exception != null)
            {
                line = line + Environment.NewLine + exception;
            }

            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(FilePath(folder, now), line + Environment.NewLine);
                }
            }
            catch
            {
                // Журнал не должен ронять службу: файл может быть занят или каталог недоступен.
            }
        }

        // Категории ILogger<T> приходят полным именем типа ("ParsecIntegrationClient.Worker"),
        // а прежний журнал писал короткое имя класса — приводим к тому же виду.
        private static string ShortenCategory(string category)
        {
            const string rootNamespace = "ParsecIntegrationClient.";

            if (string.IsNullOrEmpty(category))
            {
                return "App";
            }

            return category.StartsWith(rootNamespace, StringComparison.Ordinal)
                ? category.Substring(rootNamespace.Length)
                : category;
        }

        private static string LevelWord(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                    return "Trace";
                case LogLevel.Debug:
                    return "Debug";
                case LogLevel.Information:
                    return "Info";
                case LogLevel.Warning:
                    return "Warning";
                case LogLevel.Error:
                    return "Error";
                case LogLevel.Critical:
                    return "Critical";
                default:
                    return level.ToString();
            }
        }

        private void CleanupOldFiles(int retainedFileCount)
        {
            if (retainedFileCount <= 0)
            {
                return;
            }

            try
            {
                DirectoryInfo folder = new DirectoryInfo(_folder);
                if (!folder.Exists)
                {
                    return;
                }

                FileInfo[] files = folder.GetFiles("Log_At_*.txt");
                if (files.Length <= retainedFileCount)
                {
                    return;
                }

                foreach (FileInfo file in files.OrderByDescending(f => f.LastWriteTime).Skip(retainedFileCount))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class LegacyFileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _folder;

        public LegacyFileLogger(string category, string folder)
        {
            _category = category;
            _folder = folder;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (formatter == null)
            {
                return;
            }

            string message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception == null)
            {
                return;
            }

            LegacyFileLoggerProvider.Append(_folder, _category, logLevel, message, exception);
        }
    }
}

