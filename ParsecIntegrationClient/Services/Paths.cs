using System;
using System.IO;
using ParsecIntegrationClient.Options;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// Рабочие каталоги службы. Раньше путь был зашит в Service1.MainPath ("C:\IntegrationClient")
    /// и использовался из нескольких мест; теперь он берётся из appsettings.json (Paths:MainPath).
    /// </summary>
    public static class Paths
    {
        private static string _mainPath = @"C:\IntegrationClient";
        private static string _logFolder;

        public static string MainPath
        {
            get { return _mainPath; }
        }

        /// <summary>Каталог журнала: Log:FolderPath, по умолчанию &lt;MainPath&gt;\log.</summary>
        public static string LogFolder
        {
            get { return string.IsNullOrWhiteSpace(_logFolder) ? Path.Combine(_mainPath, "log") : _logFolder; }
        }

        /// <summary>Файл состояния последней операции.</summary>
        public static string StateFilePath
        {
            get { return Path.Combine(_mainPath, "state.json"); }
        }

        /// <summary>Прежний файл настроек (ключи вида _databaseConnectionString).</summary>
        public static string LegacySettingsFilePath
        {
            get { return Path.Combine(_mainPath, "appsettings.json"); }
        }

        public static void Initialize(WorkerOptions options)
        {
            if (options == null)
            {
                return;
            }

            if (options.Paths != null && !string.IsNullOrWhiteSpace(options.Paths.MainPath))
            {
                _mainPath = options.Paths.MainPath.Trim();
            }

            _logFolder = options.Log == null || string.IsNullOrWhiteSpace(options.Log.FolderPath)
                ? null
                : options.Log.FolderPath.Trim();
        }

        /// <summary>
        /// Создаёт каталоги данных и журнала. Если каталог недоступен для записи,
        /// служба сообщает об этом в журнал, а не падает молча, как раньше.
        /// </summary>
        public static bool EnsureCreated(out string error)
        {
            error = null;

            try
            {
                Directory.CreateDirectory(MainPath);
                Directory.CreateDirectory(LogFolder);
                return true;
            }
            catch (Exception ex)
            {
                error = string.Format("каталог {0} или {1} недоступен: {2}", MainPath, LogFolder, ex.Message);
                return false;
            }
        }
    }
}
