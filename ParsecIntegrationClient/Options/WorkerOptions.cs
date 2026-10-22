using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace ParsecIntegrationClient.Options
{
    /// <summary>
    /// Конфигурация службы из appsettings.json. Раньше настройки читались самодельным
    /// SettingsService через Json.NET из C:\IntegrationClient\appsettings.json;
    /// теперь это обычные секции конфигурации .NET.
    /// </summary>
    public sealed class WorkerOptions
    {
        public PathsOptions Paths { get; set; }
        public LogOptions Log { get; set; }
        public ParsecOptions Parsec { get; set; }
        public ServiceOptions Service { get; set; }

        public WorkerOptions()
        {
            Paths = new PathsOptions();
            Log = new LogOptions();
            Parsec = new ParsecOptions();
            Service = new ServiceOptions();
        }

        public static WorkerOptions Bind(IConfiguration configuration)
        {
            WorkerOptions options = new WorkerOptions();

            if (configuration == null)
            {
                return options;
            }

            configuration.GetSection("Paths").Bind(options.Paths);
            configuration.GetSection("Log").Bind(options.Log);
            configuration.GetSection("Parsec").Bind(options.Parsec);
            configuration.GetSection("Service").Bind(options.Service);

            return options;
        }
    }

    public sealed class PathsOptions
    {
        /// <summary>Рабочий каталог службы: журнал, state.json, прежний файл настроек.</summary>
        public string MainPath { get; set; } = @"C:\IntegrationClient";
    }

    public sealed class LogOptions
    {
        /// <summary>Уровень журнала приложения: Trace | Debug | Information | Warning | Error.</summary>
        public string Level { get; set; } = "Warning";

        /// <summary>Уровень служебных категорий Microsoft.* и System.*.</summary>
        public string FrameworkLevel { get; set; } = "Warning";

        /// <summary>Пусто — писать в &lt;MainPath&gt;\log.</summary>
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>Сколько файлов журнала хранить.</summary>
        public int RetainedFileCount { get; set; } = 31;
    }

    public sealed class ParsecOptions
    {
        /// <summary>Адрес SOAP-сервиса интеграции Парсека (раньше был зашит в app.config).</summary>
        public string Url { get; set; } = "http://127.0.0.1:10101/IntegrationService/IntegrationService.asmx";

        public string Domain { get; set; } = "SYSTEM";
        public string User { get; set; } = "parsec";
        public string Password { get; set; } = "parsec";

        /// <summary>Таймаут одного SOAP-запроса, секунды.</summary>
        public int TimeoutSeconds { get; set; } = 100;

        /// <summary>Период продления сессии, секунды (раньше ContinueSessionJob — 4 минуты).</summary>
        public int SessionContinueSeconds { get; set; } = 240;

        /// <summary>Пауза между попытками авторизации, если Парсек недоступен, секунды.</summary>
        public int SessionRetrySeconds { get; set; } = 60;
    }

    public sealed class ServiceOptions
    {
        /// <summary>Строка подключения к БД Артонит (Firebird/ShieldPro).</summary>
        public string DatabaseConnectionString { get; set; } = string.Empty;

        /// <summary>Запрос очереди заданий. Пусто — значение по умолчанию из кода.</summary>
        public string QuerySelectIdDevCardString { get; set; } = string.Empty;

        /// <summary>Интервал обработки очереди, секунды (раньше триггер Quartz на 600 с).</summary>
        public int JobIntervalSeconds { get; set; } = 600;

        /// <summary>Пауза после ошибки, минуты.</summary>
        public int ErrorTimeoutMinutes { get; set; } = 1;

        /// <summary>Пауза между повторными проверками подключения к БД, секунды.</summary>
        public int DatabaseRetrySeconds { get; set; } = 60;

        /// <summary>Коды ошибок, при которых задание удаляется из очереди.</summary>
        public List<int> SkipErrCode { get; set; } = new List<int>();
    }
}

