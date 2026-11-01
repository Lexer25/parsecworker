using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Win32;

namespace ParsecQueueViewer.Win32
{
    internal sealed class IntegratorConfig
    {
        public string ConfigPath;
        public string ConnectionString;
        public string ConnectionDisplay;
        public string ConnectionSource;
        public string MainPath;
        public string ParsecUrl;

        public static IntegratorConfig Load(string path)
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

            string json = File.ReadAllText(fullPath, Encoding.UTF8);
            string cleanJson = JsonComments.Strip(json);

            Dictionary<string, object> root;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                root = serializer.Deserialize<Dictionary<string, object>>(cleanJson);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "Файл " + fullPath + " не является корректным JSON: " + ex.Message, ex);
            }

            if (root == null)
            {
                throw new InvalidDataException("Корень appsettings.json должен быть JSON-объектом.");
            }

            string mainPath = GetString(root, "Paths", "MainPath");
            string parsecUrl = GetString(root, "Parsec", "Url");
            string connectionString = GetString(root, "Service", "DatabaseConnectionString");
            string source = "Service:DatabaseConnectionString";

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                string legacyPath;
                connectionString = TryReadLegacyConnectionString(mainPath, fullPath, out legacyPath);
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    source = "устаревший файл " + legacyPath + " (_databaseConnectionString)";
                }
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidDataException(
                    "В " + fullPath + " не задана строка подключения Service:DatabaseConnectionString " +
                    "(и не найдено прежнее значение _databaseConnectionString).");
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

            return new IntegratorConfig
            {
                ConfigPath = fullPath,
                ConnectionString = connectionString,
                ConnectionDisplay = server + " · " + databaseName,
                ConnectionSource = source,
                MainPath = mainPath,
                ParsecUrl = parsecUrl
            };
        }

        private static string TryReadLegacyConnectionString(string mainPath, string currentConfigPath,
            out string legacyPath)
        {
            legacyPath = null;
            if (string.IsNullOrWhiteSpace(mainPath))
            {
                return null;
            }

            try
            {
                legacyPath = Path.Combine(Environment.ExpandEnvironmentVariables(mainPath.Trim()),
                    "appsettings.json");

                if (!File.Exists(legacyPath) ||
                    string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(currentConfigPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string json = JsonComments.Strip(File.ReadAllText(legacyPath, Encoding.UTF8));
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
                if (root == null)
                {
                    return null;
                }

                return GetString(root, "_databaseConnectionString")
                       ?? GetString(root, "DatabaseConnectionString");
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(Dictionary<string, object> root, params string[] path)
        {
            object current = root;
            foreach (string name in path)
            {
                Dictionary<string, object> dictionary = current as Dictionary<string, object>;
                if (dictionary == null)
                {
                    return null;
                }

                bool found = false;
                foreach (KeyValuePair<string, object> pair in dictionary)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        current = pair.Value;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return null;
                }
            }

            return current == null ? null : Convert.ToString(current);
        }

        public static string Find(string explicitPath, string rememberedPath)
        {
            string found = Existing(explicitPath);
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return found;
            }

            found = Existing(rememberedPath);
            if (found != null)
            {
                return found;
            }

            found = Existing(Environment.GetEnvironmentVariable("PARSEC_INTEGRATOR_CONFIG"));
            if (found != null)
            {
                return found;
            }

            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
            {
                found = Existing(Path.Combine(directory.FullName, "appsettings.json"));
                if (found != null)
                {
                    return found;
                }

                found = Existing(Path.Combine(directory.FullName, "ParsecIntegrationClient", "appsettings.json"));
                if (found != null)
                {
                    return found;
                }

                found = Existing(Path.Combine(directory.FullName, "publish", "appsettings.json"));
                if (found != null)
                {
                    return found;
                }
            }

            foreach (string serviceName in new[] { "ParsecIntegrationService", "ParsecIntegrationService2" })
            {
                string fromService = TryGetServiceConfig(serviceName);
                if (fromService != null)
                {
                    return fromService;
                }
            }

            Environment.SpecialFolder[] folders =
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            };
            foreach (Environment.SpecialFolder folder in folders)
            {
                found = Existing(Path.Combine(Environment.GetFolderPath(folder),
                    "ParsecIntegrationService", "appsettings.json"));
                if (found != null)
                {
                    return found;
                }
            }

            string drive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(drive))
            {
                found = Existing(Path.Combine(drive, "IntegrationClient", "appsettings.json"));
                if (found != null)
                {
                    return found;
                }

                found = Existing(Path.Combine(drive, "ArtonitServices", "wrk", "appsettings.json"));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string TryGetServiceConfig(string serviceName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                           @"SYSTEM\CurrentControlSet\Services\" + serviceName))
                {
                    string imagePath = key == null ? null : key.GetValue("ImagePath") as string;
                    if (string.IsNullOrWhiteSpace(imagePath))
                    {
                        return null;
                    }

                    string value = Environment.ExpandEnvironmentVariables(imagePath.Trim());
                    if (value.StartsWith(@"\??\", StringComparison.Ordinal))
                    {
                        value = value.Substring(4);
                    }

                    if (value.StartsWith("\"", StringComparison.Ordinal))
                    {
                        int quote = value.IndexOf('"', 1);
                        if (quote > 1)
                        {
                            value = value.Substring(1, quote - 1);
                        }
                    }
                    else
                    {
                        int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                        if (exe >= 0)
                        {
                            value = value.Substring(0, exe + 4);
                        }
                    }

                    string directory = Path.GetDirectoryName(value.Trim());
                    return string.IsNullOrWhiteSpace(directory)
                        ? null
                        : Existing(Path.Combine(directory, "appsettings.json"));
                }
            }
            catch
            {
                return null;
            }
        }

        private static string Existing(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
                return File.Exists(full) ? full : null;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class JsonComments
    {
        public static string Strip(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }

            StringBuilder output = new StringBuilder(json.Length);
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    output.Append(c);
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (c == '\\')
                    {
                        escape = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    output.Append(c);
                    continue;
                }

                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    i += 2;
                    while (i < json.Length && json[i] != '\r' && json[i] != '\n')
                    {
                        i++;
                    }

                    if (i < json.Length)
                    {
                        output.Append(json[i]);
                    }

                    continue;
                }

                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/'))
                    {
                        i++;
                    }

                    i++;
                    continue;
                }

                output.Append(c);
            }

            return output.ToString();
        }
    }
}
