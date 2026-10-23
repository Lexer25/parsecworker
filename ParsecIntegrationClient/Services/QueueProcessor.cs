using ParsecIntegrationClient.Models;
using System;
using System.Linq;

namespace ParsecIntegrationClient.Services
{
    /// <summary>
    /// Обработка очереди заданий cardindev (бывший MainJob из Quartz).
    /// Логика и порядок действий сохранены: один проход = один вызов RunOnce(),
    /// расписание задаёт Worker через Service:JobIntervalSeconds.
    /// </summary>
    public sealed class QueueProcessor
    {
        public void RunOnce()
        {
            Logger.Log<QueueProcessor>("Warning", "21 ЗАПУСК ОБРАБОТКИ ЗАДАЧ");
            Logger.Log<QueueProcessor>("Warning", string.Format("22 ГЛОБАЛЬНЫЙ ТАЙМАУТ {0} секунд", SettingsService.DatabaseJobTimeout));

            var state = new State();

            var rows = DatabaseService.GetList<DbModelRowIDInDev>(SettingsService.QuerySelectIdDevCardString).OrderBy(x => Convert.ToInt32(x.ID)).ToArray();

            if (rows == null || rows.Length == 0)
            {
                state.ErrorMessage = null;
                state.desc = "Задачи для обработки отсутствуют";
                state.Status = null;
                state.Attempts = null;
                state.IdCardindev = null;
                state.Timestamp = DateTime.Now;
                state.Operation = null;
                state.OperationCode = null;
                state.NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes);
                state.keyNum = Key.keyNumber;
                StateService.SaveState(state);
                Logger.Log<QueueProcessor>("Warning", "Задач для обработки не найдено");
                Logger.Log<QueueProcessor>("Warning", string.Format("Ожидание {0} минут перед следующей попыткой", SettingsService.ErrorTimeoutMinutes));
                return;
            }

            Logger.Log<QueueProcessor>("Warning", string.Format("64 Получено {0} записей для выполнения", rows.Length));
            Logger.Log<QueueProcessor>("Warning", string.Format("90 Продолжаю работу с {0} записями.", rows.Length));

            int i = 1;
            foreach (var row in rows)
            {
                Logger.Log<QueueProcessor>("Info", string.Format(
                    "129 ЗАДАЧА {0}, {1}/{2} Начало обработки cardindev {3}",
                    row.OPERATION, i, rows.Length, row.ID));

                // Предварительные проверки до выполнения операции.
                int operationPrecheck;
                if (!int.TryParse(row.OPERATION, out operationPrecheck))
                {
                    var desc = string.Format("MainJob precheck: некорректный тип операции '{0}' для cardindev={1}", row.OPERATION, row.ID);
                    var errorMessage = string.Format("Ошибка валидации: некорректный код операции '{0}', ожидается число от 1 до 10", row.OPERATION);
                    Logger.Log<QueueProcessor>("Error", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    StateService.SaveState(state);
                    Logger.Log<QueueProcessor>("Warning", string.Format("ГЛОБАЛЬНАЯ ОСТАНОВКА Некорректная операция {0} для задачи {1}", row.OPERATION, row.ID));
                    return;
                }

                // Для некоторых операций ID_CARD обязан быть GUID (иначе операция заведомо некорректна).
                if ((operationPrecheck == 4 || operationPrecheck == 6) && !Guid.TryParse(row.ID_CARD, out _))
                {
                    var desc = string.Format("MainJob precheck: для операции {0} ожидается GUID в ID_CARD, получено '{1}' (cardindev={2})", operationPrecheck, row.ID_CARD, row.ID);
                    var errorMessage = string.Format("Ошибка валидации: для операции {0} ожидается GUID в поле ID_CARD, получено '{1}'", operationPrecheck, row.ID_CARD);
                    Logger.Log<QueueProcessor>("Error", desc);
                    state.desc = desc;
                    state.IdCardindev = row.ID;
                    state.Operation = StateService.GetOperationName(row.OPERATION);
                    state.OperationCode = row.OPERATION;
                    state.Status = "ERR";
                    state.ErrorMessage = errorMessage;
                    state.Attempts = row.ATTEMPS;
                    state.Timestamp = DateTime.Now;
                    state.keyNum = Key.keyNumber;
                    StateService.SaveState(state);
                    Logger.Log<QueueProcessor>("Warning", string.Format("ГЛОБАЛЬНАЯ ОСТАНОВКА Некорректный GUID в ID_CARD для задачи {0}", row.ID));
                    return;
                }

                int operation = 0;
                MOperation operationName = MOperation.Добавление_карточки;
                string komuName = null;
                string accessName = null;

                try
                {
                    operation = Convert.ToInt32(row.OPERATION);
                    operationName = (MOperation)Enum.GetValues(typeof(MOperation)).GetValue(operation - 1);

                    // Для операций, где нужен человек (3/4/7/8), выводим читаемые названия.
                    if (operation == 3 || operation == 4 || operation == 7 || operation == 8)
                    {
                        komuName = DatabaseService.GetString(
                            string.Format("select coalesce(p.surname,'') || ' ' || coalesce(p.name,'') || ' ' || coalesce(p.patronymic,'') from people p where p.id_pep = {0}", row.ID_PEP));

                        // Для операций добавления/удаления категории доступа (7/8) выводим и категорию.
                        if (operation == 7 || operation == 8)
                        {
                            accessName = DatabaseService.GetString(
                                string.Format("select an.name from accessname an where an.id_accessname = {0}", row.ID_CARD));
                        }
                    }
                }
                catch (Exception)
                {
                    // Игнорируем ошибки при получении доп. данных
                }

                // Выполняем команду.
                State result = new State();

                try
                {
                    switch (row.OPERATION)
                    {
                        case "1": // Добавление карточки
                            {
                                //result = ParsecService.AddCardForPeople(row);
                                break;
                            }
                        case "2": // Удалить карточку
                            {
                                //result = ParsecService.RemoveCardForPeople(row);
                                break;
                            }
                        case "3": //Добавление человека
                            {
                                result = ParsecService.AddPeople(row);
                                break;
                            }
                        case "35": //Изменение человека
                            {
                                result = ParsecService.SavePerson(row);
                                break;
                            }
                        case "4": //Удаление человека
                            {
                                result = ParsecService.RemovePeople(row);
                                break;
                            }
                        case "5": //Добавление организации
                            {
                                result = ParsecService.AddOrg(row);
                                break;
                            }
                        case "55": //изменение организации
                            {
                                result = ParsecService.SaveOrgUnit(row);
                                break;
                            }
                        case "6": //Удаление организации
                            {
                                result = ParsecService.RemoveOrg(row);
                                break;
                            }
                        case "7": //Добавление категории доступа
                            {
                                result = ParsecService.AddAccGroupPeople(row);
                                break;
                            }
                        case "8": //Удаление категории доступа
                            {
                                result = ParsecService.RemoveIdentifierPeople(row);
                                break;
                            }
                        case "9"://Добавление карты человеку
                            {
                                //result = ParsecService.AddCardForPeople(row);
                                result = ParsecService.AddCardPeople(row);
                                break;
                            }
                        case "10"://Удаление карты у человека
                            {
                                result = ParsecService.RemoveCardPeople(row);
                                break;
                            }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log<QueueProcessor>("Warning", "Command failed: " + ex.Message);
                }

                if (result == null)
                {
                    Logger.Log<QueueProcessor>("Warning", "ГЛОБАЛЬНАЯ ОСТАНОВКА Обработка прекращена: result is null");
                    Logger.Log<QueueProcessor>("Warning", string.Format("ГЛОБАЛЬНЫЙ ТАЙМАУТ Следующая попытка через {0} минут", SettingsService.ErrorTimeoutMinutes));
                    return;
                }

                Logger.Log<QueueProcessor>("Debug", result.ToString());
                StateService.SaveState(result);

                if (result.Status == "ERR")
                {
                    if (SettingsService.SkipErrCode != null && SettingsService.SkipErrCode.Contains(result.ErrorCode))
                    {
                        Logger.Log<QueueProcessor>("Info", string.Format(
                            "ErrorCode ({0}) находится в списке SkipErrCode в appsettings.json. Задача {1} была удалена автоматически.",
                            result.ErrorCode, row.ID));
                        DatabaseService.DeleteIdInDevById(row.ID);
                    }
                    else
                    {
                        DatabaseService.IncrementAttemp(row);
                        Logger.Log<QueueProcessor>("Warning", "ГЛОБАЛЬНАЯ ОСТАНОВКА Обработка прекращена: " + result.ErrorMessage);
                        Logger.Log<QueueProcessor>("Warning", string.Format("ГЛОБАЛЬНЫЙ ТАЙМАУТ Следующая попытка через {0} минут", SettingsService.ErrorTimeoutMinutes));
                        return;
                    }
                }
                else if (result.Status == "OK")
                {
                    Logger.Log<QueueProcessor>("Info", string.Format("Операция выполнена успешно УДАЛЕНИЕ ЗАДАЧИ {0}", row.ID));
                    DatabaseService.DeleteIdInDevById(row.ID);
                }
                else if (result.Status == "SKIP")
                {
                    Logger.Log<QueueProcessor>("Info", "Команда не реализована или пропущена");
                    DatabaseService.DeleteIdInDevById(row.ID);
                }

                i++;
            }

            Logger.Log<QueueProcessor>("Warning", "Все задачи успешно обработаны");

            // Проверяем, остались ли еще задачи
            var remainingRows = DatabaseService.GetList<DbModelRowIDInDev>(SettingsService.QuerySelectIdDevCardString).ToArray();
            if (remainingRows == null || remainingRows.Length == 0)
            {
                Logger.Log<QueueProcessor>("Warning", "Нет новых задач для обработки");
                Logger.Log<QueueProcessor>("Warning", string.Format("Ожидание {0} минут перед следующей попыткой", SettingsService.ErrorTimeoutMinutes));
            }
        }
    }
}