using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Models;
using ParsecIntegrationClient.Options;
using ParsecIntegrationClient.Services;

namespace ParsecIntegrationClient
{
    /// <summary>
    /// Продление сессии Парсека (раньше — задание ContinueSessionJob в Quartz с интервалом 4 минуты).
    /// Если продлить не удалось, сессия сбрасывается и Worker открывает её заново.
    /// </summary>
    public sealed class SessionKeepAliveWorker : BackgroundService
    {
        private readonly ILogger<SessionKeepAliveWorker> _logger;
        private readonly WorkerOptions _options;

        public SessionKeepAliveWorker(ILogger<SessionKeepAliveWorker> logger, WorkerOptions options)
        {
            _logger = logger;
            _options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            TimeSpan interval = TimeSpan.FromSeconds(Math.Max(30, _options.Parsec.SessionContinueSeconds));
            _logger.LogDebug("Продление сессии Парсека каждые {Seconds} с", (int)interval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                Guid session = ClientState.SessionID;
                if (session == Guid.Empty)
                {
                    _logger.LogDebug("Сессия Парсека ещё не открыта — продление пропущено");
                    continue;
                }

                try
                {
                    using var client = ParsecClient.Create();
                    int result = client.ContinueSession(session);

                    if (result == ClientState.Result_Success)
                    {
                        _logger.LogDebug("Сессия Парсека продлена: {Session}", session);
                    }
                    else
                    {
                        _logger.LogWarning("Продление сессии Парсека вернуло код {Code}; сессия будет открыта заново", result);
                        ClientState.ResetSession();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось продлить сессию Парсека; сессия будет открыта заново");
                    ClientState.ResetSession();
                }
            }
        }
    }
}
