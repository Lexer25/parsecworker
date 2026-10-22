using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Options;
using ParsecIntegrationClient.Services;

namespace ParsecIntegrationClient
{
    /// <summary>
    /// Основной цикл службы: авторизация в Парсеке и обработка очереди cardindev
    /// с интервалом Service:JobIntervalSeconds.
    ///
    /// Раньше это делал Quartz: два задания (MainJob каждые 600 с и ContinueSessionJob
    /// каждые 4 минуты) поднимались вручную в Program/Service1. Здесь вместо планировщика
    /// два обычных BackgroundService — Worker и SessionKeepAliveWorker.
    /// </summary>
    public sealed class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly WorkerOptions _options;
        private readonly IHostApplicationLifetime _lifetime;

        public Worker(ILogger<Worker> logger, WorkerOptions options, IHostApplicationLifetime lifetime)
        {
            _logger = logger;
            _options = options;
            _lifetime = lifetime;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogCritical(
                "Старт Worker: режим {Mode}; адрес Парсека {Url}; интервал обработки {Interval} с; продление сессии {KeepAlive} с",
                WindowsServiceHelpers.IsWindowsService() ? "служба Windows" : "консоль",
                SettingsService.ParsecUrl,
                SettingsService.DatabaseJobTimeout,
                _options.Parsec.SessionContinueSeconds);

            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Возвращаем управление хосту сразу: иначе первая обработка очереди задержала бы
            // старт службы (диспетчер служб ждёт OnStart не дольше 30 секунд).
            await Task.Yield();

            string pathError;
            if (!Paths.EnsureCreated(out pathError))
            {
                _logger.LogCritical("Рабочий каталог недоступен ({Error}). Работа прекращена.", pathError);
                _lifetime.StopApplication();
                return;
            }

            SettingsService.Update();
            LogSettings();

            if (SettingsService.LegacyKeysUsed.Count > 0)
            {
                _logger.LogWarning(
                    "Настройки взяты из устаревшего файла {File} (ключи: {Keys}). Перенесите значения в appsettings.json рядом с exe и удалите старый файл.",
                    Paths.LegacySettingsFilePath,
                    string.Join(", ", SettingsService.LegacyKeysUsed));
            }

            // Проверка лицензии Guardant убрана: служба работает в полном режиме.
            License.SetLicenseStatus(true);

            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, SettingsService.DatabaseJobTimeout));
            TimeSpan retry = TimeSpan.FromSeconds(Math.Max(5, _options.Parsec.SessionRetrySeconds));
            TimeSpan databaseRetry = TimeSpan.FromSeconds(Math.Max(5, _options.Service.DatabaseRetrySeconds));

            while (!stoppingToken.IsCancellationRequested)
            {
                // Обе зависимости проверяются независимо: проблема с БД не должна скрывать
                // состояние подключения к Парсеку (и наоборот).
                bool databaseReady = CheckDatabase((int)databaseRetry.TotalSeconds);

                if (ClientState.SessionID == Guid.Empty && !TryOpenSession())
                {
                    _logger.LogWarning("Повторная авторизация в Парсеке через {Seconds} с", (int)retry.TotalSeconds);
                    await Delay(retry, stoppingToken);
                    continue;
                }

                if (!databaseReady)
                {
                    _logger.LogWarning(
                        "Очередь не обрабатывается: нет подключения к базе данных. Повтор через {Seconds} с.",
                        (int)databaseRetry.TotalSeconds);
                    await Delay(databaseRetry, stoppingToken);
                    continue;
                }

                try
                {
                    new QueueProcessor().RunOnce();
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Ошибка обработки очереди заданий");
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogInformation(
                    "Следующая обработка через {Interval} (в {Next:dd.MM.yyyy HH:mm:ss})",
                    interval,
                    DateTime.Now.Add(interval));

                await Delay(interval, stoppingToken);
            }

            CloseSession();
            _logger.LogCritical("Worker остановлен");
        }

        private bool CheckDatabase(int databaseRetrySeconds)
        {
            string error;
            if (DatabaseService.CheckConnection(out error))
            {
                _logger.LogInformation("Подключение к базе данных Артонит установлено");
                return true;
            }

            _logger.LogCritical(
                "Нет подключения к базе данных Артонит: {Error}. Строка подключения: {Source}. Обработка очереди не выполняется, повтор через {Seconds} с.",
                error,
                SettingsService.DatabaseConnectionStringSource,
                (int)databaseRetrySeconds);

            return false;
        }

        private bool TryOpenSession()
        {
            try
            {
                var client = ParsecClient.Create();
                var result = client.OpenSession(_options.Parsec.Domain, _options.Parsec.User, _options.Parsec.Password);

                if (result == null || result.Result != ClientState.Result_Success)
                {
                    _logger.LogError(
                        "Авторизация в Парсеке не удалась: {Error}",
                        result == null ? "нет ответа от сервиса" : result.ErrorMessage);
                    return false;
                }

                ClientState.SetSession(result.Value, _options.Parsec.Domain, _options.Parsec.User);

                // Уровень Warning выбран сознательно: подключение к SOAP должно быть видно
                // при уровне журнала по умолчанию (Log:Level = Warning).
                _logger.LogWarning(
                    "Подключение к SOAP Парсека установлено: {Url}; протокол SOAP: {Protocol}; сессия: {Session}",
                    SettingsService.ParsecUrl,
                    ParsecClient.ProtocolName(client.SoapVersion),
                    ClientState.SessionID);

                try
                {
                    string version = client.GetVersion();

                    _logger.LogWarning(
                        "Версия SOAP-сервиса Парсека: {Version}",
                        string.IsNullOrWhiteSpace(version) ? "(сервис не вернул версию)" : version);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Не удалось запросить версию SOAP-сервиса Парсека (GetVersion): {Error}", ex.Message);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось открыть сессию Парсека ({Url})", SettingsService.ParsecUrl);
                return false;
            }
        }

        private void CloseSession()
        {
            Guid session = ClientState.SessionID;
            if (session == Guid.Empty)
            {
                return;
            }

            try
            {
                ParsecClient.Create().CloseSession(session);
                _logger.LogInformation("Сессия Парсека закрыта");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось закрыть сессию Парсека");
            }

            ClientState.ResetSession();
        }

        private void LogSettings()
        {
            _logger.LogInformation("Каталог службы: {MainPath}; журнал: {LogFolder}", Paths.MainPath, Paths.LogFolder);
            _logger.LogInformation("База данных Артонит: {ConnectionString} (источник: {Source})", MaskPassword(SettingsService.DatabaseConnectionString), SettingsService.DatabaseConnectionStringSource);
            _logger.LogInformation(
                "Интервал обработки: {Interval} с; таймаут ошибок: {ErrorTimeout} мин; пропускаемые коды ошибок: {SkipCodes}",
                SettingsService.DatabaseJobTimeout,
                SettingsService.ErrorTimeoutMinutes,
                SettingsService.SkipErrCode == null || SettingsService.SkipErrCode.Count == 0
                    ? "(нет)"
                    : string.Join(", ", SettingsService.SkipErrCode));
        }

        // Пароль из строки подключения не должен попадать в журнал.
        private static string MaskPassword(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return "(не задано)";
            }

            return Regex.Replace(connectionString, "(?i)(password\\s*=\\s*)([^;]*)", "$1***");
        }

        private static async Task Delay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                // Штатная остановка службы.
            }
        }
    }
}




