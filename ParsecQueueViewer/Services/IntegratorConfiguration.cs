using System.IO;
using System.Text.Json;
using FirebirdSql.Data.FirebirdClient;

namespace ParsecQueueViewer.Services
{
    /// <summary>
    /// Параметры подключения, прочитанные из appsettings.json интегратора.
    /// Строка подключения нигде не показывается целиком: в интерфейс выводится
    /// только сервер и имя файла базы, без логина и пароля.
    /// </summary>
    public sealed class IntegratorConfiguration
    {
        private IntegratorConfiguration()
        {
        }

        public string ConfigPath { get; private set; }
        public string ConfigDirectory { get; private set; }
        public string ConnectionString { get; private set; }
        public string ConnectionDisplay { get; private set; }
        public string ConnectionTarget { get; private set; }
        public string ConnectionSource { get; private set; }
        public string MainPath { get; private set; }
        public string ParsecUrl { get; private set; }
        public bool UsesLegacyConnectionString { get; private set; }

        public static IntegratorConfiguration Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidDataException("Не указан путь к appsettings.json интегратора.");
            }

            string fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Файл настроек интегратора не найден.", fullPath);
            }

            string json;
            try
            {
                json = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Не удалось прочитать " + fullPath + ": " + ex.Message, ex);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "Файл " + fullPath + " не является корректным JSON: " + ex.Message, ex);
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Корень appsettings.json должен быть JSON-объектом.");
                }

                JsonElement root = document.RootElement;
                string mainPath = GetString(root, "Paths", "MainPath");
                string parsecUrl = GetString(root, "Parsec", "Url");
                string connectionString = GetString(root, "Service", "DatabaseConnectionString");
                string source = "Service:DatabaseConnectionString";
                bool legacy = false;

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    connectionString = TryReadLegacyConnectionString(mainPath, fullPath, out string legacyPath);
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        source = "устаревший файл " + legacyPath + " (_databaseConnectionString)";
                        legacy = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidDataException(
                        "В " + fullPath + " не задана строка подключения Service:DatabaseConnectionString" +
                        " (и не найдено прежнее значение _databaseConnectionString).");
                }

                FbConnectionStringBuilder builder;
                try
                {
                    builder = new FbConnectionStringBuilder(connectionString);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "Строка подключения в " + fullPath + " имеет неверный формат: " + ex.Message, ex);
                }

                if (string.IsNullOrWhiteSpace(builder.Database))
                {
                    throw new InvalidDataException(
                        "В строке подключения " + fullPath + " не указана база данных (Database=...).");
                }

                string database = builder.Database.Trim();
                string databaseName = database;
                if (databaseName.IndexOf('\\') >= 0 || databaseName.IndexOf('/') >= 0)
                {
                    try
                    {
                        databaseName = Path.GetFileName(databaseName);
                    }
                    catch
                    {
                        databaseName = database;
                    }
                }

                string server = string.IsNullOrWhiteSpace(builder.DataSource)
                    ? "локальная БД"
                    : builder.DataSource.Trim() + ":" + builder.Port;

                return new IntegratorConfiguration
                {
                    ConfigPath = fullPath,
                    ConfigDirectory = Path.GetDirectoryName(fullPath),
                    ConnectionString = connectionString,
                    ConnectionDisplay = server + " · " + databaseName,
                    ConnectionTarget = database,
                    ConnectionSource = source,
                    MainPath = mainPath,
                    ParsecUrl = parsecUrl,
                    UsesLegacyConnectionString = legacy
                };
            }
        }

        private static string TryReadLegacyConnectionString(string mainPath, string currentConfigPath, out string legacyPath)
        {
            legacyPath = null;

            if (string.IsNullOrWhiteSpace(mainPath))
            {
                return null;
            }

            try
            {
                legacyPath = Path.Combine(
                    Environment.ExpandEnvironmentVariables(mainPath.Trim()),
                    "appsettings.json");

                if (!File.Exists(legacyPath) ||
                    string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(currentConfigPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string json = File.ReadAllText(legacyPath);
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return GetString(document.RootElement, "_databaseConnectionString")
                       ?? GetString(document.RootElement, "DatabaseConnectionString");
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(JsonElement element, params string[] path)
        {
            JsonElement current = element;

            foreach (string name in path)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !TryGetPropertyIgnoreCase(current, name, out current))
                {
                    return null;
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.ToString(),
                _ => null
            };
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
        {
            if (element.TryGetProperty(name, out value))
            {
                return true;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
