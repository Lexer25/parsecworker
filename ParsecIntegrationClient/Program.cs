using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Options;
using ParsecIntegrationClient.Services;
using ParsecIntegrationClient.Services.Logging;

namespace ParsecIntegrationClient
{
    /// <summary>
    /// Точка входа. Один и тот же exe запускается и из консоли, и как служба Windows:
    /// режим определяется автоматически (UseWindowsService), ключ /console больше не нужен.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Зависимости встроены в exe, поэтому загрузчик подключается до первой строки,
            // работающей с типами Microsoft.Extensions.* (JIT ищет их при компиляции метода).
            EmbeddedAssemblies.Register();

            if (Array.Exists(args, argument => string.Equals(argument, "--verify-embedded", StringComparison.OrdinalIgnoreCase)))
            {
                return VerifyEmbedded();
            }

            return Run(args);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            IHost host = null;

            try
            {
                host = BuildHost(args);
                host.Run();
                return 0;
            }
            catch (Exception ex)
            {
                ReportFatal(ex);
                return 1;
            }
            finally
            {
                if (host != null)
                {
                    host.Dispose();
                }
            }
        }

        /// <summary>
        /// Проверка поставки из одного файла: все ли встроенные сборки загружаются.
        /// Запуск: ParsecIntegrationClient.exe --verify-embedded
        /// </summary>
        private static int VerifyEmbedded()
        {
            Console.WriteLine("Версия: " + ApplicationInfo.Version);
            Console.WriteLine("Файл: " + ApplicationInfo.ExecutablePath);
            Console.WriteLine("Встроенные сборки:");

            List<string> failures = EmbeddedAssemblies.Verify();

            if (failures.Count == 0)
            {
                Console.WriteLine("ИТОГ: все встроенные сборки загружены, поставка из одного файла работоспособна.");
                return 0;
            }

            Console.WriteLine("ИТОГ: ошибок " + failures.Count + ".");
            return 1;
        }

        private static IHost BuildHost(string[] args)
        {
            // Конфигурация собирается до хоста: каталог данных, уровень журнала и адрес
            // Парсека нужны раньше, чем появится само приложение.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables("PARSEC_")
                .AddCommandLine(args)
                .Build();

            WorkerOptions options = WorkerOptions.Bind(configuration);

            Paths.Initialize(options);
            SettingsService.Initialize(options);
            ParsecClient.Configure(SettingsService.ParsecUrl);

            LogLevel applicationLevel = Logger.ParseLevel(SettingsService.LogLevel);
            LogLevel frameworkLevel = Logger.ParseLevel(options.Log.FrameworkLevel);
            bool windowsService = WindowsServiceHelpers.IsWindowsService();

            IHost host = Host.CreateDefaultBuilder(args)
                .UseContentRoot(AppContext.BaseDirectory)
                .UseWindowsService()
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Единственный источник настроек — собранная выше конфигурация,
                    // поэтому appsettings.json не читается повторно из другого каталога.
                    config.Sources.Clear();
                    config.AddConfiguration(configuration);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);

                    logging.AddSimpleConsole(console =>
                    {
                        console.SingleLine = true;
                        console.TimestampFormat = "dd.MM.yyyy HH:mm:ss ";
                    });

                    // Свои категории (имена классов: Worker, QueueProcessor, ParsecService, License, ...)
                    // фильтруются по Log:Level, служебные Microsoft.*/System.* — по Log:FrameworkLevel.
                    logging.AddFilter((category, level) =>
                        level >= (IsFrameworkCategory(category) ? frameworkLevel : applicationLevel));

                    // Файловый журнал сохраняет прежний формат строки и имя файла Log_At_ГГГГ_М_Д.txt.
                    logging.AddProvider(new LegacyFileLoggerProvider(Paths.LogFolder, options.Log.RetainedFileCount));
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton(options);
                    services.AddHostedService<Worker>();
                    services.AddHostedService<SessionKeepAliveWorker>();
                })
                .Build();

            Logger.Initialize(host.Services.GetRequiredService<ILoggerFactory>());

            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
            DateTime startedAt = DateTime.Now;

            // Старт и остановка службы пишутся отдельно от строк Worker: Worker может вообще
            // не дойти до первой итерации (нет БД, нет Парсека), а факт запуска должен остаться в журнале.
            logger.LogCritical(
                "Запуск ParsecIntegrationClient {Version}; режим: {Mode}; файл: {Exe}; время: {Started:dd.MM.yyyy HH:mm:ss}",
                ApplicationInfo.Version,
                windowsService ? "служба Windows" : "консоль",
                ApplicationInfo.ExecutablePath,
                startedAt);

            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
            {
                TimeSpan uptime = DateTime.Now - startedAt;
                logger.LogCritical(
                    "Остановка ParsecIntegrationClient; время работы: {Uptime}; время: {Stopped:dd.MM.yyyy HH:mm:ss}",
                    string.Format("{0:00}:{1:00}:{2:00}", (int)uptime.TotalHours, uptime.Minutes, uptime.Seconds),
                    DateTime.Now);
            });

            return host;
        }

        private static bool IsFrameworkCategory(string category)
        {
            if (string.IsNullOrEmpty(category))
            {
                return false;
            }

            return category.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                   category.StartsWith("System", StringComparison.OrdinalIgnoreCase);
        }

        // Ошибка до запуска хоста: приёмников журнала ещё нет, поэтому пишем напрямую в файл и в консоль.
        private static void ReportFatal(Exception ex)
        {
            string message = "Запуск ParsecIntegrationClient прерван: " + ex;

            try
            {
                LegacyFileLoggerProvider.Append(Paths.LogFolder, "Program", LogLevel.Critical, message, ex);
            }
            catch
            {
            }

            try
            {
                Console.Error.WriteLine(message);
            }
            catch
            {
            }
        }
    }
}


