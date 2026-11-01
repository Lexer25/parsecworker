using System.IO;
using Microsoft.Win32;

namespace ParsecQueueViewer.Services
{
    /// <summary>
    /// Ищет appsettings.json интегратора. Приоритет:
    /// --config, сохранённый в настройках монитора путь, переменная окружения,
    /// каталог рядом с монитором и его родители, путь службы из реестра,
    /// стандартные каталоги установки.
    /// </summary>
    public static class IntegratorConfigLocator
    {
        public const string EnvironmentVariable = "PARSEC_INTEGRATOR_CONFIG";

        public static string Find(string explicitPath, string rememberedPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return Existing(explicitPath);
            }

            string remembered = Existing(rememberedPath);
            if (remembered != null)
            {
                return remembered;
            }

            string fromEnvironment = Existing(Environment.GetEnvironmentVariable(EnvironmentVariable));
            if (fromEnvironment != null)
            {
                return fromEnvironment;
            }

            foreach (string candidate in EnumerateCandidates())
            {
                string path = Existing(candidate);
                if (path != null)
                {
                    return path;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            // Рядом с монитором и вверх по дереву: publish\QueueViewer -> publish,
            // ParsecQueueViewer\bin\Debug\net8.0-windows -> корень репозитория.
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
            {
                yield return Path.Combine(directory.FullName, "appsettings.json");
                yield return Path.Combine(directory.FullName, "ParsecIntegrationClient", "appsettings.json");
                yield return Path.Combine(directory.FullName, "publish", "appsettings.json");
            }

            // Реальная установка службы: берём каталог exe из ImagePath.
            foreach (string serviceName in new[] { "ParsecIntegrationService", "ParsecIntegrationService2" })
            {
                string serviceConfig = TryGetServiceConfigPath(serviceName);
                if (serviceConfig != null)
                {
                    yield return serviceConfig;
                }
            }

            foreach (string programFiles in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (!string.IsNullOrWhiteSpace(programFiles))
                {
                    yield return Path.Combine(programFiles, "ParsecIntegrationService", "appsettings.json");
                }
            }

            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(systemDrive))
            {
                yield return Path.Combine(systemDrive, "IntegrationClient", "appsettings.json");
                yield return Path.Combine(systemDrive, "ArtonitServices", "wrk", "appsettings.json");
            }
        }

        private static string TryGetServiceConfigPath(string serviceName)
        {
            try
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + serviceName);
                string imagePath = key?.GetValue("ImagePath") as string;
                string exePath = ExtractExecutablePath(imagePath);
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    return null;
                }

                return Path.Combine(Path.GetDirectoryName(exePath) ?? string.Empty, "appsettings.json");
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractExecutablePath(string imagePath)
        {
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
                int closingQuote = value.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    return value.Substring(1, closingQuote - 1);
                }
            }

            int exeEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEnd >= 0)
            {
                return value.Substring(0, exeEnd + 4).Trim();
            }

            return value.Trim();
        }

        private static string Existing(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
                return File.Exists(fullPath) ? fullPath : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
