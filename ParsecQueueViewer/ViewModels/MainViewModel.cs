using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Threading;
using ParsecQueueViewer.Infrastructure;
using ParsecQueueViewer.Models;
using ParsecQueueViewer.Services;

namespace ParsecQueueViewer.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private static readonly Regex PasswordRegex = new(
            @"(?i)\b(password|pwd|пароль)\s*=\s*[^;]*",
            RegexOptions.Compiled);

        private readonly ViewerSettingsStore _settingsStore;
        private readonly QueueRepository _repository = new();
        private readonly ViewerSettings _settings;
        private readonly string _explicitConfigPath;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _eventTimer;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);

        private IntegratorConfiguration _configuration;
        private CancellationTokenSource _refreshCts;
        private CancellationTokenSource _detailsCts;
        private long? _detailsQueueId;
        private QueueFilterOption _selectedFilter;
        private QueueRowViewModel _selectedRow;
        private string _searchText = string.Empty;
        private bool _autoRefresh;
        private int _refreshSeconds;
        private bool _isBusy;
        private bool _isInitialized;
        private bool _disposed;
        private bool _isConnected;
        private bool _hasConnectionError;
        private bool _hasConfigurationError;
        private int _visibleCount;
        private int _loadedCount;
        private int _totalCount;
        private int _pendingCount;
        private int _inWorkCount;
        private int _retryCount;
        private int _deadCount;
        private int _successCount;
        private DateTime? _lastUpdated;
        private string _connectionErrorText;
        private string _configurationErrorText;
        private string _configPath;
        private string _configFileDisplay = "appsettings.json не выбран";
        private string _connectionDisplay = "Подключение не настроено";
        private string _headerSubtitle = "Очередь PARSEC_QUEUE · только чтение";
        private string _emptyMessage = "Нет данных";
        private string _historyStatusText = "Выберите событие в таблице";
        private string _loadLimitText;
        private bool _hasLimitNote;
        private int _historyCount;
        private bool _hasHistory;
        private string _visibleCountText = "0 из 0";
        private string _loadedCountText = "загружено 0";

        private QueueEventWatcher _watcher;
        private string _refreshSource = "запуск";
        private string _lastRefreshSource = "запуск";
        private string _eventsWarningText;
        private bool _hasEventsWarning;
        private bool _eventListenerConnected;
        private bool _eventTriggerPresent = true;

        public MainViewModel(string explicitConfigPath)
        {
            _explicitConfigPath = explicitConfigPath;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _settingsStore = new ViewerSettingsStore();
            _settings = _settingsStore.Load();

            _autoRefresh = _settings.AutoRefresh;
            _refreshSeconds = _settings.RefreshSeconds;
            HistoryLimit = _settings.HistoryLimit;

            Rows = new ObservableCollection<QueueRowViewModel>();
            History = new ObservableCollection<QueueLogViewModel>();
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = FilterRow;

            Filters = new ObservableCollection<QueueFilterOption>
            {
                new(QueueFilterKind.Active, "Активные", "Новые, в работе, повторы и ошибки"),
                new(QueueFilterKind.Pending, "Ожидают", "Новые события, ещё не взятые в работу"),
                new(QueueFilterKind.InWork, "В работе", "События, забранные интегратором"),
                new(QueueFilterKind.Problems, "Ошибки", "Повторы и события с исчерпанными попытками"),
                new(QueueFilterKind.History, "История", "Успешные, пропущенные и отменённые"),
                new(QueueFilterKind.All, "Все", "Все загруженные события")
            };
            _selectedFilter = Filters[0];

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(_refreshSeconds)
            };
            _timer.Tick += OnTimerTick;

            // Одно изменение таблицы часто порождает пачку событий (служба правит
            // несколько строк подряд). Перечитываем очередь один раз через 250 мс
            // после последнего события.
            _eventTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _eventTimer.Tick += OnEventTimerTick;

            RefreshCommand = new AsyncRelayCommand(
                () => RefreshAsync("кнопка"),
                () => _configuration != null && !_isBusy);

            ApplySort();
        }

        public ObservableCollection<QueueRowViewModel> Rows { get; }
        public ICollectionView RowsView { get; }
        public ObservableCollection<QueueLogViewModel> History { get; }
        public ObservableCollection<QueueFilterOption> Filters { get; }
        public IReadOnlyList<int> RefreshIntervals { get; } = new[] { 5, 10, 30, 60 };
        public AsyncRelayCommand RefreshCommand { get; }
        public int HistoryLimit { get; }
        public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        public QueueFilterOption SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (SetProperty(ref _selectedFilter, value))
                {
                    ApplySort();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    ApplyFilter();
                }
            }
        }

        public bool AutoRefresh
        {
            get => _autoRefresh;
            set
            {
                if (SetProperty(ref _autoRefresh, value))
                {
                    RestartTimer();
                    // От галочки зависит текст предупреждения и режим в шапке.
                    UpdateEventsState();
                }
            }
        }

        public int RefreshSeconds
        {
            get => _refreshSeconds;
            set
            {
                int normalized = value is >= 5 and <= 3600 ? value : 60;
                if (SetProperty(ref _refreshSeconds, normalized))
                {
                    RestartTimer();
                }
            }
        }

        public QueueRowViewModel SelectedRow
        {
            get => _selectedRow;
            set
            {
                long? previousId = _selectedRow?.Id;
                int previousAttempts = _selectedRow?.Attempts ?? -1;

                if (SetProperty(ref _selectedRow, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    if (value == null)
                    {
                        ClearHistory("Выберите событие в таблице");
                    }
                    else if (previousId != value.Id || previousAttempts != value.Attempts ||
                             _detailsQueueId != value.Id)
                    {
                        // При обновлении таблицы выбранная строка заменяется новым объектом.
                        // Если это то же событие и число попыток не изменилось, историю
                        // не перезагружаем, чтобы панель не мигала каждые 10 секунд.
                        _ = LoadHistoryForSelectionAsync(value);
                    }
                }
            }
        }

        public bool HasSelection => _selectedRow != null;
        public bool IsEmpty => _visibleCount == 0;
        public bool IsBusy => _isBusy;
        public bool IsConnected => _isConnected;
        public bool HasConnectionError => _hasConnectionError;
        public bool HasConfigurationError => _hasConfigurationError;

        /// <summary>
        /// Очередь обновляется по событию Firebird: подписка активна и триггер
        /// PARSEC_QUEUE_EVENTS в базе есть.
        /// </summary>
        public bool IsEventsOnline =>
            _isConnected && _eventListenerConnected && _eventTriggerPresent;

        public bool HasEventsWarning => _hasEventsWarning;
        public string EventsWarningText => _eventsWarningText;

        public string ConnectionStatusText =>
            _hasConfigurationError ? "Нет настроек" :
            _hasConnectionError ? "Ошибка" :
            !_isConnected ? "Ожидание" :
            IsEventsOnline ? "Онлайн" :
            _autoRefresh ? "Опрос" : "Нет событий";

        public string EventsModeTooltip =>
            _hasConfigurationError ? "Параметры подключения не загружены" :
            _hasConnectionError ? "Нет связи с базой" :
            !_isConnected ? "Очередь ещё не прочитана" :
            IsEventsOnline
                ? "Подписка на событие Firebird " + QueueEventWatcher.EventName +
                  " активна: очередь перечитывается сразу после изменения таблицы"
                : _autoRefresh
                    ? "Событие Firebird недоступно: очередь обновляется резервным опросом"
                    : "Событие Firebird недоступно, а резервный опрос выключен — данные не обновляются";

        public string ConfigPath => _configPath;
        public string ConfigFileDisplay => _configFileDisplay;
        public string ConnectionDisplay => _connectionDisplay;
        public string HeaderSubtitle => _headerSubtitle;
        public string ConnectionErrorText => _connectionErrorText;
        public string ConfigurationErrorText => _configurationErrorText;
        public string EmptyMessage => _emptyMessage;
        public string HistoryStatusText => _historyStatusText;
        public int VisibleCount => _visibleCount;
        public int LoadedCount => _loadedCount;
        public int TotalCount => _totalCount;
        public int PendingCount => _pendingCount;
        public int InWorkCount => _inWorkCount;
        public int ProblemsCount => _retryCount + _deadCount;
        public int RetryCount => _retryCount;
        public int DeadCount => _deadCount;
        public int SuccessCount => _successCount;
        public int HistoryCount => _historyCount;
        public bool HasHistory => _hasHistory;
        public string VisibleCountText => _visibleCountText;
        public string LoadedCountText => _loadedCountText;
        public string LoadLimitText => _loadLimitText;
        public bool HasLimitNote => _hasLimitNote;

        public string ProblemsDetail =>
            "повтор " + _retryCount.ToString("N0", Ru) + " · ошибок " + _deadCount.ToString("N0", Ru);

        public string SuccessDetail =>
            "всего успешных " + _successCount.ToString("N0", Ru);

        public string LastUpdatedText
        {
            get
            {
                if (!_lastUpdated.HasValue)
                {
                    return "ещё не обновлялось";
                }

                return "обновлено " + _lastUpdated.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                       " · " + _lastRefreshSource;
            }
        }

        public string ConfigTooltip =>
            string.IsNullOrWhiteSpace(_configPath)
                ? "Укажите appsettings.json рядом с ParsecIntegrationClient.exe"
                : _configPath;

        public string ConnectionTooltip =>
            string.IsNullOrWhiteSpace(_connectionDisplay)
                ? "Параметры подключения не загружены"
                : _connectionDisplay + "\nИсточник: " + (_configuration?.ConnectionSource ?? "—");

        public async Task InitializeAsync()
        {
            string explicitPath = NormalizeOptionalPath(_explicitConfigPath);
            if (!string.IsNullOrWhiteSpace(_explicitConfigPath) && explicitPath == null)
            {
                ApplyConfigError(
                    "Файл, указанный в --config, не найден: " + _explicitConfigPath +
                    ". Укажите appsettings.json интегратора кнопкой «Выбрать».");
                CompleteInitialization();
                return;
            }

            string found = IntegratorConfigLocator.Find(explicitPath, _settings.ConfigPath);
            if (string.IsNullOrWhiteSpace(found))
            {
                ApplyConfigError(
                    "Не найден appsettings.json интегратора. Нажмите «Выбрать» и укажите файл " +
                    "рядом с ParsecIntegrationClient.exe (обычно C:\\Program Files\\ParsecIntegrationService\\appsettings.json).");
                CompleteInitialization();
                return;
            }

            await LoadConfigurationAsync(found);
            CompleteInitialization();
        }

        public async Task LoadConfigurationAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            IntegratorConfiguration loaded;
            try
            {
                loaded = IntegratorConfiguration.Load(path);
            }
            catch (Exception ex)
            {
                ApplyConfigError("Не удалось загрузить настройки: " + Sanitize(ex.Message));
                return;
            }

            _refreshCts?.Cancel();
            _detailsCts?.Cancel();
            StopEvents();
            _eventTriggerPresent = true;
            _eventListenerConnected = false;

            _configuration = loaded;
            _settings.ConfigPath = loaded.ConfigPath;
            _settingsStore.Save(_settings);

            _hasConfigurationError = false;
            _configurationErrorText = null;
            _configPath = loaded.ConfigPath;
            _configFileDisplay = BuildConfigFileDisplay(loaded.ConfigPath);
            _connectionDisplay = loaded.ConnectionDisplay;
            _headerSubtitle = "Очередь PARSEC_QUEUE · " + loaded.ConnectionDisplay + " · только чтение";
            _hasConnectionError = false;
            _connectionErrorText = null;
            _isConnected = false;

            OnPropertyChanged(nameof(HasConfigurationError));
            OnPropertyChanged(nameof(ConfigurationErrorText));
            OnPropertyChanged(nameof(ConfigPath));
            OnPropertyChanged(nameof(ConfigFileDisplay));
            OnPropertyChanged(nameof(ConfigTooltip));
            OnPropertyChanged(nameof(ConnectionDisplay));
            OnPropertyChanged(nameof(ConnectionTooltip));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(HasConnectionError));
            OnPropertyChanged(nameof(ConnectionErrorText));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));

            ClearSnapshot();
            RefreshCommand.RaiseCanExecuteChanged();
            StartEvents();
            await RefreshAsync("запуск");
        }

        /// <summary>
        /// Перечитать очередь. <paramref name="source"/> — что вызвало обновление
        /// (запуск, кнопка, F5, таймер, событие); значение видно в нижней строке окна.
        /// </summary>
        public async Task RefreshAsync(string source = null)
        {
            await _refreshGate.WaitAsync();
            CancellationTokenSource cts = new();
            _refreshCts = cts;

            if (!string.IsNullOrWhiteSpace(source))
            {
                _refreshSource = source;
            }

            try
            {
                IsBusySet(true);

                IntegratorConfiguration configuration = _configuration;
                if (configuration == null)
                {
                    return;
                }

                QueueSnapshot snapshot = await _repository.LoadAsync(configuration, HistoryLimit, cts.Token);
                if (cts.IsCancellationRequested || _disposed || !ReferenceEquals(configuration, _configuration))
                {
                    return;
                }

                ApplySnapshot(snapshot);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested && !_disposed)
                {
                    _isConnected = false;
                    _hasConnectionError = true;
                    _connectionErrorText = BuildDatabaseErrorMessage(ex.Message);
                    _emptyMessage = _loadedCount == 0
                        ? "Нет данных: не удалось подключиться к базе"
                        : _emptyMessage;
                    OnPropertyChanged(nameof(EmptyMessage));
                    OnPropertyChanged(nameof(IsConnected));
                    OnPropertyChanged(nameof(HasConnectionError));
                    OnPropertyChanged(nameof(ConnectionErrorText));
                    UpdateEventsState();
                }
            }
            finally
            {
                if (ReferenceEquals(_refreshCts, cts))
                {
                    _refreshCts = null;
                }

                cts.Dispose();
                IsBusySet(false);
                _refreshGate.Release();
            }
        }

        private void ApplySnapshot(QueueSnapshot snapshot)
        {
            long? selectedId = _selectedRow?.Id;

            Rows.Clear();
            foreach (QueueRow row in snapshot.Rows)
            {
                Rows.Add(new QueueRowViewModel(row));
            }

            _loadedCount = Rows.Count;
            _totalCount = snapshot.TotalCount;
            _pendingCount = Count(snapshot, 0);
            _inWorkCount = Count(snapshot, 1);
            _successCount = Count(snapshot, 2);
            _retryCount = Count(snapshot, 3);
            _deadCount = Count(snapshot, 4);
            _eventTriggerPresent = snapshot.EventTriggerPresent;

            Filters.First(option => option.Kind == QueueFilterKind.Active).Count =
                _pendingCount + _inWorkCount + _retryCount + _deadCount;
            Filters.First(option => option.Kind == QueueFilterKind.Pending).Count = _pendingCount;
            Filters.First(option => option.Kind == QueueFilterKind.InWork).Count = _inWorkCount;
            Filters.First(option => option.Kind == QueueFilterKind.Problems).Count = _retryCount + _deadCount;
            Filters.First(option => option.Kind == QueueFilterKind.History).Count =
                Count(snapshot, 2) + Count(snapshot, 5) + Count(snapshot, 6);
            Filters.First(option => option.Kind == QueueFilterKind.All).Count = _totalCount;

            _lastUpdated = snapshot.LoadedAt;
            _lastRefreshSource = _refreshSource;
            _isConnected = true;
            _hasConnectionError = false;
            _connectionErrorText = null;

            BuildLimitNote(snapshot);

            OnPropertyChanged(nameof(LoadedCount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(InWorkCount));
            OnPropertyChanged(nameof(ProblemsCount));
            OnPropertyChanged(nameof(RetryCount));
            OnPropertyChanged(nameof(DeadCount));
            OnPropertyChanged(nameof(SuccessCount));
            OnPropertyChanged(nameof(ProblemsDetail));
            OnPropertyChanged(nameof(SuccessDetail));
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(HasConnectionError));
            OnPropertyChanged(nameof(ConnectionErrorText));
            UpdateEventsState();
            OnPropertyChanged(nameof(LoadLimitText));
            OnPropertyChanged(nameof(HasLimitNote));

            ApplyFilter();

            QueueRowViewModel match = selectedId.HasValue
                ? Rows.FirstOrDefault(row => row.Id == selectedId.Value)
                : null;

            if (match != null)
            {
                SelectedRow = match;
            }
            else if (_selectedRow != null)
            {
                SelectedRow = null;
            }
        }

        private static int Count(QueueSnapshot snapshot, int status)
        {
            return snapshot.StatusCounts.TryGetValue(status, out int count) ? count : 0;
        }

        private void BuildLimitNote(QueueSnapshot snapshot)
        {
            List<string> notes = new();
            if (snapshot.ActiveRowsTruncated)
            {
                notes.Add("активных больше 5 000 — показаны первые 5 000");
            }

            if (snapshot.HistoryTruncated)
            {
                notes.Add("история ограничена последними " + HistoryLimit.ToString("N0", Ru));
            }

            _hasLimitNote = notes.Count > 0;
            _loadLimitText = notes.Count > 0 ? string.Join("; ", notes) : null;
        }

        private void ApplySort()
        {
            if (RowsView == null)
            {
                return;
            }

            RowsView.SortDescriptions.Clear();
            RowsView.SortDescriptions.Add(new SortDescription(
                nameof(QueueRowViewModel.Id),
                _selectedFilter?.SortAscending == true
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending));

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (RowsView == null)
            {
                return;
            }

            RowsView.Refresh();
            _visibleCount = RowsView.Cast<object>().Count();
            _visibleCountText = _visibleCount.ToString("N0", Ru) + " из " +
                                _loadedCount.ToString("N0", Ru);
            _loadedCountText = "загружено " + _loadedCount.ToString("N0", Ru);

            if (_loadedCount == 0)
            {
                _emptyMessage = _configuration == null
                    ? "Нет данных: не выбран appsettings.json интегратора"
                    : "В очереди нет событий";
            }
            else
            {
                _emptyMessage = "Нет событий по выбранному фильтру или поиску";
            }

            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(VisibleCountText));
            OnPropertyChanged(nameof(LoadedCountText));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(IsEmpty));
        }

        private bool FilterRow(object item)
        {
            if (item is not QueueRowViewModel row)
            {
                return false;
            }

            if (_selectedFilter != null && !_selectedFilter.Matches(row))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return true;
            }

            string haystack = row.SearchText;
            foreach (string token in _searchText.Split(
                         new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!haystack.Contains(token.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task LoadHistoryForSelectionAsync(QueueRowViewModel row)
        {
            if (row == null)
            {
                return;
            }

            _detailsCts?.Cancel();
            CancellationTokenSource cts = new();
            _detailsCts = cts;

            ClearHistory("Загрузка истории…");

            IntegratorConfiguration configuration = _configuration;
            if (configuration == null)
            {
                ClearHistory("Нет подключения к базе");
                return;
            }

            try
            {
                IReadOnlyList<QueueLogEntry> entries = await _repository.LoadHistoryAsync(
                    configuration, row.Id, cts.Token);

                if (cts.IsCancellationRequested || _disposed || !ReferenceEquals(row, _selectedRow))
                {
                    return;
                }

                foreach (QueueLogEntry entry in entries)
                {
                    History.Add(new QueueLogViewModel(entry));
                }

                _historyCount = History.Count;
                _hasHistory = _historyCount > 0;
                _detailsQueueId = row.Id;
                _historyStatusText = _hasHistory
                    ? "Загружено попыток: " + _historyCount.ToString("N0", Ru)
                    : "Записей истории пока нет — событие ещё не обрабатывалось";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested && !_disposed && ReferenceEquals(row, _selectedRow))
                {
                    _historyStatusText = "Не удалось загрузить историю: " + Sanitize(ex.Message);
                    _historyCount = 0;
                    _hasHistory = false;
                    _detailsQueueId = null;
                }
            }
            finally
            {
                if (ReferenceEquals(_detailsCts, cts))
                {
                    _detailsCts = null;
                }

                cts.Dispose();
                OnPropertyChanged(nameof(HistoryCount));
                OnPropertyChanged(nameof(HasHistory));
                OnPropertyChanged(nameof(HistoryStatusText));
            }
        }

        private void ClearHistory(string status)
        {
            History.Clear();
            _historyCount = 0;
            _hasHistory = false;
            _detailsQueueId = null;
            _historyStatusText = status;
            OnPropertyChanged(nameof(HistoryCount));
            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(HistoryStatusText));
        }

        private void ClearSnapshot()
        {
            _selectedRow = null;
            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(HasSelection));
            ClearHistory("Выберите событие в таблице");

            Rows.Clear();
            _loadedCount = 0;
            _totalCount = 0;
            _pendingCount = 0;
            _inWorkCount = 0;
            _retryCount = 0;
            _deadCount = 0;
            _successCount = 0;
            _lastUpdated = null;
            _hasLimitNote = false;
            _loadLimitText = null;

            foreach (QueueFilterOption filter in Filters)
            {
                filter.Count = 0;
            }

            OnPropertyChanged(nameof(LoadedCount));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(InWorkCount));
            OnPropertyChanged(nameof(ProblemsCount));
            OnPropertyChanged(nameof(RetryCount));
            OnPropertyChanged(nameof(DeadCount));
            OnPropertyChanged(nameof(SuccessCount));
            OnPropertyChanged(nameof(ProblemsDetail));
            OnPropertyChanged(nameof(SuccessDetail));
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(LoadLimitText));
            OnPropertyChanged(nameof(HasLimitNote));
            ApplyFilter();
        }

        private void ApplyConfigError(string message)
        {
            StopEvents();
            _configuration = null;
            _isConnected = false;
            _hasConfigurationError = true;
            _hasConnectionError = false;
            _configurationErrorText = message;
            _connectionErrorText = null;
            _configPath = null;
            _configFileDisplay = "appsettings.json не выбран";
            _connectionDisplay = "Подключение не настроено";
            _headerSubtitle = "Очередь PARSEC_QUEUE · только чтение";
            _eventTriggerPresent = true;
            _eventListenerConnected = false;

            OnPropertyChanged(nameof(HasConfigurationError));
            OnPropertyChanged(nameof(ConfigurationErrorText));
            OnPropertyChanged(nameof(ConfigPath));
            OnPropertyChanged(nameof(ConfigFileDisplay));
            OnPropertyChanged(nameof(ConfigTooltip));
            OnPropertyChanged(nameof(ConnectionDisplay));
            OnPropertyChanged(nameof(ConnectionTooltip));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(HasConnectionError));
            OnPropertyChanged(nameof(ConnectionErrorText));
            UpdateEventsState();
            ClearSnapshot();
            RefreshCommand.RaiseCanExecuteChanged();
        }

        private void CompleteInitialization()
        {
            _isInitialized = true;
            RestartTimer();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!_isInitialized || !_autoRefresh || _isBusy || _configuration == null || _disposed)
            {
                return;
            }

            _ = RefreshAsync("таймер");
        }

        private void OnEventTimerTick(object sender, EventArgs e)
        {
            _eventTimer.Stop();
            if (_disposed || _configuration == null)
            {
                return;
            }

            _ = RefreshAsync("событие");
        }

        private void RestartTimer()
        {
            if (_timer == null)
            {
                return;
            }

            _timer.Stop();
            if (_isInitialized && _autoRefresh)
            {
                _timer.Interval = TimeSpan.FromSeconds(_refreshSeconds);
                _timer.Start();
            }
        }

        /// <summary>
        /// Подписка на событие Firebird PARSEC_QUEUE_CHANGED: пока она активна,
        /// очередь перечитывается сразу после изменения таблицы. Резервный
        /// таймер остаётся включённым — так данные не устаревают при обрыве связи.
        /// </summary>
        private void StartEvents()
        {
            StopEvents();

            IntegratorConfiguration configuration = _configuration;
            if (configuration == null)
            {
                return;
            }

            QueueEventWatcher watcher;
            try
            {
                watcher = new QueueEventWatcher(configuration.ConnectionString);
            }
            catch (Exception ex)
            {
                _eventListenerConnected = false;
                _eventTriggerPresent = true;
                UpdateEventsState("Не удалось подписаться на события: " + Sanitize(ex.Message));
                return;
            }

            watcher.Changed += OnQueueChanged;
            watcher.StateChanged += OnEventStateChanged;
            _watcher = watcher;

            try
            {
                watcher.Start();
            }
            catch (Exception ex)
            {
                _watcher = null;
                watcher.Changed -= OnQueueChanged;
                watcher.StateChanged -= OnEventStateChanged;
                watcher.Dispose();
                _eventListenerConnected = false;
                UpdateEventsState("Не удалось подписаться на события: " + Sanitize(ex.Message));
                return;
            }

            UpdateEventsState();
        }

        private void StopEvents()
        {
            QueueEventWatcher watcher = _watcher;
            _watcher = null;
            if (watcher == null)
            {
                return;
            }

            watcher.Changed -= OnQueueChanged;
            watcher.StateChanged -= OnEventStateChanged;
            watcher.Dispose();

            _eventListenerConnected = false;
        }

        /// <summary>
        /// Событие приходит из фонового потока слушателя: переключаемся на поток
        /// интерфейса и планируем одно перечитывание очереди.
        /// </summary>
        private void OnQueueChanged(object sender, EventArgs e)
        {
            Dispatcher dispatcher = _dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || _disposed)
            {
                return;
            }

            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ScheduleEventRefresh));
            }
            catch
            {
                // Окно уже закрывается — обновлять нечего.
            }
        }

        private void OnEventStateChanged(object sender, EventArgs e)
        {
            Dispatcher dispatcher = _dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || _disposed)
            {
                return;
            }

            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => UpdateEventsState()));
            }
            catch
            {
            }
        }

        private void ScheduleEventRefresh()
        {
            if (_disposed)
            {
                return;
            }

            _eventTimer.Stop();
            _eventTimer.Start();
        }

        private void UpdateEventsState(string listenerError = null)
        {
            QueueEventWatcher watcher = _watcher;
            _eventListenerConnected = watcher != null && watcher.Connected;
            string error = listenerError ?? (watcher == null ? null : watcher.LastError);

            UpdateEventsWarning(error);
            UpdateHeaderSubtitle();

            OnPropertyChanged(nameof(IsEventsOnline));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(EventsModeTooltip));
        }

        private void UpdateEventsWarning(string listenerError)
        {
            string fallback = _autoRefresh
                ? "Данные обновляются по резервному таймеру."
                : "Резервный опрос выключен — данные не обновляются.";

            string warning = null;
            if (_isConnected && !_hasConnectionError && !_hasConfigurationError)
            {
                if (!_eventTriggerPresent)
                {
                    warning = "События Firebird не настроены: в базе нет активного триггера " +
                              "PARSEC_QUEUE_EVENTS. " + fallback +
                              " Примените db/08_parsec_queue_events.sql.";
                }
                else if (!_eventListenerConnected && !string.IsNullOrWhiteSpace(listenerError))
                {
                    string message = Sanitize(listenerError);
                    if (message.Length > 160)
                    {
                        message = message.Substring(0, 160) + "…";
                    }

                    warning = "События Firebird недоступны: " + message + ". " + fallback;
                }
            }

            bool changed = !string.Equals(warning, _eventsWarningText, StringComparison.Ordinal);
            _hasEventsWarning = warning != null;
            _eventsWarningText = warning;

            if (changed)
            {
                OnPropertyChanged(nameof(HasEventsWarning));
                OnPropertyChanged(nameof(EventsWarningText));
            }
        }

        private void UpdateHeaderSubtitle()
        {
            string subtitle;
            if (_hasConfigurationError || string.IsNullOrWhiteSpace(_connectionDisplay) ||
                string.Equals(_connectionDisplay, "Подключение не настроено", StringComparison.Ordinal))
            {
                subtitle = "Очередь PARSEC_QUEUE · только чтение";
            }
            else
            {
                subtitle = "Очередь PARSEC_QUEUE · " + _connectionDisplay +
                           (_isConnected ? (IsEventsOnline ? " · события" : " · опрос по таймеру") : string.Empty) +
                           " · только чтение";
            }

            if (string.Equals(subtitle, _headerSubtitle, StringComparison.Ordinal))
            {
                return;
            }

            _headerSubtitle = subtitle;
            OnPropertyChanged(nameof(HeaderSubtitle));
        }

        private void IsBusySet(bool value)
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
            RefreshCommand.RaiseCanExecuteChanged();
        }

        private static string BuildConfigFileDisplay(string path)
        {
            try
            {
                return Path.GetFileName(path) + " · " +
                       File.GetLastWriteTime(path).ToString("dd.MM.yyyy HH:mm", Ru);
            }
            catch
            {
                return Path.GetFileName(path);
            }
        }

        private static string NormalizeOptionalPath(string path)
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

        private static string BuildDatabaseErrorMessage(string message)
        {
            string sanitized = Sanitize(message);

            if (sanitized.Contains("Table unknown", StringComparison.OrdinalIgnoreCase) &&
                sanitized.Contains("PARSEC_QUEUE", StringComparison.OrdinalIgnoreCase))
            {
                return "В базе нет таблицы очереди PARSEC_QUEUE. Примените к этой базе скрипты " +
                       "db/01..03 (при обновлении с 4.0.0 — db/06), затем повторите обновление. " +
                       "Ответ базы: " + sanitized;
            }

            if (sanitized.Contains("no permission", StringComparison.OrdinalIgnoreCase) ||
                sanitized.Contains("no write access", StringComparison.OrdinalIgnoreCase))
            {
                return "У пользователя базы нет прав на чтение PARSEC_QUEUE/PARSEC_QUEUE_LOG. " +
                       "Выдайте select на обе таблицы. Ответ базы: " + sanitized;
            }

            return sanitized;
        }

        private static string Sanitize(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "неизвестная ошибка";
            }

            string result = PasswordRegex.Replace(message, "$1=***");
            result = result.Replace("\r", " ").Replace("\n", " ").Trim();

            if (result.Length > 700)
            {
                result = result.Substring(0, 700) + "…";
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _eventTimer.Stop();
            _refreshCts?.Cancel();
            _detailsCts?.Cancel();
            StopEvents();

            _settings.ConfigPath = _configuration?.ConfigPath ?? _settings.ConfigPath;
            _settings.AutoRefresh = _autoRefresh;
            _settings.RefreshSeconds = _refreshSeconds;
            _settings.HistoryLimit = HistoryLimit;
            _settingsStore.Save(_settings);
        }
    }
}
