using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ParsecQueueViewer.Infrastructure
{
    /// <summary>
    /// Приложение собирается как Exe (console subsystem), потому что self-contained
    /// WinExe-хост WPF на целевом стенде не доходит до managed Main. Консоль нужна
    /// только как обход этой проблемы: при одиночном запуске (двойной щелчок,
    /// ярлык) сразу прячем её. Если приложение запущено из уже открытой консоли,
    /// чужое окно не трогаем.
    /// </summary>
    internal static class ConsoleWindowHider
    {
        private const int SwHide = 0;

        [ModuleInitializer]
        internal static void HideOwnConsole()
        {
            try
            {
                IntPtr window = GetConsoleWindow();
                if (window == IntPtr.Zero)
                {
                    return;
                }

                uint[] processes = new uint[2];
                uint count = GetConsoleProcessList(processes, (uint)processes.Length);
                if (count <= 1)
                {
                    ShowWindow(window, SwHide);
                }
            }
            catch
            {
                // Скрытие консоли — косметика; без него приложение тоже работает.
            }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
