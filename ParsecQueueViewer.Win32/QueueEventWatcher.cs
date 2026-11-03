using System;
using System.Threading;
using FirebirdSql.Data.FirebirdClient;

namespace ParsecQueueViewer.Win32
{
    /// <summary>
    /// Слушатель события Firebird <see cref="EventName"/>: триггер
    /// PARSEC_QUEUE_EVENTS публикует его через POST_EVENT при любом изменении
    /// строки PARSEC_QUEUE. Каждое событие превращается в сообщение окну,
    /// поэтому таблица перечитывается без опроса по таймеру.
    ///
    /// Работает в отдельном потоке: подключение к базе и сокет событий не
    /// должны занимать поток интерфейса. После обрыва связи переподключается
    /// с нарастающей паузой (2..30 секунд).
    /// </summary>
    internal sealed class QueueEventWatcher : IDisposable
    {
        /// <summary>Имя события. Должно совпадать с POST_EVENT в db/08_parsec_queue_events.sql.</summary>
        public const string EventName = "PARSEC_QUEUE_CHANGED";

        private const int FirstReconnectDelayMs = 2000;
        private const int MaxReconnectDelayMs = 30000;

        private readonly string _connectionString;
        private readonly IntPtr _hwnd;
        private readonly Thread _thread;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly ManualResetEvent _fault = new ManualResetEvent(false);

        private volatile bool _connected;
        private volatile string _lastError;
        private volatile bool _disposed;

        public QueueEventWatcher(string connectionString, IntPtr hwnd)
        {
            _connectionString = connectionString;
            _hwnd = hwnd;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "PARSEC_QUEUE events"
            };
        }

        /// <summary>Подписка на событие активна.</summary>
        public bool Connected
        {
            get { return _connected; }
        }

        /// <summary>Последняя ошибка подключения к событиям (null, если её не было).</summary>
        public string LastError
        {
            get { return _lastError; }
        }

        public void Start()
        {
            _thread.Start();
        }

        public void Dispose()
        {
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
            if (_disposed || _hwnd == IntPtr.Zero)
            {
                return;
            }

            // Содержимое (counts) не нужно: важен сам факт изменения таблицы.
            Native.PostMessage(_hwnd, Native.WM_QUEUE_EVENT, IntPtr.Zero, IntPtr.Zero);
        }

        private void OnRemoteEventError(object sender, FbRemoteEventErrorEventArgs e)
        {
            SetConnected(false, e.Error == null ? "события недоступны" : Describe(e.Error));
            _fault.Set();
        }

        private void SetConnected(bool connected, string error)
        {
            _connected = connected;
            _lastError = error;
            if (!_disposed && _hwnd != IntPtr.Zero)
            {
                Native.PostMessage(_hwnd, Native.WM_QUEUE_STATE, IntPtr.Zero, IntPtr.Zero);
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
