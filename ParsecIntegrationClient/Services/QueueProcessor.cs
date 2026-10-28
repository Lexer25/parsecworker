using Newtonsoft.Json;
using ParsecIntegrationClient.Models;
using System;
using System.Diagnostics;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// Обработка очереди PARSEC_QUEUE. Один проход = один вызов RunOnce().
    ///
    /// Очередь обрабатывается строго по id. Если операция завершилась ошибкой,
    /// проход немедленно прекращается: следующие события не трогаются, пока
    /// неудачная строка не будет исправлена/исчерпает попытки. Это требование
    /// интеграции: операции зависят от предыдущих.
    ///
    /// Строки не удаляются: status=2/4/5/6 остаются в PARSEC_QUEUE как история,
    /// детали каждой попытки пишутся в PARSEC_QUEUE_LOG.
    /// </summary>
    public sealed class QueueProcessor
    {
        private const int StatusDone = 2;
        private const int StatusRetry = 3;
        private const int StatusDead = 4;
        private const int StatusSkipped = 5;

        private static readonly string WorkerId = BuildWorkerId();

        public QueueRunResult RunOnce()
        {
            var run = new QueueRunResult();

            Logger.Log<QueueProcessor>("Warning", "21 ЗАПУСК ОБРАБОТКИ ОЧЕРЕДИ PARSEC_QUEUE");

            var rows = DatabaseService.GetQueueBatch(SettingsService.QueueBatchSize);

            if (rows.Count == 0)
            {
                StateService.SaveState(StateSnapshot.Idle());
                Logger.Log<QueueProcessor>("Warning", "Задач для обработки не найдено");
                return run;
            }

            Logger.Log<QueueProcessor>("Warning", string.Format("64 Получено {0} записей для выполнения", rows.Count));

            int i = 1;
            foreach (var row in rows)
            {
                Logger.Log<QueueProcessor>("Info", string.Format(
                    "129 ЗАДАЧА {0}, {1}/{2} Начало обработки PARSEC_QUEUE {3}",
                    row.OPERATION, i, rows.Count, row.ID));

                bool claimed = DatabaseService.TryClaimQueueRow(
                    row.ID, WorkerId, DateTime.Now.AddMinutes(SettingsService.QueueLeaseMinutes));

                if (!claimed)
                {
                    // Строку уже забрал другой экземпляр службы. Не перескакиваем через неё:
                    // порядок важнее пропускной способности.
                    run.Failed = true;
                    run.FailedId = row.ID;
                    run.Error = "строка уже обрабатывается другим экземпляром";
                    Logger.Log<QueueProcessor>("Warning",
                        string.Format("Задача {0} уже обрабатывается, проход остановлен", row.ID));
                    return run;
                }

                DateTime startedAt = DateTime.Now;
                OperationResult result;

                try
                {
                    result = Precheck(row) ?? Execute(row);
                }
                catch (Exception ex)
                {
                    Logger.Log<QueueProcessor>("Warning", "Command failed: " + ex.Message);
                    result = OperationResult.Error("Необработанное исключение", ex.Message, 9);
                }

                int durationMs = (int)Math.Max(0, (DateTime.Now - startedAt).TotalMilliseconds);
                int attemptNo = row.ATTEMPTS + 1;

                if (result == null)
                {
                    result = OperationResult.Error("Операция не вернула результат", "result is null", 9);
                }

                if (result.Status == OperationStatus.Ok || result.Status == OperationStatus.Warn)
                {
                    DatabaseService.CompleteQueueRow(row.ID, row.ATTEMPTS, durationMs);
                    DatabaseService.WriteQueueLog(row.ID, attemptNo, startedAt, StatusDone,
                        result.ErrorCode, result.ErrorMessage, durationMs, WorkerId,
                        BuildDetails(row, result, attemptNo));

                    StateService.SaveState(StateSnapshot.From(row, result));

                    Logger.Log<QueueProcessor>("Info", string.Format(
                        "Задача {0} завершена успешно за {1} мс", row.ID, durationMs));

                    run.Processed++;
                    i++;
                    continue;
                }

                if (result.Status == OperationStatus.Skip || IsSkipError(result))
                {
                    string reason = string.IsNullOrWhiteSpace(result.Description)
                        ? "Пропущено"
                        : result.Description;

                    DatabaseService.SkipQueueRow(row.ID, row.ATTEMPTS, durationMs, reason);
                    DatabaseService.WriteQueueLog(row.ID, attemptNo, startedAt, StatusSkipped,
                        result.ErrorCode, reason, durationMs, WorkerId,
                        BuildDetails(row, result, attemptNo));

                    StateService.SaveState(StateSnapshot.From(row, result));

                    Logger.Log<QueueProcessor>("Warning", string.Format(
                        "Задача {0} пропущена: {1}", row.ID, reason));

                    run.Processed++;
                    i++;
                    continue;
                }

                if (IsMainSessionError(result))
                {
                    // Сессия Парсека истекла/отозвана: без сброса следующие вызовы
                    // будут падать так же, пока событие не уйдёт в dead letter.
                    ClientState.ResetSession();
                    Logger.Log<QueueProcessor>("Warning",
                        "Сессия Парсека сброшена, следующая попытка потребует повторной авторизации");
                }

                // Ошибка: инкремент попытки и остановка всего прохода.
                int attempts = row.ATTEMPTS + 1;
                int status = attempts >= row.MAX_ATTEMPTS ? StatusDead : StatusRetry;

                DatabaseService.FailQueueRow(row.ID, attempts, status, result.ErrorCode,
                    result.ErrorMessage, durationMs);

                DatabaseService.WriteQueueLog(row.ID, attemptNo, startedAt, status,
                    result.ErrorCode, result.ErrorMessage, durationMs, WorkerId,
                    BuildDetails(row, result, attemptNo));

                StateService.SaveState(StateSnapshot.From(row, result));

                run.Failed = true;
                run.FailedId = row.ID;
                run.Error = result.ErrorMessage;

                Logger.Log<QueueProcessor>("Warning", string.Format(
                    "ГЛОБАЛЬНАЯ ОСТАНОВКА: {0} (задача {1}, попытка {2}/{3}, статус {4})",
                    result.ErrorMessage, row.ID, attempts, row.MAX_ATTEMPTS, status));

                if (status == StatusDead)
                {
                    Logger.Log<QueueProcessor>("Warning", string.Format(
                        "Задача {0}: попытки исчерпаны, событие осталось в истории со статусом 4 (dead letter)", row.ID));
                }
                else
                {
                    Logger.Log<QueueProcessor>("Warning", string.Format(
                        "Следующая попытка через {0} минут", SettingsService.ErrorTimeoutMinutes));
                }

                return run;
            }

            Logger.Log<QueueProcessor>("Warning", "Все задачи успешно обработаны");
            return run;
        }

        private static bool IsSkipError(OperationResult result)
        {
            return result.ErrorCode.HasValue
                   && SettingsService.SkipErrCode != null
                   && SettingsService.SkipErrCode.Contains(result.ErrorCode.Value);
        }

        private static bool IsMainSessionError(OperationResult result)
        {
            string message = result == null ? null : result.ErrorMessage;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            // Ошибки сессии редактирования (код 7) — не повод переавторизовывать службу.
            if (message.IndexOf("редактирования", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            string[] fragments =
            {
                "session not found", "no session", "invalid session", "session is not valid",
                "not authorized", "authentication failed", "unauthorized",
                "сессия не найдена", "не авторизован"
            };

            foreach (string fragment in fragments)
            {
                if (message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static OperationResult Precheck(DbModelQueueRow row)
        {
            string idCard = row.ID_CARD == null ? null : row.ID_CARD.Trim();

            switch (row.OPERATION)
            {
                case 3:
                case 35:
                case 7:
                case 8:
                case 9:
                case 11:
                    if (!row.ID_PEP.HasValue)
                    {
                        return PrecheckError(row, "для операции " + row.OPERATION + " не заполнен ID_PEP", 30);
                    }
                    break;
            }

            switch (row.OPERATION)
            {
                case 4:
                case 6:
                    if (!Guid.TryParse(idCard, out _))
                    {
                        // Удалять в Парсеке нечего: объекта с таким идентификатором там быть не может.
                        // Это особенно важно для тестовых данных с GUID вида TEST-ORG-...
                        string reason = "ID_CARD '" + row.ID_CARD + "' не является GUID: объекта в Parsec нет";
                        Logger.Log<QueueProcessor>("Warning", "Precheck: " + reason);
                        return OperationResult.Skip(reason);
                    }
                    break;
            }

            switch (row.OPERATION)
            {
                case 5:
                case 55:
                    if (!Guid.TryParse(idCard, out _))
                    {
                        return PrecheckError(row,
                            "для операции " + row.OPERATION + " ожидается GUID в ID_CARD, получено '" + row.ID_CARD + "'",
                            31);
                    }
                    break;
            }

            switch (row.OPERATION)
            {
                case 7:
                case 8:
                    if (!int.TryParse(idCard, out _))
                    {
                        return PrecheckError(row,
                            "для операции " + row.OPERATION + " ожидается числовой ID_ACCESSNAME в ID_CARD, получено '"
                            + row.ID_CARD + "'", 33);
                    }
                    break;
            }

            switch (row.OPERATION)
            {
                case 9:
                case 10:
                case 11:
                    if (string.IsNullOrWhiteSpace(idCard))
                    {
                        return PrecheckError(row, "для операции " + row.OPERATION + " не заполнен ID_CARD", 32);
                    }

                    if (!long.TryParse(idCard, out _))
                    {
                        return PrecheckError(row,
                            "для операции " + row.OPERATION + " ожидается числовой код карты в ID_CARD, получено '"
                            + row.ID_CARD + "'", 34);
                    }
                    break;
            }

            return null;
        }

        private static OperationResult PrecheckError(DbModelQueueRow row, string message, int code)
        {
            string desc = string.Format("Precheck: {0} (PARSEC_QUEUE={1})", message, row.ID);
            Logger.Log<QueueProcessor>("Error", desc);
            return OperationResult.Error(desc, "Ошибка валидации: " + message, code);
        }

        private static OperationResult Execute(DbModelQueueRow row)
        {
            DbModelRowIDInDev legacy = row.ToLegacyRow();

            switch (row.OPERATION)
            {
                case 3: return ParsecService.AddPeople(legacy);
                case 35: return ParsecService.SavePerson(legacy);
                case 4: return ParsecService.RemovePeople(legacy);
                case 5: return ParsecService.AddOrg(legacy);
                case 55: return ParsecService.SaveOrgUnit(legacy);
                case 6: return ParsecService.RemoveOrg(legacy);
                case 7: return ParsecService.AddAccGroupPeople(legacy);
                case 8: return ParsecService.RemoveIdentifierPeople(legacy);
                case 9: return ParsecService.AddCardPeople(legacy);
                case 10: return ParsecService.RemoveCardPeople(legacy);
                case 11: return ParsecService.SyncCardIdentifier(legacy);
                case 1:
                case 2:
                    return OperationResult.Skip("Команда не реализована");
                default:
                    return OperationResult.Skip(string.Format("Неизвестная операция '{0}'", row.OPERATION));
            }
        }

        private static string BuildDetails(DbModelQueueRow row, OperationResult result, int attemptNo)
        {
            try
            {
                var details = new
                {
                    operation = row.OPERATION,
                    id_pep = row.ID_PEP,
                    id_card = row.ID_CARD,
                    attempt = attemptNo,
                    status = result.Status.ToString(),
                    description = result.Description,
                    error = result.ErrorMessage,
                    error_code = result.ErrorCode,
                    worker = WorkerId
                };

                return JsonConvert.SerializeObject(details);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildWorkerId()
        {
            try
            {
                return Environment.MachineName + ":" + Process.GetCurrentProcess().Id + ":"
                       + DateTime.Now.ToString("yyyyMMddHHmmss");
            }
            catch
            {
                return "worker:" + DateTime.Now.ToString("yyyyMMddHHmmss");
            }
        }
    }
}
