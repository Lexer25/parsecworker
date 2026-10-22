using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ParsecIntegrationClient
{
    /// <summary>
    /// Загрузчик сборок, встроенных в exe как ресурсы: поставка из одного файла.
    ///
    /// Раньше это делал Costura.Fody (и тянул за собой Fody). Здесь то же самое без сторонних
    /// библиотек: MSBuild кладёт DLL в ресурсы exe (см. target EmbedReferencedAssemblies в csproj),
    /// а этот обработчик отдаёт их среде по первому требованию.
    /// </summary>
    internal static class EmbeddedAssemblies
    {
        private static readonly object SyncRoot = new object();
        private static bool _registered;

        /// <summary>
        /// Подключает обработчик. Вызывается первой строкой Main — до любого обращения
        /// к типам из встроенных сборок.
        /// </summary>
        public static void Register()
        {
            lock (SyncRoot)
            {
                if (_registered)
                {
                    return;
                }

                _registered = true;
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            }
        }

        /// <summary>
        /// Диагностика поставки: загружает каждую встроенную сборку и перечисляет её типы.
        /// Нужна, чтобы проверить «один файл» на стенде без Парсека и базы.
        /// </summary>
        public static List<string> Verify()
        {
            List<string> failures = new List<string>();
            Assembly self = Assembly.GetExecutingAssembly();
            string[] names = self.GetManifestResourceNames();
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);

            foreach (string resourceName in names)
            {
                try
                {
                    Assembly assembly = Assembly.Load(ReadResource(self, resourceName));

                    // GetTypes() заодно требует загрузки зависимостей сборки.
                    Type[] types = assembly.GetTypes();
                    Console.WriteLine("  OK   {0} ({1} типов)", resourceName, types.Length);
                }
                catch (Exception ex)
                {
                    failures.Add(resourceName + ": " + ex.Message);
                    Console.WriteLine("  FAIL {0}: {1}", resourceName, ex.Message);
                }
            }

            if (names.Length == 0)
            {
                failures.Add("в exe нет встроенных сборок (соберите с EmbedAssembliesIntoExe=true)");
            }

            return failures;
        }

        private static byte[] ReadResource(Assembly assembly, string resourceName)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("ресурс не найден: " + resourceName);
                }

                byte[] buffer = new byte[stream.Length];
                int offset = 0;

                while (offset < buffer.Length)
                {
                    int read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0)
                    {
                        break;
                    }

                    offset += read;
                }

                return buffer;
            }
        }

        /// <summary>Ищет запрошенную сборку среди ресурсов exe и загружает её из памяти.</summary>
        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                AssemblyName requested = new AssemblyName(args.Name);
                if (string.IsNullOrEmpty(requested.Name))
                {
                    return null;
                }

                Assembly self = Assembly.GetExecutingAssembly();
                string resourceName = requested.Name + ".dll";

                using (Stream stream = self.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        // Сборки нет среди встроенных — пусть сработает стандартное разрешение.
                        return null;
                    }

                    byte[] buffer = new byte[stream.Length];
                    int offset = 0;

                    while (offset < buffer.Length)
                    {
                        int read = stream.Read(buffer, offset, buffer.Length - offset);
                        if (read <= 0)
                        {
                            break;
                        }

                        offset += read;
                    }

                    return Assembly.Load(buffer);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

