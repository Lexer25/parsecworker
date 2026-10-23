using ParsecIntegrationClient.Models;
using System;
using System.Linq;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// Обработка очереди заданий cardindev. Один проход = один вызов RunOnce().
    /// </summary>
    public sealed class QueueProcessor
    {
        public void RunOnce()
        {
            Logger.Log<QueueProcessor>("Warning", "21 ЗАПУСК ОБРАБОТКИ ЗАДАЧ");
            Logger.Log<QueueProcessor>("Warning", string.Format("22 ГЛОБАЛЬНЫЙ ТАЙМАУТ {0} секунд", SettingsService.DatabaseJobTimeout));

            var rows = DatabaseService.GetList<DbModelRowIDInDev>(SettingsService.QuerySelectIdDevCardString)
                .OrderBy(x => Convert.ToInt32(x.ID))
                .ToArray();

            if (rows.Length == 0)
            {
                StateService.SaveState(StateSnapshot.Idle());
                Logger.Log<QueueProcessor>("Warning", "Задач для обработки не найдено");
                Logger.Log<QueueProcessor>("Warning", string.Format("Ожидание {0} минут перед следующей попыткой", SettingsService.ErrorTimeoutMinutes));
                return;
            }

            Logger.Log<QueueProcessor>("Warning", string.Format("64 Получено {0} записей для выполнения", rows.Length));

            int i = 1;
            foreach (var row in rows)
            {
                Logger.Log<QueueProcessor>("Info", string.Format(
                    "129 ЗАДАЧА {0}, {1}/{2} Начало обработки cardindev {3}",
                    row.OPERATION, i, rows.Length, row.ID));

                var precheck = Precheck(row);
                if (precheck != null)
                {
                    StateService.SaveState(StateSnapshot.From(row, precheck));
                    Logger.Log<QueueProcessor>("Warning",
                        string.Format("ГЛОБАЛЬНАЯ ОСТАНОВКА: {0} (задача {1})", precheck.Description, row.ID));
                    Logger.Log<QueueProcessor>("Warning", string.Format("ГЛОБАЛЬНЫЙ ТАЙМАУТ Следующая попытка через {0} минут", SettingsService.ErrorTimeoutMinutes));
                    return;
                }

                OperationResult result;
                try
                {
                    result = Execute(row);
                }
                catch (Exception ex)
                {
                    Logger.Log<QueueProcessor>("Warning", "Command failed: " + ex.Message);
                    result = OperationResult.Error("Необработанное исключение", ex.Message, 9);
                }

                if (result == null)
                {
                    // Защита от будущих ошибок: фабрики не возвращают null, но Execute может измениться.
                    Logger.Log<QueueProcessor>("Warning", "ГЛОБАЛЬНАЯ ОСТАНОВКА Обработка прекращена: result is null");
                    return;
                }

                StateService.SaveState(StateSnapshot.From(row, result));

                switch (result.Status)
                {
                    case OperationStatus.Error:
                        if (SettingsService.SkipErrCode != null && result.ErrorCode.HasValue
                            && SettingsService.SkipErrCode.Contains(result.ErrorCode.Value))
                        {
                            Logger.Log<QueueProcessor>("Info", string.Format(
                                "ErrorCode ({0}) в SkipErrCode: задача {1} удалена автоматически.", result.ErrorCode, row.ID));
                            DatabaseService.DeleteIdInDevById(row.ID);
                        }
                        else
                        {
                            DatabaseService.IncrementAttemp(row);
                            Logger.Log<QueueProcessor>("Warning", "ГЛОБАЛЬНАЯ ОСТАНОВКА Обработка прекращена: " + result.ErrorMessage);
                            Logger.Log<QueueProcessor>("Warning", string.Format("ГЛОБАЛЬНЫЙ ТАЙМАУТ Следующая попытка через {0} минут", SettingsService.ErrorTimeoutMinutes));
                            return;
                        }
                        break;

                    case OperationStatus.Ok:
                    case OperationStatus.Skip:
                    case OperationStatus.Warn:
                        Logger.Log<QueueProcessor>("Info", string.Format("Задача {0} завершена со статусом {1}, удаление", row.ID, result.Status));
                        DatabaseService.DeleteIdInDevById(row.ID);
                        break;

                    default:
                        // None — не должно случаться для результата операции.
                        Logger.Log<QueueProcessor>("Warning", string.Format("Задача {0}: неожиданный статус {1}", row.ID, result.Status));
                        break;
                }

                i++;
            }

            Logger.Log<QueueProcessor>("Warning", "Все задачи успешно обработаны");
        }

        private static OperationResult Precheck(DbModelRowIDInDev row)
        {
            int op;
            if (!int.TryParse(row.OPERATION, out op))
            {
                var desc = string.Format("MainJob precheck: некорректный тип операции '{0}' для cardindev={1}", row.OPERATION, row.ID);
                var errorMessage = string.Format("Ошибка валидации: некорректный код операции '{0}'", row.OPERATION);
                Logger.Log<QueueProcessor>("Error", desc);
                return OperationResult.Error(desc, errorMessage, 30);
            }

            if ((op == 4 || op == 6) && !Guid.TryParse(row.ID_CARD, out _))
            {
                var desc = string.Format("MainJob precheck: для операции {0} ожидается GUID в ID_CARD, получено '{1}' (cardindev={2})", op, row.ID_CARD, row.ID);
                var errorMessage = string.Format("Ошибка валидации: для операции {0} ожидается GUID в поле ID_CARD, получено '{1}'", op, row.ID_CARD);
                Logger.Log<QueueProcessor>("Error", desc);
                return OperationResult.Error(desc, errorMessage, 31);
            }

            return null;
        }

        private static OperationResult Execute(DbModelRowIDInDev row)
        {
            switch (row.OPERATION)
            {
                case "3": return ParsecService.AddPeople(row);
                case "35": return ParsecService.SavePerson(row);
                case "4": return ParsecService.RemovePeople(row);
                case "5": return ParsecService.AddOrg(row);
                case "55": return ParsecService.SaveOrgUnit(row);
                case "6": return ParsecService.RemoveOrg(row);
                case "7": return ParsecService.AddAccGroupPeople(row);
                case "8": return ParsecService.RemoveIdentifierPeople(row);
                case "9": return ParsecService.AddCardPeople(row);
                case "10": return ParsecService.RemoveCardPeople(row);
                case "1":
                case "2":
                    return OperationResult.Skip("Команда не реализована");
                default:
                    return OperationResult.Skip(string.Format("Неизвестная операция '{0}'", row.OPERATION));
            }
        }
    }
}