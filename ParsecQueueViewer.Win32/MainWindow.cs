using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ParsecQueueViewer.Win32
{
    internal sealed class MainWindow : IDisposable
    {
        private const int IdRefresh = 100;
        private const int IdChoose = 101;
        private const int IdSearch = 110;
        private const int IdClearSearch = 111;
        private const int IdAuto = 120;
        private const int IdInterval = 121;
        private const int IdList = 140;
        private const int IdDetails = 141;
        private const int IdHistory = 142;
        private const int IdFilterBase = 200;
        private const int IdSummaryBase = 220;
        private const int IdStatusPill = 240;
        private const int IdBanner = 241;
        private const int IdFooterConfig = 400;
        private const int IdFooterCounts = 401;
        private const int IdFooterUpdated = 402;
        private const int IdFooterVersion = 403;

        private static readonly IntPtr TimerRefresh = new IntPtr(1);
        private static readonly IntPtr TimerSearch = new IntPtr(2);
        private static readonly IntPtr TimerScreenshot = new IntPtr(3);

        private static readonly string[] FilterNames =
        {
            "Активные", "Ожидают", "В работе", "Ошибки", "История", "Все"
        };

        private static readonly string[] SummaryLabels =
        {
            "Ожидают", "В работе", "Требуют внимания", "Успешно", "Всего в очереди"
        };

        private static readonly string[] SummarySubs =
        {
            "новые события", "забрано интегратором", "повтор 0 · ошибок 0", "статус 2", "PARSEC_QUEUE"
        };

        private static readonly uint[] SummaryColors =
        {
            Native.Rgb(37, 99, 235),
            Native.Rgb(217, 119, 6),
            Native.Rgb(220, 38, 38),
            Native.Rgb(22, 163, 74),
            Native.Rgb(100, 116, 139)
        };

        private static readonly uint ColorText = Native.Rgb(29, 39, 51);
        private static readonly uint ColorMuted = Native.Rgb(104, 115, 133);
        private static readonly uint ColorAccent = Native.Rgb(37, 99, 235);
        private static readonly uint ColorBorder = Native.Rgb(226, 231, 239);
        private static readonly uint ColorPage = Native.Rgb(243, 245, 249);
        private static readonly uint ColorWhite = Native.Rgb(255, 255, 255);
        private static readonly uint ColorGreen = Native.Rgb(21, 128, 61);
        private static readonly uint ColorAmber = Native.Rgb(180, 83, 9);
        private static readonly uint ColorRed = Native.Rgb(185, 28, 28);
        private static readonly uint ColorRedSoft = Native.Rgb(254, 242, 242);
        private static readonly uint ColorGreenSoft = Native.Rgb(220, 252, 231);
        private static readonly uint ColorAmberSoft = Native.Rgb(254, 243, 199);
        private static readonly uint ColorRedDot = Native.Rgb(185, 28, 28);

        private readonly string _explicitConfigPath;
        private readonly string _screenshotPath;
        private readonly Native.WndProc _wndProc;
        private readonly string _className;
        private readonly object _pendingLock = new object();
        private readonly List<QueueRow> _allRows = new List<QueueRow>();
        private readonly List<DisplayRow> _visibleRows = new List<DisplayRow>();
        private readonly ViewerSettings _settings = new ViewerSettings();

        private IntPtr _hwnd;
        private IntPtr _list;
        private IntPtr _history;
        private IntPtr _details;
        private IntPtr _search;
        private IntPtr _clearSearch;
        private IntPtr _refreshButton;
        private IntPtr _chooseButton;
        private IntPtr _autoCheck;
        private IntPtr _intervalCombo;
        private IntPtr _statusPill;
        private IntPtr _banner;
        private IntPtr _footerConfig;
        private IntPtr _footerCounts;
        private IntPtr _footerUpdated;
        private IntPtr _footerVersion;
        private IntPtr _labelTitle;
        private IntPtr _labelSubtitle;
        private IntPtr _labelDetails;
        private IntPtr _labelHistory;
        private IntPtr _labelAuto;
        private IntPtr _labelSeconds;
        private readonly IntPtr[] _filterButtons = new IntPtr[6];
        private readonly IntPtr[] _summaryCards = new IntPtr[5];

        private IntPtr _fontTitle;
        private IntPtr _fontSubtitle;
        private IntPtr _fontLabel;
        private IntPtr _fontSmall;
        private IntPtr _fontButton;
        private IntPtr _fontValue;
        private IntPtr _fontList;
        private IntPtr _fontDetails;
        private IntPtr _fontFooter;
        private IntPtr _brushPage;
        private IntPtr _brushWhite;
        private IntPtr _brushBanner;
        private IntPtr _brushGreen;
        private IntPtr _brushAmber;
        private IntPtr _brushRed;

        private int _dpi = 96;
        private float _scale = 1f;
        private int _clientWidth;
        private int _clientHeight;

        private QueueSnapshot _snapshot;
        private IntegratorConfig _config;
        private QueueRepository _repository;
        private QueueRow _selectedRow;
        private long? _selectedId;
        private long? _historyForId;
        private int _historyForAttempts = -1;
        private QueueFilterKind _filter = QueueFilterKind.Active;
        private string _searchText = string.Empty;
        private string _errorText;
        private bool _refreshing;
        private bool _refreshAgain;
        private bool _rebuilding;
        private bool _autoRefresh = true;
        private int _intervalSeconds = 10;
        private int _generation;
        private int _historyGeneration;
        private int _lastUpdatedTick;
        private DateTime? _lastUpdated;
        private string _statusText = "Ожидание";
        private uint _statusBackColor = Native.Rgb(241, 245, 249);
        private uint _statusTextColor = Native.Rgb(71, 85, 105);

        private QueueSnapshot _pendingSnapshot;
        private string _pendingError;
        private int _pendingGeneration;
        private List<QueueLogEntry> _pendingHistory;
        private long _pendingHistoryId;
        private int _pendingHistoryGeneration;

        private int _sortColumn;
        private bool _sortAscending = true;

        private readonly string[] _filterCounts = { "0", "0", "0", "0", "0", "0" };
        private readonly string[] _summaryValues = { "0", "0", "0", "0", "0" };

        public MainWindow(string explicitConfigPath, string screenshotPath)
        {
            _explicitConfigPath = explicitConfigPath;
            _screenshotPath = screenshotPath;
            _wndProc = WndProc;
            _className = "ParsecQueueViewerWin32_" + Guid.NewGuid().ToString("N");
            _settings.Load();
            _autoRefresh = _settings.AutoRefresh;
            _intervalSeconds = _settings.IntervalSeconds;

            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                _dpi = (int)graphics.DpiX;
            }

            _scale = Math.Max(1f, _dpi / 96f);

            IntPtr instance = Native.GetModuleHandle(null);
            IntPtr icon = Native.LoadIcon(instance, new IntPtr(32512));
            Native.WNDCLASSEX wc = new Native.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
                style = 0x0003,
                lpfnWndProc = _wndProc,
                hInstance = instance,
                hIcon = icon,
                hCursor = Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)),
                hbrBackground = IntPtr.Zero,
                lpszClassName = _className,
                hIconSm = icon
            };

            if (Native.RegisterClassEx(ref wc) == 0)
            {
                throw new InvalidOperationException("Не удалось зарегистрировать окно Win32.");
            }

            int width = S(1440);
            int height = S(900);
            int x = Math.Max(0, (Native.GetSystemMetrics(Native.SM_CXSCREEN) - width) / 2);
            int y = Math.Max(0, (Native.GetSystemMetrics(Native.SM_CYSCREEN) - height) / 2);

            _hwnd = Native.CreateWindowEx(
                Native.WS_EX_APPWINDOW,
                _className,
                "Очередь интегратора — PARSEC (Win32)",
                Native.WS_OVERLAPPEDWINDOW | 0x02000000,
                x, y, width, height,
                IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("Не удалось создать главное окно Win32.");
            }

            CreateResources();
            CreateControls();
            Layout();
        }

        public int Run()
        {
            Native.ShowWindow(_hwnd, Native.SW_SHOW);
            Native.UpdateWindow(_hwnd);
            LoadInitialConfig();

            Native.MSG message;
            while (Native.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }

            return 0;
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        private void CreateResources()
        {
            _brushPage = Native.CreateSolidBrush(ColorPage);
            _brushWhite = Native.CreateSolidBrush(ColorWhite);
            _brushBanner = Native.CreateSolidBrush(ColorRedSoft);
            _brushGreen = Native.CreateSolidBrush(ColorGreenSoft);
            _brushAmber = Native.CreateSolidBrush(ColorAmberSoft);
            _brushRed = Native.CreateSolidBrush(ColorRedSoft);

            _fontTitle = CreateFont(19, Native.FW_SEMIBOLD);
            _fontSubtitle = CreateFont(10.5f, Native.FW_NORMAL);
            _fontLabel = CreateFont(8.5f, Native.FW_NORMAL);
            _fontSmall = CreateFont(9, Native.FW_NORMAL);
            _fontButton = CreateFont(9.5f, Native.FW_SEMIBOLD);
            _fontValue = CreateFont(17, Native.FW_SEMIBOLD);
            _fontList = CreateFont(9.5f, Native.FW_NORMAL);
            _fontDetails = CreateFont(9, Native.FW_NORMAL);
            _fontFooter = CreateFont(8.5f, Native.FW_NORMAL);
        }

        private IntPtr CreateFont(float points, int weight)
        {
            return Native.CreateFont(
                -Math.Max(1, (int)Math.Round(points * _dpi / 72.0)),
                0, 0, 0, weight,
                0, 0, 0,
                Native.DEFAULT_CHARSET,
                Native.OUT_DEFAULT_PRECIS,
                Native.CLIP_DEFAULT_PRECIS,
                Native.CLEARTYPE_QUALITY,
                Native.DEFAULT_PITCH | Native.FF_DONTCARE,
                "Segoe UI");
        }

        private void CreateControls()
        {
            _labelTitle = Create(0, "STATIC",
                "Очередь интегратора", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, 0);
            _labelSubtitle = Create(0, "STATIC",
                "PARSEC_QUEUE · только чтение", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, 0);

            _statusPill = Create(0, "STATIC", "", Native.SS_OWNERDRAW, 0, 0, 10, 10, IdStatusPill);
            _chooseButton = Create(0, "BUTTON", "Выбрать appsettings.json",
                Native.BS_PUSHBUTTON | Native.WS_TABSTOP, 0, 0, 10, 10, IdChoose);
            _refreshButton = Create(0, "BUTTON", "Обновить",
                Native.BS_PUSHBUTTON | Native.WS_TABSTOP, 0, 0, 10, 10, IdRefresh);

            _banner = Create(0, "STATIC", "", Native.SS_OWNERDRAW, 0, 0, 10, 10, IdBanner);

            for (int i = 0; i < 5; i++)
            {
                _summaryCards[i] = Create(0, "STATIC", "", Native.SS_OWNERDRAW,
                    0, 0, 10, 10, IdSummaryBase + i);
            }

            for (int i = 0; i < 6; i++)
            {
                _filterButtons[i] = Create(0, "BUTTON", FilterNames[i],
                    Native.BS_OWNERDRAW | Native.WS_TABSTOP, 0, 0, 10, 10, IdFilterBase + i);
            }

            _search = Create(0, "EDIT", "",
                Native.ES_AUTOHSCROLL | Native.WS_TABSTOP, 0, 0, 10, 10, IdSearch);
            Native.SendMessage(_search, Native.EM_SETCUEBANNER, new IntPtr(1),
                "Поиск по ID, сотруднику, карте, ошибке…");

            _clearSearch = Create(0, "BUTTON", "×",
                Native.BS_PUSHBUTTON | Native.WS_TABSTOP, 0, 0, 10, 10, IdClearSearch);

            _labelDetails = Create(0, "STATIC",
                "Детали события", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, 0);
            _labelHistory = Create(0, "STATIC",
                "История попыток", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, 0);

            _details = Create(Native.WS_EX_CLIENTEDGE, "EDIT", "Выберите событие в очереди",
                Native.ES_MULTILINE | Native.ES_READONLY | Native.ES_AUTOVSCROLL | Native.WS_VSCROLL,
                0, 0, 10, 10, IdDetails);

            _history = Create(Native.WS_EX_CLIENTEDGE, "SysListView32", "",
                Native.LVS_REPORT | Native.LVS_SINGLESEL | Native.LVS_SHOWSELALWAYS,
                0, 0, 10, 10, IdHistory);

            _list = Create(Native.WS_EX_CLIENTEDGE, "SysListView32", "",
                Native.LVS_REPORT | Native.LVS_SINGLESEL | Native.LVS_SHOWSELALWAYS,
                0, 0, 10, 10, IdList);

            _labelAuto = Create(0, "STATIC",
                "Автообновление", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, 0);
            _autoCheck = Create(0, "BUTTON", "",
                Native.BS_AUTOCHECKBOX | Native.WS_TABSTOP, 0, 0, 10, 10, IdAuto);
            Native.SendMessage(_autoCheck, 0x00F1, new IntPtr(_autoRefresh ? 1 : 0), IntPtr.Zero);

            _intervalCombo = Create(0, "COMBOBOX", "",
                Native.CBS_DROPDOWNLIST | Native.CBS_HASSTRINGS | Native.WS_TABSTOP,
                0, 0, 10, 200, IdInterval);
            foreach (int seconds in new[] { 5, 10, 30, 60 })
            {
                Native.SendMessage(_intervalCombo, 0x0143, IntPtr.Zero,
                    seconds.ToString(CultureInfo.InvariantCulture));
            }

            int selectedIndex = Array.IndexOf(new[] { 5, 10, 30, 60 }, _intervalSeconds);
            Native.SendMessage(_intervalCombo, 0x014E, new IntPtr(selectedIndex < 0 ? 1 : selectedIndex),
                IntPtr.Zero);
            _labelSeconds = Create(0, "STATIC",
                "с", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, 0);

            _footerConfig = Create(0, "STATIC",
                "Настройки не выбраны", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, IdFooterConfig);
            _footerCounts = Create(0, "STATIC",
                "0 из 0 · загружено 0", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, IdFooterCounts);
            _footerUpdated = Create(0, "STATIC",
                "ещё не обновлялось", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, IdFooterUpdated);
            _footerVersion = Create(0, "STATIC",
                "Win32 · v1.0.0", Native.SS_LEFT | Native.SS_NOPREFIX, 0, 0, 10, 10, IdFooterVersion);

            SetFont(_labelTitle, _fontTitle);
            SetFont(_labelSubtitle, _fontSubtitle);
            SetFont(_statusPill, _fontButton);
            SetFont(_chooseButton, _fontButton);
            SetFont(_refreshButton, _fontButton);
            SetFont(_clearSearch, _fontButton);
            SetFont(_labelDetails, _fontSubtitle);
            SetFont(_labelHistory, _fontSubtitle);
            SetFont(_details, _fontDetails);
            SetFont(_list, _fontList);
            SetFont(_history, _fontList);
            SetFont(_labelAuto, _fontFooter);
            SetFont(_autoCheck, _fontFooter);
            SetFont(_intervalCombo, _fontFooter);
            SetFont(_labelSeconds, _fontFooter);
            SetFont(_footerConfig, _fontFooter);
            SetFont(_footerCounts, _fontFooter);
            SetFont(_footerUpdated, _fontFooter);
            SetFont(_footerVersion, _fontFooter);

            Native.SetWindowTheme(_list, "Explorer", null);
            Native.SetWindowTheme(_history, "Explorer", null);
            Native.SetWindowTheme(_search, "Explorer", null);
            Native.SetWindowTheme(_intervalCombo, "Explorer", null);
            Native.SetWindowTheme(_chooseButton, "Explorer", null);
            Native.SetWindowTheme(_refreshButton, "Explorer", null);
            Native.SetWindowTheme(_clearSearch, "Explorer", null);
            Native.SetWindowTheme(_autoCheck, "Explorer", null);

            Native.SendMessage(_list, Native.LVM_SETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero,
                new IntPtr(Native.LVS_EX_FULLROWSELECT | Native.LVS_EX_DOUBLEBUFFER |
                           Native.LVS_EX_GRIDLINES));
            Native.SendMessage(_history, Native.LVM_SETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero,
                new IntPtr(Native.LVS_EX_FULLROWSELECT | Native.LVS_EX_DOUBLEBUFFER));
            Native.SendMessage(_list, Native.LVM_SETBKCOLOR, IntPtr.Zero, new IntPtr(unchecked((int)ColorWhite)));
            Native.SendMessage(_list, Native.LVM_SETTEXTCOLOR, IntPtr.Zero, new IntPtr(unchecked((int)ColorText)));
            Native.SendMessage(_history, Native.LVM_SETBKCOLOR, IntPtr.Zero, new IntPtr(unchecked((int)ColorWhite)));

            AddColumn(_list, 0, "ID", 55);
            AddColumn(_list, 1, "Статус", 95);
            AddColumn(_list, 2, "Команда", 210);
            AddColumn(_list, 3, "Объект", 215);
            AddColumn(_list, 4, "Попытки", 72);
            AddColumn(_list, 5, "Создано", 130);
            AddColumn(_list, 6, "Завершено", 130);
            AddColumn(_list, 7, "Длит.", 72);
            AddColumn(_list, 8, "Ошибка", 260);

            AddColumn(_history, 0, "№", 38);
            AddColumn(_history, 1, "Статус", 90);
            AddColumn(_history, 2, "Начата", 130);
            AddColumn(_history, 3, "Длит.", 65);
            AddColumn(_history, 4, "Ошибка", 270);

            Native.SendMessage(_list, 0x101F, IntPtr.Zero, IntPtr.Zero);
        }

        private void AddColumn(IntPtr list, int index, string text, int width)
        {
            Native.LVCOLUMN column = new Native.LVCOLUMN
            {
                mask = 0x0001 | 0x0002 | 0x0004 | 0x0008,
                fmt = 0,
                cx = S(width),
                pszText = text,
                iSubItem = index
            };
            Native.SendMessage(list, Native.LVM_INSERTCOLUMNW, new IntPtr(index), ref column);
        }

        private IntPtr Create(int exStyle, string className, string text, int style,
            int x, int y, int width, int height, int id)
        {
            return Native.CreateWindowEx(
                exStyle, className, text,
                Native.WS_CHILD | Native.WS_VISIBLE | style,
                x, y, width, height,
                _hwnd, new IntPtr(id), Native.GetModuleHandle(null), IntPtr.Zero);
        }

        private void SetFont(IntPtr control, IntPtr font)
        {
            if (control != IntPtr.Zero && font != IntPtr.Zero)
            {
                Native.SendMessage(control, Native.WM_SETFONT, font, new IntPtr(1));
            }
        }

        private static void SetWindowText(IntPtr control, string text)
        {
            Native.SetWindowText(control, text ?? string.Empty);
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case Native.WM_ERASEBKGND:
                    Native.RECT rect;
                    Native.GetClientRect(hWnd, out rect);
                    Native.FillRect(wParam, ref rect, _brushPage);
                    return new IntPtr(1);

                case Native.WM_SIZE:
                    Layout();
                    return IntPtr.Zero;

                case Native.WM_GETMINMAXINFO:
                    Native.MINMAXINFO info = (Native.MINMAXINFO)Marshal.PtrToStructure(
                        lParam, typeof(Native.MINMAXINFO));
                    info.ptMinTrackSize.X = S(1100);
                    info.ptMinTrackSize.Y = S(700);
                    Marshal.StructureToPtr(info, lParam, false);
                    return IntPtr.Zero;

                case Native.WM_COMMAND:
                    OnCommand(wParam);
                    return IntPtr.Zero;

                case Native.WM_NOTIFY:
                    return OnNotify(lParam);

                case Native.WM_TIMER:
                    OnTimer(wParam);
                    return IntPtr.Zero;

                case Native.WM_DRAWITEM:
                    OnDrawItem(lParam);
                    return IntPtr.Zero;

                case Native.WM_CTLCOLORSTATIC:
                    Native.SetBkMode(wParam, Native.TRANSPARENT);
                    Native.SetTextColor(wParam, ColorText);
                    return _brushPage;

                case Native.WM_CTLCOLOREDIT:
                    Native.SetBkMode(wParam, 2);
                    Native.SetTextColor(wParam, ColorText);
                    Native.SetBkColor(wParam, ColorWhite);
                    return _brushWhite;

                case Native.WM_KEYDOWN:
                    OnKeyDown(wParam);
                    return IntPtr.Zero;

                case Native.WM_REFRESH_DONE:
                    ProcessRefreshDone();
                    return IntPtr.Zero;

                case Native.WM_HISTORY_DONE:
                    ProcessHistoryDone();
                    return IntPtr.Zero;

                case Native.WM_CLOSE:
                    Native.DestroyWindow(hWnd);
                    return IntPtr.Zero;

                case Native.WM_DESTROY:
                    Native.KillTimer(hWnd, TimerRefresh);
                    Native.KillTimer(hWnd, TimerSearch);
                    Native.KillTimer(hWnd, TimerScreenshot);
                    _settings.AutoRefresh = _autoRefresh;
                    _settings.IntervalSeconds = _intervalSeconds;
                    _settings.ConfigPath = _config == null ? _settings.ConfigPath : _config.ConfigPath;
                    _settings.Save();
                    DisposeResources();
                    Native.PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            return Native.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void OnCommand(IntPtr wParam)
        {
            int id = Native.LowWord(wParam);
            int code = Native.HighWord(wParam);

            if (id >= IdFilterBase && id < IdFilterBase + 6 && code == Native.BN_CLICKED)
            {
                _filter = (QueueFilterKind)(id - IdFilterBase);
                _sortColumn = 0;
                _sortAscending = _filter == QueueFilterKind.Active || _filter == QueueFilterKind.Pending ||
                                 _filter == QueueFilterKind.InWork || _filter == QueueFilterKind.Problems;
                InvalidateFilters();
                RebuildRows();
                return;
            }

            switch (id)
            {
                case IdRefresh:
                    if (code == Native.BN_CLICKED)
                    {
                        StartRefresh();
                    }
                    break;

                case IdChoose:
                    if (code == Native.BN_CLICKED)
                    {
                        ChooseConfig();
                    }
                    break;

                case IdClearSearch:
                    if (code == Native.BN_CLICKED)
                    {
                        Native.SetWindowText(_search, "");
                        _searchText = string.Empty;
                        RebuildRows();
                        Native.SetFocus(_search);
                    }
                    break;

                case IdSearch:
                    if (code == Native.EN_CHANGE)
                    {
                        Native.KillTimer(_hwnd, TimerSearch);
                        Native.SetTimer(_hwnd, TimerSearch, 250, IntPtr.Zero);
                    }
                    break;

                case IdAuto:
                    if (code == Native.BN_CLICKED)
                    {
                        _autoRefresh = Native.SendMessage(_autoCheck, 0x00F0, IntPtr.Zero, IntPtr.Zero)
                            .ToInt64() != 0;
                        SetupRefreshTimer();
                    }
                    break;

                case IdInterval:
                    if (code == Native.CBN_SELCHANGE)
                    {
                        int index = Native.SendMessage(_intervalCombo, 0x0147, IntPtr.Zero, IntPtr.Zero)
                            .ToInt32();
                        int[] values = { 5, 10, 30, 60 };
                        if (index >= 0 && index < values.Length)
                        {
                            _intervalSeconds = values[index];
                            SetupRefreshTimer();
                        }
                    }
                    break;
            }
        }

        private IntPtr OnNotify(IntPtr lParam)
        {
            Native.NMHDR header = (Native.NMHDR)Marshal.PtrToStructure(lParam, typeof(Native.NMHDR));
            if (header.hwndFrom == _list)
            {
                if (header.code == unchecked((uint)Native.LVN_ITEMCHANGED))
                {
                    Native.NMLISTVIEW item = (Native.NMLISTVIEW)Marshal.PtrToStructure(
                        lParam, typeof(Native.NMLISTVIEW));
                    if (!_rebuilding && item.iItem >= 0 && (item.uNewState & Native.LVIS_SELECTED) != 0)
                    {
                        if (item.iItem < _visibleRows.Count)
                        {
                            SelectRow(_visibleRows[item.iItem].Row);
                        }
                    }
                }
                else if (header.code == unchecked((uint)Native.LVN_COLUMNCLICK))
                {
                    Native.NMLISTVIEW item = (Native.NMLISTVIEW)Marshal.PtrToStructure(
                        lParam, typeof(Native.NMLISTVIEW));
                    if (_sortColumn == item.iSubItem)
                    {
                        _sortAscending = !_sortAscending;
                    }
                    else
                    {
                        _sortColumn = item.iSubItem;
                        _sortAscending = true;
                    }
                    RebuildRows();
                }
                else if (header.code == unchecked((uint)Native.NM_CUSTOMDRAW))
                {
                    return DrawRowCustom(lParam);
                }
            }

            return IntPtr.Zero;
        }

        private IntPtr DrawRowCustom(IntPtr lParam)
        {
            Native.NMLVCUSTOMDRAW custom = (Native.NMLVCUSTOMDRAW)Marshal.PtrToStructure(
                lParam, typeof(Native.NMLVCUSTOMDRAW));

            uint stage = custom.nmcd.dwDrawStage;
            if (stage == Native.CDDS_PREPAINT)
            {
                return new IntPtr(Native.CDRF_NOTIFYITEMDRAW);
            }

            if ((stage & Native.CDDS_SUBITEM) != 0)
            {
                int index = custom.nmcd.dwItemSpec.ToInt32();
                if (index >= 0 && index < _visibleRows.Count)
                {
                    int status = _visibleRows[index].Row.Status;
                    if (custom.iSubItem == 1)
                    {
                        custom.clrText = StatusColor(status);
                    }
                    else if (custom.iSubItem == 8)
                    {
                        custom.clrText = string.IsNullOrWhiteSpace(_visibleRows[index].Row.ErrorText)
                            ? ColorMuted
                            : ColorRed;
                    }

                    custom.clrTextBk = ColorWhite;
                    Marshal.StructureToPtr(custom, lParam, false);
                }

                return new IntPtr(Native.CDRF_NEWFONT);
            }

            if ((stage & 0xFFFF) == 0x0001)
            {
                return new IntPtr(Native.CDRF_NOTIFYSUBITEMDRAW);
            }

            return IntPtr.Zero;
        }

        private void OnTimer(IntPtr timerId)
        {
            if (timerId == TimerRefresh)
            {
                StartRefresh();
            }
            else if (timerId == TimerSearch)
            {
                Native.KillTimer(_hwnd, TimerSearch);
                _searchText = GetText(_search);
                RebuildRows();
            }
            else if (timerId == TimerScreenshot)
            {
                Native.KillTimer(_hwnd, TimerScreenshot);
                CaptureScreenshot();
                Native.PostMessage(_hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void OnKeyDown(IntPtr key)
        {
            int code = key.ToInt32();
            if (code == Native.VK_F5)
            {
                StartRefresh();
            }
            else if (code == Native.VK_F && (Native.GetKeyState(Native.VK_CONTROL) & 0x8000) != 0)
            {
                Native.SetFocus(_search);
            }
            else if (code == Native.VK_ESCAPE)
            {
                Native.SetWindowText(_search, "");
                _searchText = string.Empty;
                RebuildRows();
            }
        }

        private void LoadInitialConfig()
        {
            string found = IntegratorConfig.Find(_explicitConfigPath, _settings.ConfigPath);
            if (string.IsNullOrWhiteSpace(found))
            {
                string message = string.IsNullOrWhiteSpace(_explicitConfigPath)
                    ? "Не найден appsettings.json интегратора. Нажмите «Выбрать appsettings.json»."
                    : "Файл из --config не найден: " + _explicitConfigPath;
                SetConfigError(message);
                return;
            }

            ApplyConfig(found);
        }

        private void ApplyConfig(string path)
        {
            try
            {
                IntegratorConfig loaded = IntegratorConfig.Load(path);
                _config = loaded;
                _repository = new QueueRepository(loaded.ConnectionString);
                _settings.ConfigPath = loaded.ConfigPath;
                _settings.Save();
                _generation++;
                _allRows.Clear();
                _selectedId = null;
                _selectedRow = null;
                _historyForId = null;
                _historyForAttempts = -1;
                _snapshot = null;
                _errorText = null;
                UpdateCounts();
                ClearHistory();
                RebuildRows();
                UpdateFooter();
                SetSubtitle();
                SetWindowText(_footerConfig, "Настройки: " + Path.GetFileName(loaded.ConfigPath) +
                                              " · " + loaded.ConnectionDisplay);
                Native.EnableWindow(_refreshButton, true);
                SetupRefreshTimer();
                StartRefresh();
            }
            catch (Exception ex)
            {
                SetConfigError("Не удалось загрузить настройки: " + Sanitize(ex.Message));
            }
        }

        private void SetConfigError(string message)
        {
            _config = null;
            _repository = null;
            _errorText = message;
            _allRows.Clear();
            _selectedId = null;
            _selectedRow = null;
            _snapshot = null;
            Native.KillTimer(_hwnd, TimerRefresh);
            UpdateCounts();
            ClearHistory();
            RebuildRows();
            SetSubtitle();
            UpdateFooter();
            SetWindowText(_footerConfig, "Настройки не выбраны");
            SetStatus("Нет настроек", ColorRedSoft, ColorRed);
            Layout();
            InvalidateBanner();
            InvalidateStatus();
            if (!string.IsNullOrWhiteSpace(_screenshotPath))
            {
                Native.SetTimer(_hwnd, TimerScreenshot, 1000, IntPtr.Zero);
            }
        }

        private void ChooseConfig()
        {
            string initialDirectory = null;
            if (_config != null)
            {
                try
                {
                    initialDirectory = Path.GetDirectoryName(_config.ConfigPath);
                }
                catch
                {
                }
            }

            string selected = ShowOpenFileDialog(initialDirectory);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                ApplyConfig(selected);
            }
        }

        private string ShowOpenFileDialog(string initialDirectory)
        {
            const int maxFile = 2048;
            IntPtr filter = IntPtr.Zero;
            IntPtr fileBuffer = IntPtr.Zero;
            try
            {
                filter = Marshal.StringToHGlobalUni(
                    "Настройки интегратора (appsettings.json)\0appsettings.json\0" +
                    "JSON-файлы (*.json)\0*.json\0Все файлы (*.*)\0*.*\0\0");
                fileBuffer = Marshal.AllocHGlobal(maxFile * 2);
                for (int i = 0; i < maxFile * 2; i++)
                {
                    Marshal.WriteByte(fileBuffer, i, 0);
                }

                Native.OPENFILENAME dialog = new Native.OPENFILENAME
                {
                    lStructSize = (uint)Marshal.SizeOf(typeof(Native.OPENFILENAME)),
                    hwndOwner = _hwnd,
                    hInstance = Native.GetModuleHandle(null),
                    lpstrFilter = filter,
                    nFilterIndex = 1,
                    lpstrFile = fileBuffer,
                    nMaxFile = maxFile,
                    lpstrInitialDir = string.IsNullOrWhiteSpace(initialDirectory) ? null : initialDirectory,
                    lpstrTitle = "Выберите appsettings.json интегратора",
                    lpstrDefExt = "json",
                    Flags = 0x00001000 /* OFN_FILEMUSTEXIST */ |
                            0x00000800 /* OFN_PATHMUSTEXIST */ |
                            0x00000008 /* OFN_NOCHANGEDIR */
                };

                if (!Native.GetOpenFileName(ref dialog))
                {
                    return null;
                }

                return Marshal.PtrToStringUni(fileBuffer);
            }
            finally
            {
                if (filter != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(filter);
                }

                if (fileBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(fileBuffer);
                }
            }
        }

        private void StartRefresh()
        {
            if (_repository == null)
            {
                return;
            }

            if (_refreshing)
            {
                _refreshAgain = true;
                return;
            }

            _refreshing = true;
            _refreshAgain = false;
            Native.EnableWindow(_refreshButton, false);
            SetStatus("Обновление…", ColorAmberSoft, ColorAmber);
            InvalidateStatus();

            int generation = _generation;
            QueueRepository repository = _repository;

            Task.Run(() =>
            {
                QueueSnapshot snapshot = null;
                string error = null;
                try
                {
                    snapshot = repository.LoadSnapshot(1000);
                }
                catch (Exception ex)
                {
                    error = Sanitize(ex.Message);
                }

                lock (_pendingLock)
                {
                    _pendingSnapshot = snapshot;
                    _pendingError = error;
                    _pendingGeneration = generation;
                }

                Native.PostMessage(_hwnd, Native.WM_REFRESH_DONE, IntPtr.Zero, IntPtr.Zero);
            });
        }

        private void ProcessRefreshDone()
        {
            QueueSnapshot snapshot;
            string error;
            int generation;
            lock (_pendingLock)
            {
                snapshot = _pendingSnapshot;
                error = _pendingError;
                generation = _pendingGeneration;
                _pendingSnapshot = null;
                _pendingError = null;
            }

            _refreshing = false;
            Native.EnableWindow(_refreshButton, true);

            if (generation == _generation)
            {
                if (error != null)
                {
                    _errorText = FriendlyError(error);
                    SetStatus("Ошибка", ColorRedSoft, ColorRed);
                    Layout();
                    InvalidateBanner();
                    InvalidateStatus();
                    if (!string.IsNullOrWhiteSpace(_screenshotPath))
                    {
                        Native.SetTimer(_hwnd, TimerScreenshot, 900, IntPtr.Zero);
                    }
                }
                else if (snapshot != null)
                {
                    ApplySnapshot(snapshot);
                }
            }

            if (_refreshAgain)
            {
                _refreshAgain = false;
                StartRefresh();
            }
        }

        private void ApplySnapshot(QueueSnapshot snapshot)
        {
            long? previousId = _selectedId;
            _snapshot = snapshot;
            _allRows.Clear();
            _allRows.AddRange(snapshot.Rows);
            _errorText = null;
            _lastUpdated = snapshot.LoadedAt;
            _lastUpdatedTick = Environment.TickCount;
            SetStatus("Подключено", ColorGreenSoft, ColorGreen);
            SetSubtitle();
            UpdateCounts();
            UpdateFooter();
            Layout();
            InvalidateBanner();
            InvalidateStatus();

            if (previousId.HasValue && _allRows.Any(r => r.Id == previousId.Value))
            {
                _selectedId = previousId;
            }
            else
            {
                _selectedId = null;
            }

            RebuildRows();

            if (!previousId.HasValue && _visibleRows.Count > 0)
            {
                QueueRow best = _visibleRows.Select(r => r.Row)
                    .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.ErrorText))
                    ?? _visibleRows.Select(r => r.Row).FirstOrDefault(r => r.Status is 0 or 1 or 3 or 4)
                    ?? _visibleRows[0].Row;
                if (_selectedId != best.Id)
                {
                    _selectedId = best.Id;
                    RebuildRows();
                }
            }

            if (!string.IsNullOrWhiteSpace(_screenshotPath))
            {
                Native.SetTimer(_hwnd, TimerScreenshot, 900, IntPtr.Zero);
            }
        }

        private void UpdateCounts()
        {
            if (_snapshot == null)
            {
                for (int i = 0; i < _filterCounts.Length; i++)
                {
                    _filterCounts[i] = "0";
                }

                for (int i = 0; i < _summaryValues.Length; i++)
                {
                    _summaryValues[i] = "0";
                }

                SummarySubs[2] = "повтор 0 · ошибок 0";
                InvalidateSummary();
                InvalidateFilters();
                return;
            }

            int pending = Count(0);
            int inWork = Count(1);
            int success = Count(2);
            int retry = Count(3);
            int dead = Count(4);
            int total = _snapshot.TotalCount;

            _filterCounts[0] = (pending + inWork + retry + dead).ToString(CultureInfo.InvariantCulture);
            _filterCounts[1] = pending.ToString(CultureInfo.InvariantCulture);
            _filterCounts[2] = inWork.ToString(CultureInfo.InvariantCulture);
            _filterCounts[3] = (retry + dead).ToString(CultureInfo.InvariantCulture);
            _filterCounts[4] = (success + Count(5) + Count(6)).ToString(CultureInfo.InvariantCulture);
            _filterCounts[5] = total.ToString(CultureInfo.InvariantCulture);

            _summaryValues[0] = pending.ToString(CultureInfo.InvariantCulture);
            _summaryValues[1] = inWork.ToString(CultureInfo.InvariantCulture);
            _summaryValues[2] = (retry + dead).ToString(CultureInfo.InvariantCulture);
            _summaryValues[3] = success.ToString(CultureInfo.InvariantCulture);
            _summaryValues[4] = total.ToString(CultureInfo.InvariantCulture);
            SummarySubs[2] = "повтор " + retry.ToString(CultureInfo.InvariantCulture) +
                             " · ошибок " + dead.ToString(CultureInfo.InvariantCulture);

            InvalidateSummary();
            InvalidateFilters();
        }

        private int Count(int status)
        {
            int count;
            return _snapshot != null && _snapshot.StatusCounts.TryGetValue(status, out count) ? count : 0;
        }

        private void UpdateFooter()
        {
            if (_snapshot == null)
            {
                SetWindowText(_footerCounts, "0 из 0 · загружено 0");
            }
            else
            {
                string limit = (_snapshot.ActiveRowsTruncated || _snapshot.HistoryTruncated)
                    ? " · лимит выборки"
                    : string.Empty;
                SetWindowText(_footerCounts,
                    "В таблице " + _visibleRows.Count.ToString(CultureInfo.InvariantCulture) +
                    " из " + _allRows.Count.ToString(CultureInfo.InvariantCulture) +
                    " · всего " + _snapshot.TotalCount.ToString(CultureInfo.InvariantCulture) + limit);
            }

            SetWindowText(_footerUpdated, _lastUpdated.HasValue
                ? "обновлено " + _lastUpdated.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : "ещё не обновлялось");
        }

        private void SetSubtitle()
        {
            string subtitle = _config == null
                ? "PARSEC_QUEUE · только чтение"
                : "PARSEC_QUEUE · " + _config.ConnectionDisplay + " · только чтение";
            SetWindowText(_labelSubtitle, subtitle);
        }

        private void SetStatus(string text, uint background, uint foreground)
        {
            _statusText = text;
            _statusBackColor = background;
            _statusTextColor = foreground;
            InvalidateStatus();
        }

        private void RebuildRows()
        {
            _rebuilding = true;
            try
            {
                IEnumerable<QueueRow> filtered = _allRows.Where(MatchesFilter);
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    string[] tokens = _searchText.Split(new[] { ' ', '\t', '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
                    filtered = filtered.Where(row =>
                    {
                        string[] values = BuildDisplay(row).SearchText.Split(' ');
                        return tokens.All(token => values.Any(value =>
                            value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
                    });
                }

                _visibleRows.Clear();
                foreach (QueueRow row in filtered)
                {
                    _visibleRows.Add(BuildDisplay(row));
                }

                SortRows();
                PopulateList();

                if (_selectedId.HasValue)
                {
                    QueueRow selected = _allRows.FirstOrDefault(r => r.Id == _selectedId.Value);
                    if (selected != null)
                    {
                        SelectRow(selected, true);
                    }
                }

                if (_selectedId == null && _visibleRows.Count > 0)
                {
                    SelectRow(_visibleRows[0].Row, true);
                }
                else if (_visibleRows.Count == 0)
                {
                    SelectRow(null, false);
                }
            }
            finally
            {
                _rebuilding = false;
            }

            UpdateFooter();
        }

        private bool MatchesFilter(QueueRow row)
        {
            switch (_filter)
            {
                case QueueFilterKind.Active:
                    return row.Status == 0 || row.Status == 1 || row.Status == 3 || row.Status == 4;
                case QueueFilterKind.Pending:
                    return row.Status == 0;
                case QueueFilterKind.InWork:
                    return row.Status == 1;
                case QueueFilterKind.Problems:
                    return row.Status == 3 || row.Status == 4;
                case QueueFilterKind.History:
                    return row.Status == 2 || row.Status == 5 || row.Status == 6;
                default:
                    return true;
            }
        }

        private DisplayRow BuildDisplay(QueueRow row)
        {
            string operation = Formatting.OperationName(row.Operation);
            string title;
            string subtitle;
            Formatting.Object(row, out title, out subtitle);
            return new DisplayRow
            {
                Row = row,
                Operation = operation,
                ObjectTitle = title,
                ObjectSubtitle = subtitle,
                SearchText = Formatting.BuildSearchText(row, operation, title, subtitle)
            };
        }

        private void SortRows()
        {
            Comparison<DisplayRow> comparison;
            switch (_sortColumn)
            {
                case 1:
                    comparison = (a, b) => a.Row.Status.CompareTo(b.Row.Status);
                    break;
                case 2:
                    comparison = (a, b) => string.Compare(a.Operation, b.Operation, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case 3:
                    comparison = (a, b) => string.Compare(a.ObjectTitle, b.ObjectTitle, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case 4:
                    comparison = (a, b) => a.Row.Attempts.CompareTo(b.Row.Attempts);
                    break;
                case 5:
                    comparison = (a, b) => Nullable.Compare(a.Row.CreatedAt, b.Row.CreatedAt);
                    break;
                case 6:
                    comparison = (a, b) => Nullable.Compare(a.Row.FinishedAt, b.Row.FinishedAt);
                    break;
                case 7:
                    comparison = (a, b) => Nullable.Compare(a.Row.DurationMs, b.Row.DurationMs);
                    break;
                case 8:
                    comparison = (a, b) => string.Compare(a.Row.ErrorText, b.Row.ErrorText,
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                default:
                    comparison = (a, b) => a.Row.Id.CompareTo(b.Row.Id);
                    break;
            }

            _visibleRows.Sort((a, b) =>
            {
                int result = comparison(a, b);
                if (!_sortAscending)
                {
                    result = -result;
                }

                return result != 0 ? result : a.Row.Id.CompareTo(b.Row.Id);
            });
        }

        private void PopulateList()
        {
            Native.SendMessage(_list, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW
            Native.SendMessage(_list, Native.LVM_DELETEALLITEMS, IntPtr.Zero, IntPtr.Zero);

            for (int i = 0; i < _visibleRows.Count; i++)
            {
                DisplayRow display = _visibleRows[i];
                QueueRow row = display.Row;
                Native.LVITEM item = new Native.LVITEM
                {
                    mask = Native.LVIF_TEXT | Native.LVIF_PARAM,
                    iItem = i,
                    iSubItem = 0,
                    pszText = row.Id.ToString(CultureInfo.InvariantCulture),
                    lParam = new IntPtr(i)
                };

                int index = Native.SendMessage(_list, Native.LVM_INSERTITEMW, IntPtr.Zero, ref item)
                    .ToInt32();
                SetSubItem(index, 1, Formatting.StatusName(row.Status));
                SetSubItem(index, 2, display.Operation);
                SetSubItem(index, 3, display.ObjectTitle);
                SetSubItem(index, 4, Formatting.Attempts(row));
                SetSubItem(index, 5, Formatting.Date(row.CreatedAt));
                SetSubItem(index, 6, Formatting.Date(row.FinishedAt));
                SetSubItem(index, 7, Formatting.Duration(row.DurationMs));
                SetSubItem(index, 8, string.IsNullOrWhiteSpace(row.ErrorText) ? "—" : row.ErrorText);
            }

            Native.SendMessage(_list, 0x000B, new IntPtr(1), IntPtr.Zero); // WM_SETREDRAW true
            Native.InvalidateRect(_list, IntPtr.Zero, true);

            if (_selectedId.HasValue)
            {
                int index = _visibleRows.FindIndex(r => r.Row.Id == _selectedId.Value);
                if (index >= 0)
                {
                    Native.LVITEM state = new Native.LVITEM
                    {
                        stateMask = Native.LVIS_SELECTED | Native.LVIS_FOCUSED,
                        state = Native.LVIS_SELECTED | Native.LVIS_FOCUSED
                    };
                    Native.SendMessage(_list, Native.LVM_SETITEMSTATE, new IntPtr(index), ref state);
                    Native.SendMessage(_list, Native.LVM_ENSUREVISIBLE, new IntPtr(index), new IntPtr(1));
                }
            }

            InvalidateFilters();
        }

        private void SetSubItem(int itemIndex, int column, string text)
        {
            Native.LVITEM item = new Native.LVITEM
            {
                iSubItem = column,
                pszText = text ?? string.Empty
            };
            Native.SendMessage(_list, Native.LVM_SETITEMTEXTW, new IntPtr(itemIndex), ref item);
        }

        private void SelectRow(QueueRow row, bool loadHistory = true)
        {
            if (row == null)
            {
                _selectedRow = null;
                _selectedId = null;
                _historyForId = null;
                _historyForAttempts = -1;
                SetWindowText(_details, "Выберите событие в очереди");
                ClearHistory();
                return;
            }

            _selectedRow = row;
            _selectedId = row.Id;
            SetWindowText(_details, BuildDetails(row));

            if (!loadHistory)
            {
                return;
            }

            bool needHistory = _historyForId != row.Id || _historyForAttempts != row.Attempts;
            if (needHistory)
            {
                _historyForId = row.Id;
                _historyForAttempts = row.Attempts;
                StartHistory(row.Id);
            }
        }

        private string BuildDetails(QueueRow row)
        {
            string title;
            string subtitle;
            Formatting.Object(row, out title, out subtitle);
            StringBuilder text = new StringBuilder(512);
            text.AppendLine("Событие #" + row.Id.ToString(CultureInfo.InvariantCulture) +
                            " · " + Formatting.StatusName(row.Status));
            text.AppendLine(Formatting.StatusDescription(row.Status));
            text.AppendLine();
            text.AppendLine("Ошибка:        " +
                            (string.IsNullOrWhiteSpace(row.ErrorText) ? "не указана" : row.ErrorText));
            text.AppendLine("Код ошибки:    " + (row.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "—"));
            text.AppendLine();
            text.AppendLine("Операция:      " + Formatting.OperationName(row.Operation) +
                            " (" + row.Operation.ToString(CultureInfo.InvariantCulture) + ")");
            text.AppendLine("Объект:        " + title);
            text.AppendLine("Подпись:       " + subtitle);
            text.AppendLine("ID_PEP:        " + (row.IdPep?.ToString(CultureInfo.InvariantCulture) ?? "—"));
            text.AppendLine("ID_CARD:       " + (string.IsNullOrWhiteSpace(row.IdCard) ? "—" : row.IdCard));
            text.AppendLine("Создано:       " + Formatting.Date(row.CreatedAt));
            text.AppendLine("Запущено:      " + Formatting.Date(row.StartedAt));
            text.AppendLine("Завершено:     " + Formatting.Date(row.FinishedAt));
            text.AppendLine("Следующая:     " + Formatting.Date(row.NextAttempt));
            text.AppendLine("Аренда до:     " + Formatting.Date(row.LeaseUntil));
            text.AppendLine("Длительность:  " + Formatting.Duration(row.DurationMs));
            text.AppendLine("Попытки:       " + Formatting.Attempts(row));
            text.AppendLine("Worker:        " + (string.IsNullOrWhiteSpace(row.WorkerId) ? "—" : row.WorkerId));
            text.AppendLine("Dedup-ключ:    " + (string.IsNullOrWhiteSpace(row.DedupKey) ? "—" : row.DedupKey));
            text.Append("Источник:      " + (string.IsNullOrWhiteSpace(row.SourceTable) ? "—" : row.SourceTable));
            return text.ToString();
        }

        private void StartHistory(long queueId)
        {
            ClearHistory();
            int generation = ++_historyGeneration;
            QueueRepository repository = _repository;
            if (repository == null)
            {
                return;
            }

            Task.Run(() =>
            {
                List<QueueLogEntry> entries = null;
                try
                {
                    entries = repository.LoadHistory(queueId);
                }
                catch
                {
                    entries = new List<QueueLogEntry>();
                }

                lock (_pendingLock)
                {
                    _pendingHistory = entries;
                    _pendingHistoryId = queueId;
                    _pendingHistoryGeneration = generation;
                }

                Native.PostMessage(_hwnd, Native.WM_HISTORY_DONE, IntPtr.Zero, IntPtr.Zero);
            });
        }

        private void ProcessHistoryDone()
        {
            List<QueueLogEntry> entries;
            long queueId;
            int generation;
            lock (_pendingLock)
            {
                entries = _pendingHistory;
                queueId = _pendingHistoryId;
                generation = _pendingHistoryGeneration;
                _pendingHistory = null;
            }

            if (generation != _historyGeneration || !_selectedId.HasValue || _selectedId.Value != queueId)
            {
                return;
            }

            PopulateHistory(entries ?? new List<QueueLogEntry>());
        }

        private void PopulateHistory(List<QueueLogEntry> entries)
        {
            Native.SendMessage(_history, 0x000B, IntPtr.Zero, IntPtr.Zero);
            Native.SendMessage(_history, Native.LVM_DELETEALLITEMS, IntPtr.Zero, IntPtr.Zero);

            for (int i = 0; i < entries.Count; i++)
            {
                QueueLogEntry entry = entries[i];
                Native.LVITEM item = new Native.LVITEM
                {
                    mask = Native.LVIF_TEXT,
                    iItem = i,
                    iSubItem = 0,
                    pszText = entry.AttemptNo.ToString(CultureInfo.InvariantCulture)
                };
                int index = Native.SendMessage(_history, Native.LVM_INSERTITEMW, IntPtr.Zero, ref item)
                    .ToInt32();
                SetHistorySubItem(index, 1, entry.Status.HasValue
                    ? Formatting.StatusName(entry.Status.Value)
                    : "—");
                SetHistorySubItem(index, 2, Formatting.Date(entry.StartedAt));
                SetHistorySubItem(index, 3, Formatting.Duration(entry.DurationMs));
                SetHistorySubItem(index, 4, string.IsNullOrWhiteSpace(entry.ErrorText) ? "—" : entry.ErrorText);
            }

            Native.SendMessage(_history, 0x000B, new IntPtr(1), IntPtr.Zero);
            Native.InvalidateRect(_history, IntPtr.Zero, true);
            SetWindowText(_labelHistory, "История попыток · " +
                                         entries.Count.ToString(CultureInfo.InvariantCulture));
        }

        private void SetHistorySubItem(int itemIndex, int column, string text)
        {
            Native.LVITEM item = new Native.LVITEM
            {
                iSubItem = column,
                pszText = text ?? string.Empty
            };
            Native.SendMessage(_history, Native.LVM_SETITEMTEXTW, new IntPtr(itemIndex), ref item);
        }

        private void ClearHistory()
        {
            Native.SendMessage(_history, Native.LVM_DELETEALLITEMS, IntPtr.Zero, IntPtr.Zero);
            SetWindowText(_labelHistory, "История попыток");
        }

        private string GetText(IntPtr control)
        {
            int length = Native.GetWindowTextLength(control);
            StringBuilder text = new StringBuilder(length + 1);
            Native.GetWindowText(control, text, text.Capacity);
            return text.ToString();
        }

        private void SetupRefreshTimer()
        {
            Native.KillTimer(_hwnd, TimerRefresh);
            if (_autoRefresh && _repository != null)
            {
                Native.SetTimer(_hwnd, TimerRefresh, (uint)(_intervalSeconds * 1000), IntPtr.Zero);
            }
        }

        private void Layout()
        {
            Native.RECT client;
            Native.GetClientRect(_hwnd, out client);
            _clientWidth = client.Right;
            _clientHeight = client.Bottom;

            int margin = S(20);
            int headerH = S(74);
            int bannerH = string.IsNullOrWhiteSpace(_errorText) ? 0 : S(46);
            int summaryH = S(86);
            int filterH = S(32);
            int footerH = S(32);
            int tableY = headerH + bannerH + summaryH + filterH + S(28);
            int detailsH = Math.Max(S(175), Math.Min(S(315), _clientHeight / 3));
            int footerY = _clientHeight - footerH;
            int detailsY = footerY - detailsH - S(10);
            int tableH = Math.Max(S(140), detailsY - tableY - S(8));
            int contentW = _clientWidth - margin * 2;

            // Header
            int refreshW = S(92);
            int chooseW = S(178);
            int statusW = S(112);
            int buttonsY = S(20);
            Native.MoveWindow(_labelTitle, margin, S(11), S(430), S(30), true);
            Native.MoveWindow(_labelSubtitle, margin, S(42), Math.Max(S(200), contentW - chooseW - refreshW - statusW - S(40)),
                S(20), true);

            int refreshX = _clientWidth - margin - refreshW;
            int chooseX = refreshX - S(8) - chooseW;
            int statusX = chooseX - S(10) - statusW;
            Native.MoveWindow(_statusPill, statusX, buttonsY, statusW, S(26), true);
            Native.MoveWindow(_chooseButton, chooseX, buttonsY, chooseW, S(27), true);
            Native.MoveWindow(_refreshButton, refreshX, buttonsY, refreshW, S(27), true);

            // Banner
            Native.MoveWindow(_banner, margin, headerH, contentW, bannerH, true);

            // Summary
            int summaryY = headerH + bannerH + S(12);
            int gap = S(10);
            int cardW = (contentW - gap * 4) / 5;
            for (int i = 0; i < 5; i++)
            {
                Native.MoveWindow(_summaryCards[i], margin + i * (cardW + gap), summaryY, cardW, summaryH, true);
            }

            // Filters and search
            int filtersY = summaryY + summaryH + S(14);
            int buttonW = S(105);
            int buttonGap = S(6);
            for (int i = 0; i < 6; i++)
            {
                Native.MoveWindow(_filterButtons[i], margin + i * (buttonW + buttonGap), filtersY,
                    buttonW, filterH, true);
            }

            int searchW = S(320);
            int searchX = _clientWidth - margin - searchW;
            int searchY = filtersY;
            Native.MoveWindow(_search, searchX, searchY, searchW - S(30), filterH, true);
            Native.MoveWindow(_clearSearch, searchX + searchW - S(28), searchY, S(28), filterH, true);

            // Main table
            Native.MoveWindow(_list, margin, tableY, contentW, tableH, true);
            SetListColumns(contentW);

            // Details
            int detailsLeftW = (contentW * 55) / 100;
            int detailsRightX = margin + detailsLeftW + S(18);
            int detailsRightW = _clientWidth - margin - detailsRightX;
            Native.MoveWindow(_labelDetails, margin, detailsY, detailsLeftW, S(22), true);
            Native.MoveWindow(_details, margin, detailsY + S(24), detailsLeftW, detailsH - S(28), true);
            Native.MoveWindow(_labelHistory, detailsRightX, detailsY, detailsRightW, S(22), true);
            Native.MoveWindow(_history, detailsRightX, detailsY + S(24), detailsRightW, detailsH - S(28), true);
            SetHistoryColumns(detailsRightW);

            // Footer
            int autoW = S(220);
            int autoX = _clientWidth - margin - autoW;
            int footerTextW = Math.Max(S(200), autoX - margin - S(16));
            int configW = (footerTextW * 48) / 100;
            int countsW = (footerTextW * 28) / 100;
            int updatedW = Math.Max(S(120), footerTextW - configW - countsW - S(16));
            Native.MoveWindow(_footerConfig, margin, footerY + S(8), configW, S(18), true);
            Native.MoveWindow(_footerCounts, margin + configW + S(8), footerY + S(8),
                countsW, S(18), true);
            Native.MoveWindow(_footerUpdated, margin + configW + S(8) + countsW + S(8),
                footerY + S(8), updatedW, S(18), true);

            Native.MoveWindow(_labelAuto, autoX, footerY + S(8), S(98), S(18), true);
            Native.MoveWindow(_autoCheck, autoX + S(100), footerY + S(8), S(18), S(18), true);
            Native.MoveWindow(_intervalCombo, autoX + S(124), footerY + S(4), S(64), S(200), true);
            Native.MoveWindow(_labelSeconds, autoX + S(192), footerY + S(8), S(14), S(18), true);
            Native.MoveWindow(_footerVersion, -S(100), -S(100), S(1), S(1), true);

            Native.InvalidateRect(_hwnd, IntPtr.Zero, true);
        }

        private void SetListColumns(int width)
        {
            int[] widths =
            {
                S(55), S(95), S(205), S(210), S(72), S(130), S(130), S(72), 0
            };
            int fixedWidth = widths.Take(8).Sum();
            widths[8] = Math.Max(S(160), width - fixedWidth - S(20));
            for (int i = 0; i < widths.Length; i++)
            {
                Native.SendMessage(_list, Native.LVM_SETCOLUMNWIDTH, new IntPtr(i),
                    new IntPtr(widths[i]));
            }
        }

        private void SetHistoryColumns(int width)
        {
            int[] widths = { S(38), S(90), S(130), S(65), 0 };
            int fixedWidth = widths.Take(4).Sum();
            widths[4] = Math.Max(S(100), width - fixedWidth - S(20));
            for (int i = 0; i < widths.Length; i++)
            {
                Native.SendMessage(_history, Native.LVM_SETCOLUMNWIDTH, new IntPtr(i),
                    new IntPtr(widths[i]));
            }
        }

        private void OnDrawItem(IntPtr lParam)
        {
            Native.DRAWITEMSTRUCT item = (Native.DRAWITEMSTRUCT)Marshal.PtrToStructure(
                lParam, typeof(Native.DRAWITEMSTRUCT));

            if (item.CtlID >= IdFilterBase && item.CtlID < IdFilterBase + 6)
            {
                DrawFilterButton(item, (int)(item.CtlID - IdFilterBase));
            }
            else if (item.CtlID >= IdSummaryBase && item.CtlID < IdSummaryBase + 5)
            {
                DrawSummary(item, (int)(item.CtlID - IdSummaryBase));
            }
            else if (item.CtlID == IdStatusPill)
            {
                DrawStatus(item);
            }
            else if (item.CtlID == IdBanner)
            {
                DrawBanner(item);
            }
        }

        private void DrawFilterButton(Native.DRAWITEMSTRUCT item, int index)
        {
            bool active = (int)_filter == index;
            bool pressed = (item.itemState & Native.ODS_SELECTED) != 0;
            uint back = active ? ColorAccent : ColorWhite;
            uint border = active ? ColorAccent : ColorBorder;
            uint fore = active ? ColorWhite : Native.Rgb(49, 64, 82);
            if (pressed)
            {
                back = active ? Native.Rgb(29, 78, 216) : Native.Rgb(245, 248, 254);
            }

            DrawRoundedButton(item.hDC, item.rcItem, back, border, ColorWhite,
                FilterNames[index] + "  " + _filterCounts[index], fore, _fontButton);
        }

        private void DrawRoundedButton(IntPtr hdc, Native.RECT rect, uint back, uint border,
            uint textBack, string text, uint fore, IntPtr font)
        {
            IntPtr brush = Native.CreateSolidBrush(back);
            IntPtr pen = Native.CreatePen(0, 1, border);
            IntPtr oldBrush = Native.SelectObject(hdc, brush);
            IntPtr oldPen = Native.SelectObject(hdc, pen);
            Native.RoundRect(hdc, rect.Left, rect.Top, rect.Right, rect.Bottom, S(16), S(16));
            Native.SelectObject(hdc, oldBrush);
            Native.SelectObject(hdc, oldPen);
            Native.DeleteObject(brush);
            Native.DeleteObject(pen);

            Native.SetBkMode(hdc, Native.TRANSPARENT);
            Native.SetTextColor(hdc, fore);
            IntPtr oldFont = Native.SelectObject(hdc, font);
            Native.DrawText(hdc, text, text.Length, ref rect,
                Native.DT_CENTER | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
            Native.SelectObject(hdc, oldFont);
        }

        private void DrawSummary(Native.DRAWITEMSTRUCT item, int index)
        {
            Native.RECT rect = item.rcItem;
            IntPtr white = Native.CreateSolidBrush(ColorWhite);
            IntPtr border = Native.CreatePen(0, 1, ColorBorder);
            IntPtr oldBrush = Native.SelectObject(item.hDC, white);
            IntPtr oldPen = Native.SelectObject(item.hDC, border);
            Native.RoundRect(item.hDC, rect.Left, rect.Top, rect.Right, rect.Bottom, S(20), S(20));
            Native.SelectObject(item.hDC, oldBrush);
            Native.SelectObject(item.hDC, oldPen);
            Native.DeleteObject(white);
            Native.DeleteObject(border);

            Native.RECT accent = rect;
            accent.Right = accent.Left + S(4);
            accent.Top += S(10);
            accent.Bottom -= S(10);
            IntPtr accentBrush = Native.CreateSolidBrush(SummaryColors[index]);
            Native.FillRect(item.hDC, ref accent, accentBrush);
            Native.DeleteObject(accentBrush);

            Native.SetBkMode(item.hDC, Native.TRANSPARENT);
            Native.SetTextColor(item.hDC, ColorMuted);
            Native.RECT labelRect = rect;
            labelRect.Left += S(16);
            labelRect.Top += S(10);
            labelRect.Right -= S(12);
            labelRect.Bottom = labelRect.Top + S(18);
            IntPtr oldFont = Native.SelectObject(item.hDC, _fontLabel);
            Native.DrawText(item.hDC, SummaryLabels[index], SummaryLabels[index].Length,
                ref labelRect, Native.DT_LEFT | Native.DT_SINGLELINE | Native.DT_NOPREFIX);

            Native.SetTextColor(item.hDC, ColorText);
            Native.SelectObject(item.hDC, _fontValue);
            Native.RECT valueRect = rect;
            valueRect.Left += S(16);
            valueRect.Top += S(27);
            valueRect.Right -= S(12);
            valueRect.Bottom = valueRect.Top + S(32);
            Native.DrawText(item.hDC, _summaryValues[index], _summaryValues[index].Length,
                ref valueRect, Native.DT_LEFT | Native.DT_SINGLELINE | Native.DT_NOPREFIX);

            Native.SetTextColor(item.hDC, ColorMuted);
            Native.SelectObject(item.hDC, _fontSmall);
            Native.RECT subRect = rect;
            subRect.Left += S(16);
            subRect.Top += S(61);
            subRect.Right -= S(12);
            subRect.Bottom = subRect.Top + S(18);
            string sub = SummarySubs[index];
            Native.DrawText(item.hDC, sub, sub.Length, ref subRect,
                Native.DT_LEFT | Native.DT_SINGLELINE | Native.DT_END_ELLIPSIS | Native.DT_NOPREFIX);
            Native.SelectObject(item.hDC, oldFont);
        }

        private void DrawStatus(Native.DRAWITEMSTRUCT item)
        {
            Native.RECT rect = item.rcItem;
            IntPtr brush = Native.CreateSolidBrush(_statusBackColor);
            IntPtr oldBrush = Native.SelectObject(item.hDC, brush);
            IntPtr pen = Native.CreatePen(0, 1, _statusBackColor);
            IntPtr oldPen = Native.SelectObject(item.hDC, pen);
            Native.RoundRect(item.hDC, rect.Left, rect.Top, rect.Right, rect.Bottom, S(20), S(20));
            Native.SelectObject(item.hDC, oldBrush);
            Native.SelectObject(item.hDC, oldPen);
            Native.DeleteObject(brush);
            Native.DeleteObject(pen);

            Native.RECT dot = rect;
            dot.Left += S(12);
            dot.Right = dot.Left + S(7);
            dot.Top = rect.Top + (rect.Bottom - rect.Top - S(7)) / 2;
            dot.Bottom = dot.Top + S(7);
            IntPtr dotBrush = Native.CreateSolidBrush(_statusTextColor);
            Native.FillRect(item.hDC, ref dot, dotBrush);
            Native.DeleteObject(dotBrush);

            Native.SetBkMode(item.hDC, Native.TRANSPARENT);
            Native.SetTextColor(item.hDC, _statusTextColor);
            IntPtr oldFont = Native.SelectObject(item.hDC, _fontButton);
            Native.RECT textRect = rect;
            textRect.Left += S(26);
            textRect.Right -= S(8);
            Native.DrawText(item.hDC, _statusText, _statusText.Length, ref textRect,
                Native.DT_LEFT | Native.DT_VCENTER | Native.DT_SINGLELINE | Native.DT_NOPREFIX);
            Native.SelectObject(item.hDC, oldFont);
        }

        private void DrawBanner(Native.DRAWITEMSTRUCT item)
        {
            if (string.IsNullOrWhiteSpace(_errorText))
            {
                return;
            }

            Native.RECT rect = item.rcItem;
            IntPtr brush = Native.CreateSolidBrush(ColorRedSoft);
            IntPtr pen = Native.CreatePen(0, 1, Native.Rgb(254, 202, 202));
            IntPtr oldBrush = Native.SelectObject(item.hDC, brush);
            IntPtr oldPen = Native.SelectObject(item.hDC, pen);
            Native.RoundRect(item.hDC, rect.Left, rect.Top, rect.Right, rect.Bottom, S(14), S(14));
            Native.SelectObject(item.hDC, oldBrush);
            Native.SelectObject(item.hDC, oldPen);
            Native.DeleteObject(brush);
            Native.DeleteObject(pen);

            Native.SetBkMode(item.hDC, Native.TRANSPARENT);
            Native.SetTextColor(item.hDC, ColorRed);
            IntPtr oldFont = Native.SelectObject(item.hDC, _fontSmall);
            Native.RECT textRect = rect;
            textRect.Left += S(14);
            textRect.Top += S(7);
            textRect.Right -= S(12);
            textRect.Bottom -= S(7);
            Native.DrawText(item.hDC, _errorText, _errorText.Length, ref textRect,
                Native.DT_LEFT | Native.DT_WORDBREAK | Native.DT_END_ELLIPSIS | Native.DT_NOPREFIX);
            Native.SelectObject(item.hDC, oldFont);
        }

        private void InvalidateFilters()
        {
            foreach (IntPtr button in _filterButtons)
            {
                Native.InvalidateRect(button, IntPtr.Zero, true);
            }
        }

        private void InvalidateSummary()
        {
            foreach (IntPtr card in _summaryCards)
            {
                Native.InvalidateRect(card, IntPtr.Zero, true);
            }
        }

        private void InvalidateStatus()
        {
            Native.InvalidateRect(_statusPill, IntPtr.Zero, true);
        }

        private void InvalidateBanner()
        {
            Native.InvalidateRect(_banner, IntPtr.Zero, true);
        }

        private uint StatusColor(int status)
        {
            switch (status)
            {
                case 0: return Native.Rgb(29, 78, 216);
                case 1: return Native.Rgb(180, 83, 9);
                case 2: return Native.Rgb(21, 128, 61);
                case 3: return Native.Rgb(180, 83, 9);
                case 4: return Native.Rgb(185, 28, 28);
                case 5: return Native.Rgb(71, 85, 105);
                default: return Native.Rgb(71, 85, 105);
            }
        }

        private void CaptureScreenshot()
        {
            try
            {
                Native.RECT rect;
                Native.GetWindowRect(_hwnd, out rect);
                int width = Math.Max(1, rect.Right - rect.Left);
                int height = Math.Max(1, rect.Bottom - rect.Top);
                using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        IntPtr hdc = graphics.GetHdc();
                        Native.PrintWindow(_hwnd, hdc, 2);
                        graphics.ReleaseHdc(hdc);
                    }

                    string path = Path.GetFullPath(_screenshotPath);
                    string folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    bitmap.Save(path, ImageFormat.Png);
                }
            }
            catch
            {
            }
        }

        private static string FriendlyError(string message)
        {
            if (message.IndexOf("Table unknown", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("PARSEC_QUEUE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "В базе нет таблицы PARSEC_QUEUE. Примените db/01..03 (при обновлении с 4.0.0 — db/06). " +
                       "Ответ базы: " + message;
            }

            return message;
        }

        private static string Sanitize(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "неизвестная ошибка";
            }

            string result = Regex.Replace(message,
                @"(?i)\b(password|pwd|пароль)\s*=\s*[^;]*", "$1=***");
            result = result.Replace("\r", " ").Replace("\n", " ").Trim();
            return result.Length > 700 ? result.Substring(0, 700) + "…" : result;
        }

        private void DisposeResources()
        {
            IntPtr[] fonts =
            {
                _fontTitle, _fontSubtitle, _fontLabel, _fontSmall, _fontButton,
                _fontValue, _fontList, _fontDetails, _fontFooter
            };
            foreach (IntPtr font in fonts)
            {
                if (font != IntPtr.Zero)
                {
                    Native.DeleteObject(font);
                }
            }

            IntPtr[] brushes = { _brushPage, _brushWhite, _brushBanner, _brushGreen, _brushAmber, _brushRed };
            foreach (IntPtr brush in brushes)
            {
                if (brush != IntPtr.Zero)
                {
                    Native.DeleteObject(brush);
                }
            }
        }

        public void Dispose()
        {
        }

        private sealed class DisplayRow
        {
            public QueueRow Row;
            public string Operation;
            public string ObjectTitle;
            public string ObjectSubtitle;
            public string SearchText;
        }

        private sealed class ViewerSettings
        {
            private static readonly string SettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ParsecQueueViewerWin32", "settings.ini");

            public string ConfigPath;
            public bool AutoRefresh = true;
            public int IntervalSeconds = 10;

            public void Load()
            {
                try
                {
                    if (!File.Exists(SettingsPath))
                    {
                        return;
                    }

                    foreach (string line in File.ReadAllLines(SettingsPath))
                    {
                        int separator = line.IndexOf('=');
                        if (separator <= 0)
                        {
                            continue;
                        }

                        string key = line.Substring(0, separator).Trim();
                        string value = line.Substring(separator + 1).Trim();
                        if (string.Equals(key, "Config", StringComparison.OrdinalIgnoreCase))
                        {
                            ConfigPath = value;
                        }
                        else if (string.Equals(key, "Auto", StringComparison.OrdinalIgnoreCase))
                        {
                            bool auto;
                            if (bool.TryParse(value, out auto))
                            {
                                AutoRefresh = auto;
                            }
                        }
                        else if (string.Equals(key, "Interval", StringComparison.OrdinalIgnoreCase))
                        {
                            int seconds;
                            if (int.TryParse(value, out seconds) && seconds >= 5 && seconds <= 3600)
                            {
                                IntervalSeconds = seconds;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            public void Save()
            {
                try
                {
                    string folder = Path.GetDirectoryName(SettingsPath);
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        Directory.CreateDirectory(folder);
                    }

                    File.WriteAllLines(SettingsPath, new[]
                    {
                        "Config=" + (ConfigPath ?? string.Empty),
                        "Auto=" + AutoRefresh.ToString(),
                        "Interval=" + IntervalSeconds.ToString(CultureInfo.InvariantCulture)
                    });
                }
                catch
                {
                }
            }
        }
    }
}

