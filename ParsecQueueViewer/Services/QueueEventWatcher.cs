using FirebirdSql.Data.FirebirdClient;

namespace ParsecQueueViewer.Services
{
    /// <summary>
    /// Слушатель события Firebird <see cref="EventName"/>: триггер
    /// PARSEC_QUEUE_EVENTS публикует его через POST_EVENT при любом изменении
    /// строки PARSEC_QUEUE. Каждое событие поднимает <see cref="Changed"/>,
    /// поэтому таблица перечитывается без опроса по таймеру.
    ///
    /// Подключение к базе и сокет событий живут в отдельном потоке и не занимают
    /// поток интерфейса. После обрыва связи слушатель переподключается
    /// с нарастающей паузой (2..30 секунд). События <see cref="Changed"/>
    /// и <see cref="StateChanged"/> приходят из этого фонового потока: подписчик
    /// сам переносит обработку в поток интерфейса.
    /// </summary>
    public sealed class QueueEventWatcher : IDisposable
    {
        /// <summary>Имя события. Должно совпадать с POST_EVENT в db/08_parsec_queue_events.sql.</summary>
        public const string EventName = "PARSEC_QUEUE_CHANGED";

        private const int FirstReconnectDelayMs = 2000;
        private const int MaxReconnectDelayMs = 30000;

        private readonly string _connectionString;
        private readonly Thread _thread;
        private readonly ManualResetEvent _stop = new(false);
        private readonly ManualResetEvent _fault = new(false);

        private volatile bool _connected;
        private volatile string _lastError;
        private volatile bool _disposed;
        private int _started;

        public QueueEventWatcher(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Не задана строка подключения к базе.", nameof(connectionString));
            }

            _connectionString = connectionString;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "PARSEC_QUEUE events"
            };
        }

        /// <summary>Изменение очереди: пришло событие PARSEC_QUEUE_CHANGED.</summary>
        public event EventHandler Changed;

        /// <summary>Изменилось состояние подписки (подключились или потеряли связь).</summary>
        public event EventHandler StateChanged;

        /// <summary>Подписка на событие активна.</summary>
        public bool Connected => _connected;

        /// <summary>Последняя ошибка подключения к событиям (null, если её не было).</summary>
        public string LastError => _lastError;

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 0)
            {
                _thread.Start();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stop.Set();
            _fault.Set();
            try
            {
                if (_thread.IsAlive)
                {
                    _thread.Join(1500);
                }
            }
            catch
            {
            }
        }

        private void Run()
        {
            int delay = FirstReconnectDelayMs;
            while (!_stop.WaitOne(0))
            {
                FbRemoteEvent remoteEvent = null;
                try
                {
                    remoteEvent = new FbRemoteEvent(_connectionString);
                    remoteEvent.RemoteEventCounts += OnRemoteEventCounts;
                    remoteEvent.RemoteEventError += OnRemoteEventError;
                    remoteEvent.Open();
                    if (_stop.WaitOne(0))
                    {
                        break;
                    }

                    remoteEvent.QueueEvents(new[] { EventName });
                    delay = FirstReconnectDelayMs;
                    SetConnected(true, null);

                    // События принимает собственный поток провайдера; здесь ждём
                    // только команды «закрыть» или сообщения об обрыве связи.
                    WaitHandle.WaitAny(new WaitHandle[] { _stop, _fault });
                    _fault.Reset();
                }
                catch (Exception ex)
                {
                    SetConnected(false, Describe(ex));
                }
                finally
                {
                    _connected = false;
                    if (remoteEvent != null)
                    {
                        try
                        {
                            remoteEvent.CancelEvents();
                        }
                        catch
                        {
                        }

                        try
                        {
                            remoteEvent.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }

                if (_stop.WaitOne(delay))
                {
                    break;
                }

                delay = Math.Min(delay * 2, MaxReconnectDelayMs);
            }

            _connected = false;
        }

        private void OnRemoteEventCounts(object sender, FbRemoteEventCountsEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            // Содержимое (counts) не нужно: важен сам факт изменения таблицы.
            Raise(Changed);
        }

        private void OnRemoteEventError(object sender, FbRemoteEventErrorEventArgs e)
        {
            SetConnected(false, e.Error == null ? "события недоступны" : Describe(e.Error));
            _fault.Set();
        }

        private void SetConnected(bool connected, string error)
        {
            bool changed = _connected != connected ||
                           !string.Equals(_lastError, error, StringComparison.Ordinal);

            _connected = connected;
            _lastError = error;

            if (changed && !_disposed)
            {
                Raise(StateChanged);
            }
        }

        private void Raise(EventHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // Ошибка подписчика не должна останавливать слушатель событий.
            }
        }

        private static string Describe(Exception error)
        {
            if (error == null)
            {
                return "неизвестная ошибка";
            }

            string message = error.Message;
            return string.IsNullOrWhiteSpace(message) ? error.GetType().Name : message;
        }
    }
}
