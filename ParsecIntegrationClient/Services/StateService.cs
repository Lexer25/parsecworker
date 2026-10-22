using ParsecIntegrationClient;
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
            {"9", "Добавление_карточки" },
            {"10", "Удаление_карточки" },
            {"35", "Обновление_пользователя" },
            {"55", "Обновление_организации" },
        };

        /// <summary>
        /// Возвращает название операции по её коду
        /// </summary>
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

        private static string GetOperationText(string operationCode)
        {
            switch ((operationCode ?? string.Empty).Trim())
            {
                case "1": return "Добавление_карточки";
                case "2": return "Удаление_карточки";
                case "3": return "Добавление_пользователя";
                case "4": return "Удаление_пользователя";
                case "5": return "Добавление_организации";
                case "6": return "Удаление_организации";
                case "7": return "Добавление_группы_доступа_карте";
                case "8": return "Удаление_группы_доступа_у_карты";
                default: return $"Unknown({operationCode})";
            }
        }

        public static bool HasErrorState(string stateFilePath = null)
        {
            stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;
            try
            {
                if (!File.Exists(stateFilePath))
                    return false;

                var json = File.ReadAllText(stateFilePath);
                // Проверяем без парсинга JSON, чтобы не зависеть от Json.NET.
                return json != null && json.IndexOf("\"status\":\"ERR\"", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        public static void WriteState(string idCardindev, string operation, string status, string desc, string error, string attempts = null, string stateFilePath = null)
        {
            stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;

            try
            {
                var dir = Path.GetDirectoryName(stateFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { /* ignore */ }

            var now = DateTime.Now;
            var operationText = GetOperationText(operation);
            var state = new State
            {
                IdCardindev = idCardindev ?? string.Empty,
                OperationCode = operation ?? string.Empty,
                Operation = operationText,
                Status = status ?? string.Empty,
                desc = string.IsNullOrWhiteSpace(desc) ? null : desc,
                ErrorMessage = string.IsNullOrWhiteSpace(error) ? null : error,
                Attempts = attempts,
                Timestamp = now
            };

            File.WriteAllText(stateFilePath, state.ToString());
        }
        public static void SaveState(State state, string stateFilePath = null)
        {
            stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;

            try
            {
                var dir = Path.GetDirectoryName(stateFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { /* ignore */ }

            //var now = DateTime.Now;
            //var operationText = GetOperationText(operation);
            //var state = new State
            //{
            //    IdCardindev = idCardindev ?? string.Empty,
            //    OperationCode = operation ?? string.Empty,
            //    Operation = operationText,
            //    Status = status ?? string.Empty,
            //    desc = string.IsNullOrWhiteSpace(desc) ? null : desc,
            //    ErrorMessage = string.IsNullOrWhiteSpace(error) ? null : error,
            //    Attempts = attempts,
            //    Timestamp = now
            //};

            File.WriteAllText(stateFilePath, state.ToString());
        }

        // Обновляет state.json ОДНОЙ записью:
        // - если в "новых" строках лога есть Error/Exception -> пишем ERR + описание ошибки
        // - иначе -> пишем OK
        public static StateUpdateResult UpdateStateFromLog(long prevLogPosition, string logFilePath, string idCardindev, string operation, string desc = null, string stateFilePath = null)
        {
            stateFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;

            // Убедимся, что директория существует.
            try
            {
                var dir = Path.GetDirectoryName(stateFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch
            {
                // если не смогли создать каталог — дальше попробуем записать, как есть
            }

            DateTime now = DateTime.Now;
            string errorMessage = null;

            // Проверяем ГЛОБАЛЬНЫЙ таймаут для ошибок
            var errorTimeout = DateTime.Now.AddMinutes(-SettingsService.ErrorTimeoutMinutes);
            bool hasRecentError = false;

            // Читаем только то, что добавилось после prevLogPosition.
            try
            {
                if (!File.Exists(stateFilePath))
                    errorMessage = null;
                else
                {
                    var stateJson = File.ReadAllText(stateFilePath);
                    if (!string.IsNullOrWhiteSpace(stateJson))
                    {
                        // Проверяем время последней ошибки ГЛОБАЛЬНО
                        if (stateJson.Contains("\"status\":\"ERR\""))
                        {
                            // Извлекаем timestamp из JSON для проверки таймаута
                            var timestampMatch = System.Text.RegularExpressions.Regex.Match(stateJson, "\"timestamp\":\"([^\"]+)\"");
                            if (timestampMatch.Success && DateTime.TryParse(timestampMatch.Groups[1].Value, out DateTime lastErrorTime))
                            {
                                hasRecentError = lastErrorTime > errorTimeout;
                                if (hasRecentError)
                                {
                                    Logger.Log<StateService>("Warning", $"Global error timeout active until {lastErrorTime.AddMinutes(SettingsService.ErrorTimeoutMinutes)}");
                                }
                            }
                        }
                    }
                }

                if (!File.Exists(logFilePath))
                    errorMessage = null;
                else
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

            // Если есть последняя ошибка ГЛОБАЛЬНО и не истек таймаут - не меняем статус
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

            // Не перезаписываем state.json - он уже записан в ParsecService
            //WriteState(
            //    idCardindev: idCardindev,
            //    operation: operation,
            //    status: status,
            //    desc: desc,
            //    error: (status == "ERR" ? (errorMessage ?? string.Empty) : null),
            //    attempts: null,
            //    stateFilePath: stateFilePath);

            return new StateUpdateResult
            {
                Status = status,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }
    }
}

