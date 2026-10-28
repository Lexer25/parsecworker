using Newtonsoft.Json;
using ParsecIntegrationClient.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ParsecIntegrationClient.Services
{
    public class StateService
    {
        // Дефолтный путь state-файла
        public static string DefaultStateFilePath { get { return Paths.StateFilePath; } }

        // Словарь кодов операций и их названий
        public static readonly Dictionary<string, string> OperationNames = new Dictionary<string, string>
        {
            { "1", "Добавление_карточки" },
            { "2", "Удаление_карточки" },
            { "3", "Добавление_пользователя" },
            { "4", "Удаление_пользователя" },
            { "5", "Добавление_организации" },
            { "6", "Удаление_организации" },
            { "7", "Добавление_группы_доступа_карте" },
            { "8", "Удаление_группы_доступа_у_карты" },
            { "9", "Добавление_карточки" },
            { "10", "Удаление_карточки" },
            { "11", "Изменение_свойств_карточки" },
            { "35", "Обновление_пользователя" },
            { "55", "Обновление_организации" },
        };

        /// <summary>Возвращает название операции по её коду.</summary>
        public static string GetOperationName(string operationCode)
        {
            if (string.IsNullOrWhiteSpace(operationCode))
                return "Unknown";

            return OperationNames.TryGetValue(operationCode.Trim(), out var name)
                ? name
                : $"Unknown({operationCode})";
        }

        public static string GetLogFilePath(DateTime now)
        {
            return Path.Combine(Paths.LogFolder, string.Format("LogAt{0}_{1}_{2}.txt", now.Day, now.Month, now.Year));
        }

        public static long GetLogFileLength(string logFilePath)
        {
            try
            {
                if (!File.Exists(logFilePath))
                    return 0;
                return new FileInfo(logFilePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        public class StateUpdateResult
        {
            public string Status { get; set; } // OK / ERR
            public string ErrorMessage { get; set; }
        }

        /// <summary>Пишет state.json.</summary>
        public static void SaveState(StateSnapshot snapshot, string stateFilePath = null)
        {
            if (snapshot == null)
                return;

            stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;

            try
            {
                var dir = Path.GetDirectoryName(stateFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { /* ignore */ }

            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            File.WriteAllText(stateFilePath, json);
        }

        /// <summary>Читает state.json; null — если файла нет или он битый.</summary>
        public static StateSnapshot TryRead(string stateFilePath = null)
        {
            stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;
            try
            {
                if (!File.Exists(stateFilePath))
                    return null;

                return JsonConvert.DeserializeObject<StateSnapshot>(File.ReadAllText(stateFilePath));
            }
            catch
            {
                return null;
            }
        }

        public static bool HasErrorState(string stateFilePath = null)
        {
            var snapshot = TryRead(stateFilePath);
            return snapshot != null && snapshot.Status == OperationStatus.Error;
        }

        // Обновляет state.json ОДНОЙ записью:
        // - если в "новых" строках лога есть Error/Exception -> пишем ERR + описание ошибки
        // - иначе -> пишем OK
        public static StateUpdateResult UpdateStateFromLog(long prevLogPosition, string logFilePath,
            string idCardindev, string operation, string desc = null, string stateFilePath = null)
        {
            stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;

            string errorMessage = null;

            // Проверяем ГЛОБАЛЬНЫЙ таймаут для ошибок
            var errorTimeout = DateTime.Now.AddMinutes(-SettingsService.ErrorTimeoutMinutes);
            bool hasRecentError = false;

            try
            {
                var existing = TryRead(stateFilePath);
                if (existing != null && existing.Status == OperationStatus.Error)
                {
                    hasRecentError = existing.Timestamp > errorTimeout;
                    if (hasRecentError)
                    {
                        Logger.Log<StateService>("Warning", $"Global error timeout active until {existing.Timestamp.AddMinutes(SettingsService.ErrorTimeoutMinutes)}");
                    }
                }

                if (File.Exists(logFilePath))
                {
                    using (var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        long len = fs.Length;
                        if (prevLogPosition < 0 || prevLogPosition > len)
                            prevLogPosition = 0;

                        fs.Seek(prevLogPosition, SeekOrigin.Begin);
                        using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                        {
                            var newText = sr.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(newText))
                            {
                                // Берём последнюю ошибку, если она есть.
                                var lines = newText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                                for (int i = lines.Length - 1; i >= 0; i--)
                                {
                                    var line = lines[i];
                                    if (line != null && (line.Contains("LOG: Error-") || line.Contains("LOG: Exception-")))
                                    {
                                        var idx = line.IndexOf("Message:", StringComparison.OrdinalIgnoreCase);
                                        if (idx >= 0)
                                            errorMessage = line.Substring(idx + "Message:".Length).Trim();
                                        else
                                            errorMessage = line.Trim();
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Если чтение лога не получилось, не создаём вторичных записей — просто считаем, что OK.
                errorMessage = null;
            }

            if (hasRecentError)
            {
                return new StateUpdateResult
                {
                    Status = "ERR",
                    ErrorMessage = "Global error timeout active - processing stopped"
                };
            }

            var status = string.IsNullOrWhiteSpace(errorMessage) ? "OK" : "ERR";
            if (status == "ERR" && string.IsNullOrWhiteSpace(desc))
                desc = errorMessage;

            return new StateUpdateResult
            {
                Status = status,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }
    }
}