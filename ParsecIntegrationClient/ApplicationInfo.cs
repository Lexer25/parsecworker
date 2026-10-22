using System;
using System.Diagnostics;
using System.Reflection;

namespace ParsecIntegrationClient
{
    /// <summary>Сведения о программе для записей журнала.</summary>
    public static class ApplicationInfo
    {
        /// <summary>Версия сборки (пишется в журнал при старте и остановке).</summary>
        public static string Version
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "unknown" : version.ToString();
            }
        }

        /// <summary>Полный путь к exe: в журнале видно, из какого каталога запущена служба.</summary>
        public static string ExecutablePath
        {
            get
            {
                try
                {
                    return Process.GetCurrentProcess().MainModule.FileName;
                }
                catch (Exception)
                {
                    return AppDomain.CurrentDomain.BaseDirectory;
                }
            }
        }
    }
}
