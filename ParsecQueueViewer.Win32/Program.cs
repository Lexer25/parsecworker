using System;

namespace ParsecQueueViewer.Win32
{
    internal static class Program
    {
        private static void HideOwnConsole()
        {
            try
            {
                IntPtr window = Native.GetConsoleWindow();
                if (window == IntPtr.Zero)
                {
                    return;
                }

                uint[] processes = new uint[2];
                uint count = Native.GetConsoleProcessList(processes, (uint)processes.Length);
                if (count <= 1)
                {
                    Native.ShowWindow(window, 0);
                    Native.FreeConsole();
                }
            }
            catch
            {
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            EmbeddedAssemblies.Register();
            HideOwnConsole();
            try
            {
                Native.SetProcessDPIAware();

                Native.INITCOMMONCONTROLSEX icc = new Native.INITCOMMONCONTROLSEX
                {
                    dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(
                        typeof(Native.INITCOMMONCONTROLSEX)),
                    dwICC = 0x00000004 /* ICC_LISTVIEW_CLASSES */ | 0x00000001 /* ICC_STANDARD_CLASSES */
                };
                Native.InitCommonControlsEx(ref icc);

                string configPath = null;
                string screenshotPath = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < args.Length)
                    {
                        configPath = args[++i];
                    }
                    else if (args[i].StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                    {
                        configPath = args[i].Substring("--config=".Length);
                    }
                    else if (string.Equals(args[i], "--screenshot", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < args.Length)
                    {
                        screenshotPath = args[++i];
                    }
                    else if (args[i].StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase))
                    {
                        screenshotPath = args[i].Substring("--screenshot=".Length);
                    }
                }

                using (MainWindow window = new MainWindow(configPath, screenshotPath))
                {
                    return window.Run();
                }
            }
            catch (Exception ex)
            {
                Native.MessageBox(IntPtr.Zero,
                    "Не удалось запустить очередь интегратора.\r\n\r\n" + ex.Message,
                    "Очередь интегратора (Win32)", 0x10);
                return 1;
            }
        }
    }
}
