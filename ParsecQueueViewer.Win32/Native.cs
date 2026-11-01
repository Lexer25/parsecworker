using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ParsecQueueViewer.Win32
{
    internal static class Native
    {
        public const int WM_CREATE = 0x0001;
        public const int WM_DESTROY = 0x0002;
        public const int WM_SIZE = 0x0005;
        public const int WM_PAINT = 0x000F;
        public const int WM_CLOSE = 0x0010;
        public const int WM_ERASEBKGND = 0x0014;
        public const int WM_GETMINMAXINFO = 0x0024;
        public const int WM_DRAWITEM = 0x002B;
        public const int WM_COMMAND = 0x0111;
        public const int WM_TIMER = 0x0113;
        public const int WM_NOTIFY = 0x004E;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SETFONT = 0x0030;
        public const int WM_CTLCOLOREDIT = 0x0133;
        public const int WM_CTLCOLORSTATIC = 0x0138;
        public const int WM_APP = 0x8000;
        public const int WM_REFRESH_DONE = WM_APP + 10;
        public const int WM_HISTORY_DONE = WM_APP + 11;

        public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        public const int WS_CHILD = 0x40000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_TABSTOP = 0x00010000;
        public const int WS_VSCROLL = 0x00200000;
        public const int WS_BORDER = 0x00800000;
        public const int WS_EX_CLIENTEDGE = 0x00000200;
        public const int WS_EX_APPWINDOW = 0x00040000;

        public const int SS_LEFT = 0x00000000;
        public const int SS_CENTER = 0x00000001;
        public const int SS_NOPREFIX = 0x00000080;
        public const int SS_OWNERDRAW = 0x0000000D;

        public const int ES_MULTILINE = 0x0004;
        public const int ES_AUTOVSCROLL = 0x0040;
        public const int ES_AUTOHSCROLL = 0x0080;
        public const int ES_READONLY = 0x0800;
        public const int ES_WANTRETURN = 0x1000;

        public const int BS_PUSHBUTTON = 0x00000000;
        public const int BS_AUTOCHECKBOX = 0x00000003;
        public const int BS_OWNERDRAW = 0x0000000B;

        public const int CBS_DROPDOWNLIST = 0x0003;
        public const int CBS_HASSTRINGS = 0x0200;

        public const int BN_CLICKED = 0;
        public const int EN_CHANGE = 0x0300;
        public const int CBN_SELCHANGE = 1;

        public const int LVS_REPORT = 0x0001;
        public const int LVS_SINGLESEL = 0x0004;
        public const int LVS_SHOWSELALWAYS = 0x0008;
        public const int LVS_NOSORTHEADER = 0x8000;
        public const int LVS_EX_FULLROWSELECT = 0x00000020;
        public const int LVS_EX_DOUBLEBUFFER = 0x00010000;
        public const int LVS_EX_GRIDLINES = 0x00000001;

        public const int LVM_FIRST = 0x1000;
        public const int LVM_DELETEALLITEMS = LVM_FIRST + 9;
        public const int LVM_GETNEXTITEM = LVM_FIRST + 12;
        public const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
        public const int LVM_INSERTCOLUMNW = LVM_FIRST + 97;
        public const int LVM_INSERTITEMW = LVM_FIRST + 77;
        public const int LVM_SETITEMTEXTW = LVM_FIRST + 116;
        public const int LVM_SETITEMSTATE = LVM_FIRST + 43;
        public const int LVM_SETCOLUMNWIDTH = LVM_FIRST + 30;
        public const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
        public const int LVM_ENSUREVISIBLE = LVM_FIRST + 19;
        public const int LVM_SETBKCOLOR = LVM_FIRST + 1;
        public const int LVM_SETTEXTCOLOR = LVM_FIRST + 36;
        public const int LVM_SETTEXTBKCOLOR = LVM_FIRST + 38;

        public const int LVIF_TEXT = 0x0001;
        public const int LVIF_PARAM = 0x0004;
        public const int LVIS_SELECTED = 0x0002;
        public const int LVIS_FOCUSED = 0x0001;
        public const int LVNI_SELECTED = 0x0002;

        public const int LVN_FIRST = -100;
        public const int LVN_ITEMCHANGED = LVN_FIRST - 1;
        public const int LVN_COLUMNCLICK = LVN_FIRST - 8;
        public const int NM_CUSTOMDRAW = -12;

        public const int CDDS_PREPAINT = 0x00000001;
        public const int CDDS_ITEMPREPAINT = 0x00010001;
        public const int CDDS_SUBITEM = 0x00020000;
        public const int CDRF_DODEFAULT = 0x00000000;
        public const int CDRF_NEWFONT = 0x00000002;
        public const int CDRF_NOTIFYITEMDRAW = 0x00000020;
        public const int CDRF_NOTIFYSUBITEMDRAW = 0x00000020;

        public const int ODS_SELECTED = 0x0001;
        public const int ODS_DISABLED = 0x0004;
        public const int ODS_FOCUS = 0x0010;
        public const int ODT_STATIC = 6;

        public const int DT_LEFT = 0x00000000;
        public const int DT_CENTER = 0x00000001;
        public const int DT_VCENTER = 0x00000004;
        public const int DT_SINGLELINE = 0x00000020;
        public const int DT_WORDBREAK = 0x00000010;
        public const int DT_END_ELLIPSIS = 0x00008000;
        public const int DT_NOPREFIX = 0x00000800;

        public const int TRANSPARENT = 1;
        public const int LOGPIXELSX = 88;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        public const int SW_SHOW = 5;
        public const int VK_F5 = 0x74;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_CONTROL = 0x11;
        public const int VK_F = 0x46;
        public const int EM_SETCUEBANNER = 0x1501;

        public const int FW_NORMAL = 400;
        public const int FW_SEMIBOLD = 600;
        public const int FW_BOLD = 700;
        public const int DEFAULT_CHARSET = 1;
        public const int OUT_DEFAULT_PRECIS = 0;
        public const int CLIP_DEFAULT_PRECIS = 0;
        public const int CLEARTYPE_QUALITY = 5;
        public const int DEFAULT_PITCH = 0;
        public const int FF_DONTCARE = 0;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int SWP_NOSIZE = 0x0001;
        public const int SWP_NOMOVE = 0x0002;
        public const int SWP_NOZORDER = 0x0004;

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INITCOMMONCONTROLSEX
        {
            public uint dwSize;
            public uint dwICC;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NMHDR
        {
            public IntPtr hwndFrom;
            public IntPtr idFrom;
            public uint code;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NMLISTVIEW
        {
            public NMHDR hdr;
            public int iItem;
            public int iSubItem;
            public uint uNewState;
            public uint uOldState;
            public uint uChanged;
            public POINT ptAction;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NMCUSTOMDRAW
        {
            public NMHDR hdr;
            public uint dwDrawStage;
            public IntPtr hdc;
            public RECT rc;
            public IntPtr dwItemSpec;
            public uint uItemState;
            public IntPtr lItemlParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NMLVCUSTOMDRAW
        {
            public NMCUSTOMDRAW nmcd;
            public uint clrText;
            public uint clrTextBk;
            public int iSubItem;
            public uint dwItemType;
            public uint clrFace;
            public int iIconEffect;
            public int iIconPhase;
            public int iPartId;
            public int iStateId;
            public RECT rcText;
            public uint uAlign;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LVCOLUMN
        {
            public uint mask;
            public int fmt;
            public int cx;
            public string pszText;
            public int cchText;
            public int iSubItem;
            public int iImage;
            public int iOrder;
            public int cxMin;
            public int cxDefault;
            public int cxIdeal;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LVITEM
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public string pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct OPENFILENAME
        {
            public uint lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public IntPtr lpstrFilter;
            public IntPtr lpstrCustomFilter;
            public uint nMaxCustFilter;
            public uint nFilterIndex;
            public IntPtr lpstrFile;
            public uint nMaxFile;
            public IntPtr lpstrFileTitle;
            public uint nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public uint Flags;
            public ushort nFileOffset;
            public ushort nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
            public IntPtr pvReserved;
            public uint dwReserved;
            public uint FlagsEx;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DRAWITEMSTRUCT
        {
            public uint CtlType;
            public uint CtlID;
            public uint itemID;
            public uint itemAction;
            public uint itemState;
            public IntPtr hwndItem;
            public IntPtr hDC;
            public RECT rcItem;
            public IntPtr itemData;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        [DllImport("kernel32.dll")]
        public static extern bool FreeConsole();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVCOLUMN lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVITEM lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        public static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

        [DllImport("user32.dll")]
        public static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreatePen(int fnPenStyle, int nWidth, uint color);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern bool FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

        [DllImport("gdi32.dll")]
        public static extern int SetBkMode(IntPtr hdc, int mode);

        [DllImport("gdi32.dll")]
        public static extern uint SetTextColor(IntPtr hdc, uint color);

        [DllImport("gdi32.dll")]
        public static extern uint SetBkColor(IntPtr hdc, uint color);

        [DllImport("gdi32.dll")]
        public static extern bool RoundRect(IntPtr hdc, int left, int top, int right, int bottom,
            int width, int height);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int DrawText(IntPtr hdc, string lpchText, int cchText,
            ref RECT lprc, uint dwDTFormat);

        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFont(
            int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
            uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
            uint iOutPrecision, uint iClipPrecision, uint iQuality,
            uint iPitchAndFamily, string pszFaceName);

        [DllImport("comctl32.dll")]
        public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName,
            string pszSubIdList);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        public static uint Rgb(int r, int g, int b)
        {
            return (uint)(r | (g << 8) | (b << 16));
        }

        public static int LowWord(IntPtr value)
        {
            return (int)(value.ToInt64() & 0xFFFF);
        }

        public static int HighWord(IntPtr value)
        {
            return (int)((value.ToInt64() >> 16) & 0xFFFF);
        }
    }
}
