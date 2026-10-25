using Newtonsoft.Json;
using ParsecIntegrationClient.IntegrationWebService;
using ParsecIntegrationClient.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ParsecIntegrationClient.Services
{
    public class ParsecService
    {
        // ==================== Публичные операции ====================

        public static OperationResult AddAccGroupPeople(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Info", $"102 Start AddIdentifierPeople {row.ID}");

            try
            {
                var query = BuildAddAccessCatQuery(row);
                var model = DatabaseService.Get<DbModelAccessCategoryForPeople>(query);

                if (model == null)
                {
                    Logger.Log<ParsecService>("Warning", "114 No models found");
                    return OperationResult.Error("Модели данных не найдены", "Модели данных не найдены", 1);
                }

                Logger.Log<ParsecService>("Info", $"118 Found GUID_ACCGROUP {model.GUID_ACCGROUP} models to process");

                var lastState = ProcessAccessCatAddition(row, model);

                if (lastState.Status == OperationStatus.Ok)
                {
                    Logger.Log<ParsecService>("Info", "159 Model processed successfully");
                    return lastState;
                }

                return lastState;
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"199 Critical error in AddIdentifierPeople: {ex.Message}");
                return OperationResult.Error($"201 Критическая ошибка: {ex.Message}", ex.Message, 9);
            }
        }

        public static OperationResult RemoveIdentifierPeople(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Info", $"828 Start RemoveIdentifierPeople {row.ID}");

            try
            {
                var query = BuildRemoveAccessCatQuery(row);
                var model = DatabaseService.Get<DbModelAccessCategoryForPeople>(query);

                if (model == null)
                {
                    Logger.Log<ParsecService>("Warning", "840 Модель данных не найдена");
                    return OperationResult.Error("841 Модель данных не найдена", "Модель данных не найдена", 1);
                }

                Logger.Log<ParsecService>("Info", $"844 Найден GUID_ACCGROUP {model.GUID_ACCGROUP} для удаления");

                return ProcessAccessCatRemoval(row, model);
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"853 Critical error in RemoveIdentifierPeople: {ex.Message}");
                return OperationResult.Error($"855 Критическая ошибка: {ex.Message}", ex.Message, 9);
            }
        }

        public static OperationResult AddPeople(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Error", $"537 start AddPeople {row.ID}");

            try
            {
                var query = "select p.id_pep, p.guid as guid_pep, " +
                            "o.guid as guid_org, p.name, p.surname, p.patronymic, p.tabnum, o.name as org_name " +
                            "from people p " +
                            "left join organization o on p.id_org=o.id_org " +
                            $"where p.id_pep={row.ID_PEP};";
                Logger.Log<ParsecService>("Error", $"528 {query}");

                var people = DatabaseService.Get<DbModelAddPeople>(query);

                if (people == null || string.IsNullOrWhiteSpace(people.GUID_PEP))
                {
                    var descNotFound = $"475 Пользователь с {row.ID_PEP} не найден в базе СКУД Артонит.";
                    Logger.Log<ParsecService>("Warning", descNotFound);
                    return OperationResult.Error(descNotFound,
                        $"Ошибка БД: пользователь с ID_PEP={row.ID_PEP} не найден в базе СКУД", 10);
                }

                var person = new Person()
                {
                    ID = new Guid(people.GUID_PEP),
                    FIRST_NAME = people.NAME,
                    LAST_NAME = people.SURNAME,
                    MIDDLE_NAME = people.PATRONYMIC,
                    TAB_NUM = people.TABNUM,
                    ORG_ID = new Guid(people.GUID_ORG),
                };

                Logger.Log<ParsecService>("Info", $"554 Добавляется сотрудник {JsonConvert.SerializeObject(person)}");

                if (CheckGuidePresent(person.ID))
                {
                    const int code = 18;
                    var desc = $"577 КОД ОШИБКИ: {code}. Уже имеется сотрудник с GUID {person.ID}. Работаю завершаю.";
                    var errorMessage = $"1134 Сотрудник с GUID {person.ID} уже существует в Parsec";
                    Logger.Log<ParsecService>("Warning", desc);
                    // Повтор после сбоя связи: человек уже создан. Это не ошибка интеграции,
                    // иначе событие навсегда застрянет в очереди.
                    return OperationResult.Warn(desc, errorMessage);
                }

                if (!CheckGuidePresent(person.ORG_ID))
                {
                    const int code = 19;
                    var desc = $"560 НЕ существует организация {people.ORG_NAME} с указанным GUID {person.ORG_ID} в Парсек. Работаю завершаю.";
                    var errorMessage = $"1158 Ошибка: организация не найдена в Parsec (GUID: {person.ORG_ID})";
                    Logger.Log<ParsecService>("Warning", desc);
                    return OperationResult.Error(desc, errorMessage, code);
                }

                using var integServ = ParsecClient.Create();
                var res = integServ.CreatePerson(ClientState.SessionID, person);

                if (res.Result != ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Error", $"605 не смог вставить сотрудника {JsonConvert.SerializeObject(person)}");
                    Logger.Log<ParsecService>("Error", $"606 {res.ErrorMessage}");
                    return OperationResult.Error($"Ошибка при добавлении сотрудника: {res.ErrorMessage}", res.ErrorMessage, 1);
                }

                Logger.Log<ParsecService>("Info", $"466 Пользователь добавлен успешно {JsonConvert.SerializeObject(person)}");
                return OperationResult.Ok("Пользователь добавлен успешно");
            }
            catch (Exception ex)
            {
                var cleanErrorMessage = string.Join(" ", ex.Message.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                Logger.Log<ParsecService>("Warning", $"1231 Пользователя ID_pep={row.ID_PEP} нет в базе данных СКУД: {cleanErrorMessage}");
                return OperationResult.Error($"Пользователя ID_pep={row.ID_PEP} нет в базе данных СКУД", cleanErrorMessage, 10);
            }
        }

        public static OperationResult SavePerson(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Error", $"1629 start SavePerson {row.ID}");

            try
            {
                var query = "select p.id_pep, p.guid as guid_pep, " +
                            "o.guid as guid_org, p.name, p.surname, p.patronymic, p.tabnum, o.name as org_name " +
                            "from people p " +
                            "left join organization o on p.id_org=o.id_org " +
                            $"where p.id_pep={row.ID_PEP};";
                Logger.Log<ParsecService>("Error", $"1639 {query}");

                var people = DatabaseService.Get<DbModelAddPeople>(query);

                if (people == null || string.IsNullOrWhiteSpace(people.GUID_PEP))
                {
                    var descNotFound = $"1736 Пользователь с {row.ID_PEP} не найден в базе СКУД Артонит.";
                    Logger.Log<ParsecService>("Warning", descNotFound);
                    return OperationResult.Error(descNotFound,
                        $"Ошибка БД: пользователь с ID_PEP={row.ID_PEP} не найден в базе СКУД", 10);
                }

                var person = new Person()
                {
                    ID = new Guid(people.GUID_PEP),
                    FIRST_NAME = people.NAME,
                    LAST_NAME = people.SURNAME,
                    MIDDLE_NAME = people.PATRONYMIC,
                    TAB_NUM = people.TABNUM,
                    ORG_ID = new Guid(people.GUID_ORG),
                };

                Logger.Log<ParsecService>("Info", $"1657 обновляется сотрудник {JsonConvert.SerializeObject(person)}");

                if (!CheckGuidePresent(person.ID))
                {
                    const int code = 18;
                    var desc = $"1612 КОД ОШИБКИ: {code}. Нет сотрудника с GUID {person.ID} для обновления. Работаю завершаю.";
                    var errorMessage = $"1134 Ошибка: сотрудник для обновления с GUID {person.ID} не существует в Parsec";
                    Logger.Log<ParsecService>("Warning", desc);
                    return OperationResult.Error(desc, errorMessage, code);
                }

                if (!CheckGuidePresent(person.ORG_ID))
                {
                    const int code = 19;
                    var desc = $"560 НЕ существует организация {people.ORG_NAME} с указанным GUID {person.ORG_ID} в Парсек. Работаю завершаю.";
                    var errorMessage = $"1158 Ошибка: организация не найдена в Parsec (GUID: {person.ORG_ID})";
                    Logger.Log<ParsecService>("Warning", desc);
                    return OperationResult.Error(desc, errorMessage, code);
                }

                using var integServ = ParsecClient.Create();
                var sessionResult = integServ.OpenPersonEditingSession(ClientState.SessionID, new Guid(people.GUID_PEP));

                if (sessionResult.Result != ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Error", $"1700 Ошибка открытия сессии для обновления сотрудника: {sessionResult.ErrorMessage}");
                    return OperationResult.Error(
                        $"Ошибка открытия сессии редактирования: {sessionResult.ErrorMessage}",
                        sessionResult.ErrorMessage, 7);
                }

                try
                {
                    var res = integServ.SavePerson(sessionResult.Value, person);

                    if (res.Result != ClientState.Result_Success)
                    {
                        Logger.Log<ParsecService>("Error", $"1706 не смог обновить сотрудника {JsonConvert.SerializeObject(person)}");
                        Logger.Log<ParsecService>("Error", $"1707 {res.ErrorMessage}");
                        return OperationResult.Error($"1708 Ошибка при обновлении сотрудника: {res.ErrorMessage}", res.ErrorMessage, 1);
                    }

                    Logger.Log<ParsecService>("Info", $"1723 Пользователь обновлен успешно {JsonConvert.SerializeObject(person)}");
                    return OperationResult.Ok("Пользователь обновлен успешно");
                }
                finally
                {
                    CloseEditingSession(integServ, sessionResult.Value);
                }
            }
            catch (Exception ex)
            {
                var cleanErrorMessage = string.Join(" ", ex.Message.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                Logger.Log<ParsecService>("Warning", $"1754 Пользователя ID_pep={row.ID_PEP} нет в базе данных СКУД: {cleanErrorMessage}");
                return OperationResult.Error($"Пользователя ID_pep={row.ID_PEP} нет в базе данных СКУД", cleanErrorMessage, 10);
            }
        }

        public static OperationResult RemovePeople(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Error", $"608 start RemovePeople {JsonConvert.SerializeObject(row.ID)}");

            try
            {
                // Не проверяем наличие отдельным вызовом: при недоступном Парсеке
                // CheckGuidePresent вернул бы false, и удаление было бы ложно
                // помечено успешным. Отсутствие человека распознаём по ответу DeletePerson.
                using var integServ = ParsecClient.Create();
                var res = integServ.DeletePerson(ClientState.SessionID, new Guid(row.ID_CARD));
                Logger.Log<ParsecService>("Error", $"1358 РЕЗУЛЬТАТ УДАЛЕНИЯ {JsonConvert.SerializeObject(res)}");

                if (res.Result == ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Info", $"508 пользователь успешно удален | {JsonConvert.SerializeObject(row)}");
                    return OperationResult.Ok("Пользователь успешно удален");
                }

                if (IsNotFound(res.ErrorMessage))
                {
                    var descForThis = "1311 удаление человека произошло успешно. Человека не было в Парсек";
                    Logger.Log<ParsecService>("Info", descForThis);
                    return OperationResult.Ok(descForThis);
                }

                Logger.Log<ParsecService>("Error", $"498 Ошибка при удалении пользователя. Ошибка: {res.ErrorMessage}");
                return OperationResult.Error($"1300 Ошибка при удалении пользователя: {res.ErrorMessage}", res.ErrorMessage, 20);
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"552 Ошибка в RemovePeople: {ex.Message}");
                return OperationResult.Error($"552 Ошибка в RemovePeople: {ex.Message}", ex.Message, 20);
            }
        }

        public static OperationResult AddOrg(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Error", $"1353 start AddOrg {JsonConvert.SerializeObject(row.ID)}");

            try
            {
                var query = "select o.guid as guide_for_add, " +
                            "o2.guid as guid_for_parent, o.name, o.divcode, o.id_org from organization o " +
                            "join organization o2 on o2.id_org=o.id_parent " +
                            $"where o.guid='{row.ID_CARD}'";

                var model = DatabaseService.Get<DbModelAddOrg>(query);

                Logger.Log<ParsecService>("Warning", $"533 Добавление организации | NAME: {model.NAME} DIVCODE: {model.DIVCODE}");

                if (model.NAME == null)
                {
                    const int code = 21;
                    var desc = $"541 Организация {row.ID_CARD} не найдена";
                    var errorMessage = $"1376 КОД ОШИБКИ: {code}. Ошибка БД: организация с GUID={row.ID_CARD} не найдена в базе СКУД";
                    Logger.Log<ParsecService>("Error", desc);
                    return OperationResult.Error(desc, errorMessage, code);
                }

                using var integServ = ParsecClient.Create();

                var org = new OrgUnit()
                {
                    NAME = model.NAME,
                    ID = new Guid(model.GUID),
                    PARENT_ID = new Guid(model.GUID_PARENT),
                    DESC = ""
                };

                if (CheckGuidePresent(org.ID))
                {
                    var desc = $"564 Организация {org.NAME} (GUID {org.ID}) уже существует в Parsec";
                    Logger.Log<ParsecService>("Warning", desc);
                    // Идемпотентность повторной доставки: организация уже создана.
                    return OperationResult.Warn(desc, desc);
                }

                var result = integServ.CreateOrgUnit(ClientState.SessionID, org);
                if (result.Result != ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Error", $"1572 {result.ErrorMessage}");
                    return OperationResult.Error($"1408 Ошибка при добавлении организации: {result.ErrorMessage}", result.ErrorMessage, 22);
                }

                Logger.Log<ParsecService>("Warning", $"564 Организация добавлена успешно: {org.NAME} " +
                    $"| ID: {org.ID} Parent ID: {org.PARENT_ID} divcode: {model.DIVCODE} IdOrg: {model.ID_ORG}");

                Thread.Sleep(1000); // задержка 1 секунда — как было
                return OperationResult.Ok("1426 Организация добавлена успешно");
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"610 Ошибка в AddOrg: {ex.Message}");
                return OperationResult.Error($"610 Ошибка в AddOrg: {ex.Message}", ex.Message, 22);
            }
        }

        public static OperationResult SaveOrgUnit(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Error", $"1987 start SaveOrgUnit {JsonConvert.SerializeObject(row.ID)}");

            try
            {
                var query = "select o.guid as guide_for_add, " +
                            "o2.guid as guid_for_parent, o.name, o.divcode, o.id_org from organization o " +
                            "join organization o2 on o2.id_org=o.id_parent " +
                            $"where o.guid='{row.ID_CARD}'";

                var model = DatabaseService.Get<DbModelAddOrg>(query);

                Logger.Log<ParsecService>("Warning", $"1997 Обновление организации | NAME: {model.NAME} DIVCODE: {model.DIVCODE}");

                if (model.NAME == null)
                {
                    const int code = 21;
                    var desc = $"2003 Организация {row.ID_CARD} для обновления не найдена";
                    var errorMessage = $"2004 КОД ОШИБКИ: {code}. Ошибка БД: организация с GUID={row.ID_CARD} не найдена в базе СКУД";
                    Logger.Log<ParsecService>("Error", desc);
                    return OperationResult.Error(desc, errorMessage, code);
                }

                using var integServ = ParsecClient.Create();

                var org = new OrgUnit()
                {
                    NAME = model.NAME,
                    ID = new Guid(model.GUID),
                    PARENT_ID = new Guid(model.GUID_PARENT),
                    DESC = ""
                };

                var sessionResult = integServ.OpenOrgUnitEditingSession(ClientState.SessionID, new Guid(model.GUID));

                if (sessionResult.Result != ClientState.Result_Success)
                {
                    Logger.Log<ParsecService>("Error", $"2040 Ошибка открытия сессии редактирования организации: {sessionResult.ErrorMessage}");
                    return OperationResult.Error(
                        $"Ошибка открытия сессии редактирования организации: {sessionResult.ErrorMessage}",
                        sessionResult.ErrorMessage, 7);
                }

                try
                {
                    var result = integServ.SaveOrgUnit(sessionResult.Value, org);
                    if (result.Result != ClientState.Result_Success)
                    {
                        Logger.Log<ParsecService>("Error", $"2039 {result.ErrorMessage}");
                        return OperationResult.Error($"2035 Ошибка при обновлении организации: {result.ErrorMessage}", result.ErrorMessage, 22);
                    }

                    Logger.Log<ParsecService>("Warning", $"2049 Организация обновлена успешно: {org.NAME} " +
                        $"| ID: {org.ID} Parent ID: {org.PARENT_ID} divcode: {model.DIVCODE} IdOrg: {model.ID_ORG}");

                    return OperationResult.Ok("2052 Организация обновлена успешно");
                }
                finally
                {
                    CloseOrgUnitEditingSession(integServ, sessionResult.Value);
                }
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"2066 Ошибка в SaveOrgUnit: {ex.Message}");
                return OperationResult.Error($"2066 Ошибка в SaveOrgUnit: {ex.Message}", ex.Message, 22);
            }
        }

        public static OperationResult RemoveOrg(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Error", $"1458 start RemoveOrg {JsonConvert.SerializeObject(row.ID)}");

            try
            {
                var orgId = new Guid(row.ID_CARD);

                using var integServ = ParsecClient.Create();

                Logger.Log<ParsecService>("Warning", $"584 Удаление организации | {row.ID_CARD}");

                var res = integServ.DeleteOrgUnit(ClientState.SessionID, orgId);
                if (res.Result != ClientState.Result_Success)
                {
                    if (IsNotFound(res.ErrorMessage))
                    {
                        Logger.Log<ParsecService>("Warning",
                            $"584 Организация {orgId} отсутствует в Parsec, удаление не требуется");
                        return OperationResult.Ok("Организация отсутствует в Parsec, удаление не требуется");
                    }

                    Logger.Log<ParsecService>("Error", $"1642 {res.ErrorMessage}");
                    return OperationResult.Error($"1473 Ошибка при удалении организации: {res.ErrorMessage}", res.ErrorMessage, 22);
                }

                Logger.Log<ParsecService>("Warning", $"595 Организация успешно удалена | {row.ID_CARD}");
                return OperationResult.Ok("1490 Организация успешно удалена");
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"638 Ошибка в RemoveOrg: {ex.Message}");
                return OperationResult.Error($"638 Ошибка в RemoveOrg: {ex.Message}", ex.Message, 22);
            }
        }

        public static OperationResult AddCardPeople(DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Error", $"1434 start AddCardPeople {JsonConvert.SerializeObject(row)}");

            try
            {
                var query = "select p.guid, p.tabnum, p.name, p.patronymic, " +
                            $"p.surname from people p where p.id_pep = {row.ID_PEP}";

                var list = DatabaseService.GetList<DbModelAddCard>(query);

                foreach (var model in list)
                {
                    if (model.GUID_PEP == null || model.GUID_PEP == string.Empty)
                    {
                        const int code = 23;
                        var errorDesc = $"621 КОД ОШИБКИ: {code}. GUID_PEP null or empty для контакта ID={row.ID_PEP}";
                        Logger.Log<ParsecService>("Warning", errorDesc);
                        return OperationResult.Error(errorDesc,
                            $"Ошибка БД: поле GUID_PEP не заполнено для контакта ID={row.ID_PEP}", code);
                    }

                    string hexValue = Convert.ToInt64(row.ID_CARD).ToString("X8");

                    Logger.Log<ParsecService>("Warning", $"627 Добавление карты пользователю |" +
                        $"code: {row.ID_CARD} (hex: {hexValue}) | GUID_PEP = {model.GUID_PEP} " +
                        $"| tab_num = {model.TAB_NUM_PEP} " +
                        $"| ФИО (artsec): {model.SURNAME} {model.NAME} {model.PATRONYMIC}");

                    using var integServ = ParsecClient.Create();
                    var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

                    if (person == null)
                    {
                        const int code = 25;
                        var errorDesc = $"674 КОД ОШИБКИ: {code}. Пользователь с GUID: {model.GUID_PEP} не найден в parsec.";
                        Logger.Log<ParsecService>("Warning", errorDesc);
                        return OperationResult.Error(errorDesc,
                            $"Ошибка Parsec: пользователь с GUID {model.GUID_PEP} не найден", code);
                    }

                    var res = integServ.OpenPersonEditingSession(ClientState.SessionID, person.ID);

                    if (res.Result != ClientState.Result_Success)
                    {
                        Logger.Log<ParsecService>("Error", $"642 Ошибка открытия сессии для редактирования пользователя. {res.ErrorMessage}");
                        return OperationResult.Error($"Ошибка открытия сессии: {res.ErrorMessage}", res.ErrorMessage, 7);
                    }

                    try
                    {
                        var creatingItem = BuildCardIdentifier(row, hexValue, model.GUID_PEP);

                        var resAddPersonIdentifier = integServ.AddPersonIdentifier(res.Value, creatingItem);
                        if (resAddPersonIdentifier.Result != ClientState.Result_Success)
                        {
                            if (IsAlreadyExists(resAddPersonIdentifier.ErrorMessage))
                            {
                                Logger.Log<ParsecService>("Warning",
                                    $"659 Карта {row.ID_CARD} уже есть в Parsec, повторное добавление не требуется");
                                return OperationResult.Ok("Карта уже добавлена в Parsec");
                            }

                            Logger.Log<ParsecService>("Error", $"659 Ошибка при добавлении карты пользователю. Ошибка: {resAddPersonIdentifier.ErrorMessage}");
                            return OperationResult.Error(
                                $"Ошибка при добавлении карты: {resAddPersonIdentifier.ErrorMessage}",
                                resAddPersonIdentifier.ErrorMessage, 24);
                        }

                        Logger.Log<ParsecService>("Warning", $"665 Карта успешно добавлена | " +
                            $"code: {row.ID_CARD} (hex: {hexValue}) " +
                            $"Пользователю ФИО (parsec): {person.FIRST_NAME} {person.MIDDLE_NAME} {person.LAST_NAME}");

                        return OperationResult.Ok("Карта успешно добавлена");
                    }
                    finally
                    {
                        CloseEditingSession(integServ, res.Value);
                    }
                }

                const int noDataCode = 25;
                var errorNoData = $"1847 КОД ОШИБКИ: {noDataCode}. Не найден пользователь с ID_PEP = {row.ID_PEP}";
                Logger.Log<ParsecService>("Warning", errorNoData);
                return OperationResult.Error(errorNoData, $"В БД не найден контакт с ID={row.ID_PEP}", noDataCode);
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"724 Ошибка в AddCardPeople: {ex.Message}");
                return OperationResult.Error($"724 Ошибка в AddCardPeople: {ex.Message}", ex.Message, 9);
            }
        }

        public static OperationResult RemoveCardPeople(DbModelRowIDInDev row)
        {
            try
            {
                using var integServ = ParsecClient.Create();

                string hexValue = Convert.ToInt64(row.ID_CARD).ToString("X8");

                Logger.Log<ParsecService>("Warning", $"700 Удаление карты | {row.ID_CARD} (hex: {hexValue})");

                var res = integServ.DeleteIdentifier(ClientState.SessionID, hexValue);
                if (res.Result != ClientState.Result_Success)
                {
                    if (IsNotFound(res.ErrorMessage))
                    {
                        Logger.Log<ParsecService>("Warning",
                            $"726 Карта {row.ID_CARD} отсутствует в Parsec, удаление не требуется");
                        return OperationResult.Ok("Карта отсутствует в Parsec, удаление не требуется");
                    }

                    Logger.Log<ParsecService>("Error", $"726 {res.ErrorMessage}");
                    return OperationResult.Error($"Ошибка при удалении карты: {res.ErrorMessage}", res.ErrorMessage, 26);
                }

                Logger.Log<ParsecService>("Warning", $"711 Карта успешно удалена | {row.ID_CARD} (hex: {hexValue})");
                return OperationResult.Ok("Карта успешно удалена");
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"754 Ошибка в RemoveCardPeople: {ex.Message}");
                return OperationResult.Error($"754 Ошибка в RemoveCardPeople: {ex.Message}", ex.Message, 26);
            }
        }

        // ==================== Вспомогательное (CheckAccessGroups, сессии, Process*, Build*) ====================

        public static Guid CheckAccessGroups(List<Guid> InheritedAccessGroups)
        {
            using var integServ = ParsecClient.Create();
            var accessGroups = integServ.GetAccessGroups(ClientState.SessionID);

            foreach (var accessGroup in accessGroups)
            {
                var list = integServ.GetInheritedAccessGroups(ClientState.SessionID, accessGroup.ID);

                bool isEqual = list.OrderBy(a => a).SequenceEqual(InheritedAccessGroups.OrderBy(a => a));

                if (isEqual)
                {
                    return accessGroup.ID;
                }
            }

            return Guid.Empty;
        }

        public static bool CheckGuidePresent(Guid guid)
        {
            try
            {
                using var integServ = ParsecClient.Create();
                var result = integServ.GetObjectName(ClientState.SessionID, guid);

                if (result != null && result.Value != null && !string.IsNullOrEmpty(result.Value.ToString()))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"56 CheckGuidePresent: Ошибка при проверке GUID {guid}: {ex.Message}");
                return false;
            }
        }

        public static AccessGroup GetAccessGroups(Guid idGroup)
        {
            try
            {
                using var integServ = ParsecClient.Create();
                var accessGroupsResult = integServ.GetAccessGroups(ClientState.SessionID);

                if (accessGroupsResult == null)
                {
                    Logger.Log<ParsecService>("Warning", $"99 GetAccessGroups: Результат null для SessionID: {ClientState.SessionID}");
                    return null;
                }

                var accesGroups = accessGroupsResult.ToList();

                foreach (var item in accesGroups)
                {
                    if (item != null && item.ID.Equals(idGroup))
                        return item;
                }

                Logger.Log<ParsecService>("Warning", $"111 GetAccessGroups: Группа с GUID {idGroup} не найдена");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Error", $"116 GetAccessGroups: Ошибка при получении групп доступа: {ex.Message}");
                return null;
            }
        }

        private static string BuildAddAccessCatQuery(DbModelRowIDInDev row)
        {
            var query = "select  p.guid, an.guid  from accessname an , people p " +
                        $"where p.id_pep = {row.ID_PEP} " +
                        $"and an.id_accessname = {row.ID_CARD}";

            Logger.Log<ParsecService>("Info", $"331 Executing query for models addAccessCat with ID_PEP={row.ID_PEP}, ID_accessname={row.ID_CARD}");
            Logger.Log<ParsecService>("Debug", $"332 Query: {query}");

            return query;
        }

        private static string BuildRemoveAccessCatQuery(DbModelRowIDInDev row)
        {
            var query = $"select p.guid, an.guid from accessname an, people p " +
                        $"where p.id_pep = {row.ID_PEP} " +
                        $"and an.id_accessname = {row.ID_CARD}";

            Logger.Log<ParsecService>("Info", $"868 Executing query for remove access cat with ID_PEP={row.ID_PEP}, ID_accessname={row.ID_CARD}");
            Logger.Log<ParsecService>("Debug", $"869 Query: {query}");

            return query;
        }

        /// <summary>
        /// Формирует идентификатор карты для AddPersonIdentifier. Если у карты в базе Артонит
        /// заполнен срок действия (CARD.TIMESTART/CARD.TIMEEND), возвращается IdentifierTemp
        /// с VALID_FROM/VALID_TO; если срока нет — прежний BaseIdentifier, чтобы не передавать
        /// в Парсек нулевые даты (01.01.0001).
        /// </summary>
        private static BaseIdentifier BuildCardIdentifier(DbModelRowIDInDev row, string hexValue, string personGuid)
        {
            DateTime? validFrom;
            DateTime? validTo;

            if (DatabaseService.TryGetCardPeriod(row.ID_CARD, out validFrom, out validTo))
            {
                Logger.Log<ParsecService>("Info",
                    $"Срок действия карты {row.ID_CARD}: VALID_FROM={validFrom.Value:dd.MM.yyyy HH:mm:ss}, VALID_TO={validTo.Value:dd.MM.yyyy HH:mm:ss}");

                return new IdentifierTemp
                {
                    IS_PRIMARY = true,
                    CODE = hexValue,
                    PERSON_ID = new Guid(personGuid),
                    // 0 — идентификатор подсистемы доступа «Parsec» (обычная карта),
                    // 1 — автомобильный номер; карте всегда соответствует 0.
                    IDENTIFTYPE = 0,
                    VALID_FROM = validFrom.Value,
                    VALID_TO = validTo.Value
                };
            }

            Logger.Log<ParsecService>("Warning",
                $"Срок действия карты {row.ID_CARD} не найден в базе Артонит, идентификатор добавляется без VALID_FROM/VALID_TO");

            return new BaseIdentifier
            {
                IS_PRIMARY = true,
                CODE = hexValue,
                PERSON_ID = new Guid(personGuid)
            };
        }

        private static OperationResult ProcessAccessCatAddition(DbModelRowIDInDev row, DbModelAccessCategoryForPeople model)
        {
            using var integServ = ParsecClient.Create();
            var accessGroupGuid = new Guid(model.GUID_ACCGROUP);
            var accesGroup = GetAccessGroups(accessGroupGuid);
            var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

            if (person == null)
            {
                Logger.Log<ParsecService>("Warning", $"291 Пользователь с GUID: {model.GUID_PEP} не найден в Parsec");
                return OperationResult.Ok("292 Пользователь не найден в Parsec");
            }

            var sessionResult = integServ.OpenPersonEditingSession(ClientState.SessionID, new Guid(model.GUID_PEP));
            if (sessionResult.Result != ClientState.Result_Success)
            {
                var errorMsg = $"299 Ошибка открытия сессии редактирования: {sessionResult.ErrorMessage}";
                Logger.Log<ParsecService>("Error", errorMsg);
                return OperationResult.Error(errorMsg, sessionResult.ErrorMessage, 7);
            }

            var editSessionID = sessionResult.Value;

            try
            {
                var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));
                if (identifiers == null || identifiers.Length == 0)
                {
                    Logger.Log<ParsecService>("Warning", $"312 У пользователя {person.FIRST_NAME} нет идентификаторов");
                    return OperationResult.Ok("Идентификаторы не найдены");
                }

                Logger.Log<ParsecService>("Info", $"Найдено {identifiers.Length} идентификаторов для {person.FIRST_NAME}");

                foreach (var identifier in identifiers)
                {
                    ProcessSingleIdentifier(identifier, accesGroup, accessGroupGuid, editSessionID, integServ, row);
                }

                return OperationResult.Ok("Операция выполнена успешно");
            }
            finally
            {
                CloseEditingSession(integServ, editSessionID);
            }
        }

        private static OperationResult ProcessAccessCatRemoval(DbModelRowIDInDev row, DbModelAccessCategoryForPeople model)
        {
            using var integServ = ParsecClient.Create();
            var accessGroupGuid = new Guid(model.GUID_ACCGROUP);
            var person = integServ.GetPerson(ClientState.SessionID, new Guid(model.GUID_PEP));

            if (person == null)
            {
                Logger.Log<ParsecService>("Warning", $"885 Пользователь с GUID: {model.GUID_PEP} не найден в Parsec");
                return OperationResult.Ok("886 Пользователь не найден в Parsec, задача удалена");
            }

            var sessionResult = integServ.OpenPersonEditingSession(ClientState.SessionID, new Guid(model.GUID_PEP));
            if (sessionResult.Result != ClientState.Result_Success)
            {
                var errorMsg = $"893 Ошибка открытия сессии редактирования: {sessionResult.ErrorMessage}";
                Logger.Log<ParsecService>("Error", errorMsg);
                return OperationResult.Error(errorMsg, sessionResult.ErrorMessage, 7);
            }

            var editSessionID = sessionResult.Value;

            try
            {
                var identifiers = integServ.GetPersonIdentifiers(ClientState.SessionID, new Guid(model.GUID_PEP));
                if (identifiers == null || identifiers.Length == 0)
                {
                    Logger.Log<ParsecService>("Warning", $"906 У пользователя {person.FIRST_NAME} нет идентификаторов");
                    return OperationResult.Ok("907 Идентификаторы не найдены, задача удалена");
                }

                Logger.Log<ParsecService>("Info", $"910 Найдено {identifiers.Length} идентификаторов для {person.FIRST_NAME}");

                foreach (var identifier in identifiers)
                {
                    ProcessSingleIdentifierRemoval(identifier, accessGroupGuid, editSessionID, integServ, row);
                }

                return OperationResult.Ok("923 Операция удаления выполнена успешно");
            }
            finally
            {
                CloseEditingSession(integServ, editSessionID);
            }
        }

        private static void ProcessSingleIdentifier(
            Identifier identifier, AccessGroup accesGroup, Guid accessGroupGuid,
            Guid editSessionID, IntegrationServiceSoapClient integServ, DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Info", $"350 Обработка карты {identifier.CODE} добавление категории доступ {accessGroupGuid}");

            var updatedIdentifier = identifier;
            var newGroupId = DetermineAccessGroup(identifier, accesGroup, accessGroupGuid, integServ);
            updatedIdentifier.ACCGROUP_ID = newGroupId;

            Logger.Log<ParsecService>("Info", $"362 Обновление идентификатора: CODE={updatedIdentifier.CODE}, добавляется группа ACCGROUP_ID={updatedIdentifier.ACCGROUP_ID}");

            var result = integServ.AddPersonIdentifier(editSessionID, updatedIdentifier);
            if (result.Result != ClientState.Result_Success)
            {
                throw new InvalidOperationException($"Ошибка обновления идентификатора: {result.ErrorMessage}");
            }
        }

        private static void ProcessSingleIdentifierRemoval(
            Identifier identifier, Guid accessGroupToRemove,
            Guid editSessionID, IntegrationServiceSoapClient integServ, DbModelRowIDInDev row)
        {
            Logger.Log<ParsecService>("Info", $"941 Обработка удаления для карты {identifier.CODE} {identifier.ACCGROUP_ID}. Удаляю категорию доступа {accessGroupToRemove}");

            if (identifier.ACCGROUP_ID == Guid.Empty)
            {
                Logger.Log<ParsecService>("Warning", $"946 У карты {identifier.CODE} нет привязанной группы доступа, пропускаем");
                return;
            }

            var inheritedAccessGroups = integServ.GetInheritedAccessGroups(ClientState.SessionID, identifier.ACCGROUP_ID)?.ToList()
                                        ?? new List<Guid>();

            Logger.Log<ParsecService>("Info", $"954 Текущая цепочка наследования: {string.Join(" -> ", inheritedAccessGroups)}");

            var removed = inheritedAccessGroups.Remove(accessGroupToRemove);
            if (!removed)
            {
                Logger.Log<ParsecService>("Warning", $"961 Группа {accessGroupToRemove} не найдена в цепочке наследования карты {identifier.CODE}");
            }

            Logger.Log<ParsecService>("Info", $"965 После удаления группы {accessGroupToRemove}: {string.Join(" -> ", inheritedAccessGroups)}");

            var updatedIdentifier = BuildUpdatedIdentifierAfterRemoval(identifier, inheritedAccessGroups, accessGroupToRemove, integServ);

            Logger.Log<ParsecService>("Info", $"975 Обновление идентификатора: CODE={updatedIdentifier.CODE}, ACCGROUP_ID={updatedIdentifier.ACCGROUP_ID}");

            var result = integServ.AddPersonIdentifier(editSessionID, updatedIdentifier);
            if (result.Result != ClientState.Result_Success)
            {
                throw new InvalidOperationException($"982 Ошибка обновления идентификатора: {result.ErrorMessage}");
            }

            Logger.Log<ParsecService>("Info", $"985 Карта {identifier.CODE} успешно обновлена, удалена группа {accessGroupToRemove}");
        }

        private static Guid DetermineAccessGroup(
            Identifier identifier, AccessGroup targetGroup, Guid targetGroupId, IntegrationServiceSoapClient integServ)
        {
            Logger.Log<ParsecService>("Info", $"382 Старт формирования группы для {identifier.CODE}. Уже имеется группа {identifier.ACCGROUP_ID}, добавляется группа {targetGroupId}");

            if (identifier.ACCGROUP_ID == Guid.Empty)
            {
                Logger.Log<ParsecService>("Info", $"385 У карты {identifier.CODE} нет группы, назначаем {targetGroupId}");
                return targetGroupId;
            }

            var inheritedChain = BuildInheritedChain(identifier.ACCGROUP_ID, targetGroupId, integServ);
            var existingGroup = CheckAccessGroups(inheritedChain);
            if (existingGroup != Guid.Empty)
            {
                Logger.Log<ParsecService>("Info", $"396 Найдена существующая группа {existingGroup}");
                return existingGroup;
            }

            return CreateAccessGroupWithInheritance(inheritedChain, integServ);
        }

        private static List<Guid> BuildInheritedChain(Guid currentGroupId, Guid targetGroupId, IntegrationServiceSoapClient integServ)
        {
            var inheritedGroups = integServ.GetInheritedAccessGroups(ClientState.SessionID, currentGroupId)?.ToList()
                                  ?? new List<Guid>();

            if (!inheritedGroups.Any())
                inheritedGroups.Add(currentGroupId);

            inheritedGroups.Add(targetGroupId);

            return inheritedGroups.Distinct().ToList();
        }

        private static Guid CreateAccessGroupWithInheritance(List<Guid> inheritedChain, IntegrationServiceSoapClient integServ)
        {
            var schedules = integServ.GetAccessSchedules(ClientState.SessionID);
            if (schedules == null || schedules.Length == 0)
                throw new InvalidOperationException("Нет доступных расписаний");

            var groupName = BuildGroupName(inheritedChain, integServ);

            var createResult = integServ.CreateAccessGroup(
                ClientState.SessionID,
                groupName,
                schedules[0].ID,
                null);

            if (createResult.Result != ClientState.Result_Success)
                throw new InvalidOperationException($"Ошибка создания группы: {createResult.ErrorMessage}");

            var newGroupId = createResult.Value;
            integServ.SetInheritedAccessGroups(ClientState.SessionID, newGroupId, inheritedChain.ToArray());

            Logger.Log<ParsecService>("Info", $"Создана группа {groupName} с цепочкой из {inheritedChain.Count} групп");
            return newGroupId;
        }

        private static string BuildGroupName(List<Guid> inheritedChain, IntegrationServiceSoapClient integServ)
        {
            var groupNames = inheritedChain
                .Select(groupId => GetAccessGroups(groupId)?.NAME)
                .Where(groupName => !string.IsNullOrEmpty(groupName))
                .ToList();

            var resultName = groupNames.Any()
                ? string.Join(" + ", groupNames)
                : "(Особая) Artsec";

            if (resultName.Length > 255)
            {
                resultName = resultName.Substring(0, 252) + "...";
            }

            Logger.Log<ParsecService>("Info", $"Сформировано имя группы: {resultName}");
            return resultName;
        }

        private static Identifier BuildUpdatedIdentifierAfterRemoval(
            Identifier originalIdentifier, List<Guid> inheritedGroups,
            Guid removedGroupId, IntegrationServiceSoapClient integServ)
        {
            var updatedIdentifier = originalIdentifier;

            if (inheritedGroups.Count == 0)
            {
                if (originalIdentifier.ACCGROUP_ID == removedGroupId)
                {
                    updatedIdentifier.ACCGROUP_ID = Guid.Empty;
                    Logger.Log<ParsecService>("Info", $"1004 У карты {originalIdentifier.CODE} не осталось групп, ACCGROUP_ID = Guid.Empty");
                }
            }
            else if (inheritedGroups.Count == 1)
            {
                updatedIdentifier.ACCGROUP_ID = inheritedGroups[0];
                Logger.Log<ParsecService>("Info", $"1011 У карты {originalIdentifier.CODE} осталась одна группа {inheritedGroups[0]}");
            }
            else
            {
                var existingGroup = CheckAccessGroups(inheritedGroups);
                if (existingGroup != Guid.Empty)
                {
                    updatedIdentifier.ACCGROUP_ID = existingGroup;
                    Logger.Log<ParsecService>("Info", $"1021 Найдена существующая группа {existingGroup} для цепочки");
                }
                else
                {
                    updatedIdentifier.ACCGROUP_ID = CreateAccessGroupWithInheritance(inheritedGroups, integServ);
                    Logger.Log<ParsecService>("Info", $"1027 Создана новая группа {updatedIdentifier.ACCGROUP_ID} для оставшейся цепочки");
                }
            }

            return updatedIdentifier;
        }

        private static void CloseEditingSession(IntegrationServiceSoapClient integServ, Guid sessionId)
        {
            if (integServ == null || sessionId == Guid.Empty)
            {
                Logger.Log<ParsecService>("Warning", "CloseEditingSession: сессия не была открыта, закрытие пропущено");
                return;
            }

            try
            {
                integServ.ClosePersonEditingSession(sessionId);
                Logger.Log<ParsecService>("Info", $"Сессия {sessionId} закрыта");
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"Ошибка при закрытии сессии {sessionId}: {ex.Message}");
            }
        }

        private static void CloseOrgUnitEditingSession(IntegrationServiceSoapClient integServ, Guid sessionId)
        {
            if (integServ == null || sessionId == Guid.Empty)
            {
                Logger.Log<ParsecService>("Warning", "CloseOrgUnitEditingSession: сессия не была открыта, закрытие пропущено");
                return;
            }

            try
            {
                integServ.CloseOrgUnitEditingSession(sessionId);
                Logger.Log<ParsecService>("Info", $"Сессия редактирования организации {sessionId} закрыта");
            }
            catch (Exception ex)
            {
                Logger.Log<ParsecService>("Warning", $"Ошибка при закрытии сессии организации {sessionId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Повторная доставка события после сбоя связи: объект уже создан в Парсеке.
        /// Это не ошибка интеграции, иначе очередь встанет навсегда.
        /// </summary>
        private static bool IsAlreadyExists(string message)
        {
            return ContainsAny(message,
                "already exists", "already exist", "duplicate", "уже существует", "уже создан");
        }

        /// <summary>Объект уже удалён/никогда не создавался — удаление считать успешным.</summary>
        private static bool IsNotFound(string message)
        {
            return ContainsAny(message,
                "not found", "no person", "no identifier", "no org", "no such",
                "не найден", "отсутств");
        }

        private static bool ContainsAny(string text, params string[] fragments)
        {
            if (string.IsNullOrWhiteSpace(text) || fragments == null)
            {
                return false;
            }

            foreach (string fragment in fragments)
            {
                if (text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}