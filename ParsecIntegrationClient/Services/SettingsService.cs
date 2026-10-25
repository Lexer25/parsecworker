using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using ParsecIntegrationClient.Options;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// Настройки службы. Основной источник — appsettings.json рядом с exe (секции Paths/Log/Parsec/Service).
    ///
    /// Для совместимости с уже работающими установками читается и прежний файл
    /// C:\IntegrationClient\appsettings.json (ключи _databaseConnectionString, _databaseJobTimeout и др.):
    /// если такой файл существует, его значения имеют приоритет, а в журнал пишется предупреждение
    /// с перечнем использованных ключей. Перенесите значения в новый appsettings.json и удалите старый файл.
    /// </summary>
    public static class SettingsService
    {
        // Встроенного значения нет: строка подключения обязательна и задаётся в appsettings.json
        // (Service:DatabaseConnectionString) или в прежнем C:\IntegrationClient\appsettings.json.
        public const string DefaultConnectionString = "";

        public const string DefaultQuerySelectIdDevCard =
            "select cd.id_cardindev as id, cd.id_card, cd.id_pep, cd.operation, cd.attempts from cardindev cd " +
            "where ((cd.id_dev in (select d.id_dev from device d join device d2 on d2.id_ctrl=d.id_ctrl and d2.id_reader is null " +
            "join servertypelist stl on d2.id_server=stl.id_server join servertype st on st.id=stl.id_type and st.sname='parsec' " +
            "where d.id_reader is not null)) or(cd.id_dev is null)) and cd.attempts<20 order by cd.id_cardindev";

        // --- прежний набор настроек, его читает весь существующий код ---
        public static string LogLevel = "Warning";
        public static string DatabaseConnectionString = DefaultConnectionString;

        /// <summary>Откуда взята строка подключения: appsettings.json, устаревший файл или «не задана».</summary>
        public static string DatabaseConnectionStringSource = "не задана";
        public static string QuerySelectIdDevCardString = DefaultQuerySelectIdDevCard;
        public static int DatabaseJobTimeout = 600;
        public static int ErrorTimeoutMinutes = 1;
        public static List<int> SkipErrCode = new List<int>();

        /// <summary>Размер батча PARSEC_QUEUE за один проход.</summary>
        public static int QueueBatchSize = 100;

        /// <summary>Аренда строки в работе, минуты.</summary>
        public static int QueueLeaseMinutes = 15;

        // --- настройки Парсека и сессии (раньше были зашиты в коде) ---
        public static string ParsecUrl = "http://127.0.0.1:10101/IntegrationService/IntegrationService.asmx";
        public static string ParsecDomain = "SYSTEM";
        public static string ParsecUser = "parsec";
        public static string ParsecPassword = "parsec";
        public static int ParsecTimeoutSeconds = 100;
        public static int SessionContinueSeconds = 240;
        public static int SessionRetrySeconds = 60;

        /// <summary>Ключи, значения которых взяты из устаревшего файла настроек.</summary>
        public static readonly List<string> LegacyKeysUsed = new List<string>();

        private static WorkerOptions _options = new WorkerOptions();

        public static void Initialize(WorkerOptions options)
        {
            _options = options ?? new WorkerOptions();
            Update();
        }

        /// <summary>Перечитывает настройки (вызывается при старте Worker).</summary>
        public static void Update()
        {
            WorkerOptions options = _options ?? new WorkerOptions();
            JObject legacy = ReadLegacyFile();
            LegacyKeysUsed.Clear();

            LogLevel = PickText(options.Log == null ? null : options.Log.Level, legacy, "_logLevel", "Warning");
            DatabaseConnectionString = PickText(options.Service.DatabaseConnectionString, legacy, "_databaseConnectionString", DefaultConnectionString);

            if (string.IsNullOrWhiteSpace(DatabaseConnectionString))
            {
                DatabaseConnectionStringSource = "не задана";
            }
            else if (LegacyKeysUsed.Contains("_databaseConnectionString"))
            {
                DatabaseConnectionStringSource = "устаревший файл " + Paths.LegacySettingsFilePath;
            }
            else
            {
                DatabaseConnectionStringSource = "appsettings.json";
            }
            QuerySelectIdDevCardString = PickText(options.Service.QuerySelectIdDevCardString, legacy, "_querySelectIdDevCardString", DefaultQuerySelectIdDevCard);
            DatabaseJobTimeout = PickInt(options.Service.JobIntervalSeconds, legacy, "_databaseJobTimeout", 600);
            ErrorTimeoutMinutes = PickInt(options.Service.ErrorTimeoutMinutes, legacy, "_errorTimeoutMinutes", 1);
            SkipErrCode = PickIntList(options.Service.SkipErrCode, legacy, "_skipErrCode");
            QueueBatchSize = options.Service.QueueBatchSize > 0 ? options.Service.QueueBatchSize : 100;
            QueueLeaseMinutes = options.Service.QueueLeaseMinutes > 0 ? options.Service.QueueLeaseMinutes : 15;

            if (options.Parsec != null)
            {
                ParsecUrl = PickOwn(options.Parsec.Url, ParsecUrl);
                ParsecDomain = PickOwn(options.Parsec.Domain, ParsecDomain);
                ParsecUser = PickOwn(options.Parsec.User, ParsecUser);
                ParsecPassword = PickOwn(options.Parsec.Password, ParsecPassword);
                ParsecTimeoutSeconds = options.Parsec.TimeoutSeconds > 0 ? options.Parsec.TimeoutSeconds : ParsecTimeoutSeconds;
                SessionContinueSeconds = options.Parsec.SessionContinueSeconds > 0 ? options.Parsec.SessionContinueSeconds : SessionContinueSeconds;
                SessionRetrySeconds = options.Parsec.SessionRetrySeconds > 0 ? options.Parsec.SessionRetrySeconds : SessionRetrySeconds;
            }
        }

        private static string PickOwn(string configured, string current)
        {
            return string.IsNullOrWhiteSpace(configured) ? current : configured.Trim();
        }

        private static JObject ReadLegacyFile()
        {
            string path = Paths.LegacySettingsFilePath;

            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Logger.Log("SettingsService", "Exception", "Не удалось прочитать " + path + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Строка: устаревший файл главнее нового, новый главнее значения по умолчанию.</summary>
        private static string PickText(string configured, JObject legacy, string legacyKey, string fallback)
        {
            JToken token = legacy == null ? null : legacy[legacyKey];
            if (token != null && token.Type != JTokenType.Null)
            {
                string text = token.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    LegacyKeysUsed.Add(legacyKey);
                    return text;
                }
            }

            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }

        private static int PickInt(int configured, JObject legacy, string legacyKey, int fallback)
        {
            JToken token = legacy == null ? null : legacy[legacyKey];
            if (token != null && token.Type != JTokenType.Null)
            {
                int value;
                if (int.TryParse(token.ToString(), out value))
                {
                    LegacyKeysUsed.Add(legacyKey);
                    return value;
                }
            }

            return configured > 0 ? configured : fallback;
        }

        private static List<int> PickIntList(List<int> configured, JObject legacy, string legacyKey)
        {
            JToken token = legacy == null ? null : legacy[legacyKey];
            JArray array = token as JArray;
            if (array != null)
            {
                List<int> values = new List<int>();

                foreach (JToken item in array)
                {
                    int value;
                    if (int.TryParse(item.ToString(), out value))
                    {
                        values.Add(value);
                    }
                }

                LegacyKeysUsed.Add(legacyKey);
                return values;
            }

            return configured ?? new List<int>();
        }
    }
}

