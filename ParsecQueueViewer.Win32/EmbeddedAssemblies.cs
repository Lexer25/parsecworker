using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ParsecQueueViewer.Win32
{
    /// <summary>
    /// Загружает managed-зависимости из ресурсов exe. Нужно, чтобы поставлять
    /// FirebirdSql.Data.FirebirdClient, Tasks.Extensions и Unsafe одним файлом;
    /// внешним остаётся только native fbclient.dll.
    /// </summary>
    internal static class EmbeddedAssemblies
    {
        private static readonly Dictionary<string, string> Resources =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "FirebirdSql.Data.FirebirdClient",
                    "ParsecQueueViewerWin32.Embedded.FirebirdSql.Data.FirebirdClient.dll"
                },
                {
                    "System.Threading.Tasks.Extensions",
                    "ParsecQueueViewerWin32.Embedded.System.Threading.Tasks.Extensions.dll"
                },
                {
                    "System.Runtime.CompilerServices.Unsafe",
                    "ParsecQueueViewerWin32.Embedded.System.Runtime.CompilerServices.Unsafe.dll"
                }
            };

        private static readonly Dictionary<string, Assembly> Cache =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new object();

        public static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Name))
            {
                return null;
            }

            string simpleName = new AssemblyName(args.Name).Name;
            string resourceName;
            if (simpleName == null || !Resources.TryGetValue(simpleName, out resourceName))
            {
                return null;
            }

            lock (Sync)
            {
                Assembly cached;
                if (Cache.TryGetValue(simpleName, out cached))
                {
                    return cached;
                }

                using (Stream stream = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (MemoryStream memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        Assembly loaded = Assembly.Load(memory.ToArray());
                        Cache[simpleName] = loaded;
                        return loaded;
                    }
                }
            }
        }
    }
}
