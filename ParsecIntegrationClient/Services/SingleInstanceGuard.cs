using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using ParsecIntegrationClient.Services.Logging;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// Защита от второго экземпляра: один именованный мьютекс на всю машину.
    ///
    /// Служба и консольный запуск одного и того же exe, а также две копии в разных
    /// каталогах используют одно и то же имя, поэтому второй процесс завершается,
    /// не начиная обработку очереди. Имя с префиксом Global действует на всю машину
    /// (включая сеансы RDP).
    ///
    /// Если у пользователя нет права создавать объекты в глобальном пространстве
    /// имён (не администратор и не служба), запуск всё равно блокируется, когда на
    /// машине видна работающая копия; если копий нет — защита сужается до текущего
    /// сеанса, о чём пишется предупреждение в журнал.
    ///
    /// Мьютекс захватывается главным потоком при старте и удерживается до выхода
    /// из процесса. Если процесс завершился аварийно, следующий запуск получит
    /// AbandonedMutexException и продолжит работу: «зависшей» блокировки не будет.
    /// </summary>
    public static class SingleInstanceGuard
    {
        private const string GlobalMutexName = @"Global\ParsecIntegrationClient.SingleInstance";
        private const string LocalMutexName = @"Local\ParsecIntegrationClient.SingleInstance";

        private static Mutex _mutex;
        private static bool _owned;

        /// <summary>
        /// Пытается стать единственным экземпляром. Возвращает false, если уже
        /// работает другой процесс; в <paramref name="reason"/> — готовый текст
        /// для журнала и консоли.
        /// </summary>
        public static bool TryAcquire(out string reason)
        {
            reason = null;

            if (TryAcquireCore(GlobalMutexName, out bool accessDenied))
            {
                return true;
            }

            if (!accessDenied)
            {
                reason = BuildBusyMessage();
                return false;
            }

            // UnauthorizedAccessException означает одно из двух:
            //   * у процесса нет SeCreateGlobalPrivilege (обычный пользователь) — тогда
            //     глобальный объект создать нельзя вообще;
            //   * право есть, но существующий мьютекс закрыт DACL — значит, экземпляр
            //     уже работает, и запускать второй нельзя.
            // Различаем попыткой создать заведомо новый глобальный объект.
            if (CanCreateGlobalObject())
            {
                reason = BuildBusyMessage();
                return false;
            }

            // Права на глобальные объекты нет. Если на машине всё равно видна наша
            // копия (например, служба под LocalService), второй запуск запрещён:
            // слабое место глобального имени здесь не должно превращаться в дыру.
            if (IsAnotherInstanceVisible())
            {
                reason = BuildBusyMessage();
                return false;
            }

            // Обычный пользователь без SeCreateGlobalPrivilege не может создавать
            // объекты в глобальном пространстве имён. Не отказываем в запуске:
            // работаем в пределах сеанса, но сообщаем об ослаблении защиты.
            LegacyFileLoggerProvider.Append(
                Paths.LogFolder,
                "Program",
                LogLevel.Warning,
                "Нет права на глобальный мьютекс " + GlobalMutexName +
                ": защита от второго экземпляра действует только в пределах сеанса пользователя.",
                null);

            if (TryAcquireCore(LocalMutexName, out _))
            {
                return true;
            }

            reason = BuildBusyMessage();
            return false;
        }

        /// <summary>Освобождает мьютекс при штатном выходе. Вызывается из того же потока, что и захват.</summary>
        public static void Release()
        {
            Mutex mutex = _mutex;
            _mutex = null;

            if (mutex == null)
            {
                return;
            }

            try
            {
                if (_owned)
                {
                    mutex.ReleaseMutex();
                }
            }
            catch
            {
                // Мьютекс уже освобождён или поток другой — процесс всё равно завершается.
            }
            finally
            {
                _owned = false;
                mutex.Dispose();
            }
        }

        /// <summary>Проверка «есть ли право создавать объекты в глобальном пространстве имён».</summary>
        private static bool CanCreateGlobalObject()
        {
            string probeName = @"Global\ParsecIntegrationClient.Probe." + Guid.NewGuid().ToString("N");

            try
            {
                using (new Mutex(false, probeName))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryAcquireCore(string name, out bool accessDenied)
        {
            accessDenied = false;
            Mutex mutex = null;

            try
            {
                mutex = new Mutex(false, name);

                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    // Предыдущий владелец завершился, не освободив мьютекс.
                    acquired = true;
                }

                if (!acquired)
                {
                    mutex.Dispose();
                    return false;
                }

                _mutex = mutex;
                _owned = true;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                accessDenied = true;
                mutex?.Dispose();
                return false;
            }
            catch
            {
                mutex?.Dispose();
                throw;
            }
        }

        /// <summary>Виден ли в системе другой процесс с нашим именем (проверка без мьютекса).</summary>
        private static bool IsAnotherInstanceVisible()
        {
            try
            {
                Process current = Process.GetCurrentProcess();

                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    using (process)
                    {
                        if (process.Id != current.Id)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Перечислить процессы не удалось — считаем, что копия может работать.
                return true;
            }

            return false;
        }

        /// <summary>Текст для журнала: по возможности с PID уже работающих копий.</summary>
        private static string BuildBusyMessage()
        {
            return "Уже запущен другой экземпляр ParsecIntegrationClient" + DescribeOtherInstances() +
                   ". Второй запуск запрещён: две копии обрабатывали бы одну очередь PARSEC_QUEUE " +
                   "параллельно и дублировали бы операции в Парсеке.";
        }

        private static string DescribeOtherInstances()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                var ids = new List<int>();

                foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                {
                    using (process)
                    {
                        if (process.Id != current.Id)
                        {
                            ids.Add(process.Id);
                        }
                    }
                }

                return ids.Count == 0 ? string.Empty : " (PID " + string.Join(", ", ids) + ")";
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
