using System.IO;
using System.Text.Json;
using ParsecQueueViewer.Models;

namespace ParsecQueueViewer.Services
{
    /// <summary>
    /// Настройки самого монитора (последний выбранный appsettings.json, автообновление).
    /// Хранятся в %LOCALAPPDATA%, чтобы не менять настройки службы интегратора.
    /// </summary>
    public sealed class ViewerSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _path;

        public ViewerSettingsStore()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ParsecQueueViewer");
            _path = Path.Combine(folder, "settings.json");
        }

        public string SettingsPath => _path;

        public ViewerSettings Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new ViewerSettings();
                }

                ViewerSettings settings = JsonSerializer.Deserialize<ViewerSettings>(
                    File.ReadAllText(_path), JsonOptions);

                if (settings == null)
                {
                    return new ViewerSettings();
                }

                if (settings.RefreshSeconds < 5 || settings.RefreshSeconds > 3600)
                {
                    settings.RefreshSeconds = 60;
                }

                if (settings.HistoryLimit < 100 || settings.HistoryLimit > 5000)
                {
                    settings.HistoryLimit = 1000;
                }

                return settings;
            }
            catch
            {
                return new ViewerSettings();
            }
        }

        public void Save(ViewerSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            try
            {
                string folder = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporary, _path, true);
            }
            catch
            {
                // Настройки монитора не критичны: при ошибке используем значения по умолчанию.
            }
        }
    }
}
