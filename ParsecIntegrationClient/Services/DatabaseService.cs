using FirebirdSql.Data.FirebirdClient;
using ParsecIntegrationClient.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace ParsecIntegrationClient.Services
{
    public class DatabaseService
    {
        public static string GetString(string query)
        {
            var connectionString = SettingsService.DatabaseConnectionString;

            using (var connection = new FbConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    var cmd = new FbCommand(query, connection);
                    var result = cmd.ExecuteScalar();
                    return result == null ? null : result.ToString();
                }
                catch (Exception ex)
                {
                    Logger.Log<DatabaseService>("Error", $"DatabaseService.GetString failed: {ex.Message}");
                    return null;
                }
            }
        }

        public static List<T> GetList<T>(string query)
        {
            var rows = new List<T>();

            var connectionString = SettingsService.DatabaseConnectionString;

            using (var connection = new FbConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    var cmd = new FbCommand(query, connection);

                    using (var dr = cmd.ExecuteReader())
                    {
                        while (dr.Read())
                        {
                            var instance = (T) Activator.CreateInstance(typeof(T));

                            int i = 0;
                            var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Static | 
                                BindingFlags.NonPublic | BindingFlags.Public);

                            foreach (var field in fields)
                            {
                                field.SetValue(instance, dr.GetValue(i).ToString()); 
                                i++;
                            }

                            rows.Add(instance);
                        }
                    }

                    return rows;
                }
                catch (Exception ex)
                {
                    Logger.Log<DatabaseService>("Exception", $"{ex.Message}");
                }
            }

            return rows;
        }

        /// <summary>
        /// Одна строка запроса → объект модели. Возвращает null, если строк нет.
        ///
        /// Раньше при пустом результате возвращался пустой объект (все поля null),
        /// поэтому проверки вида "model == null" у вызывающего кода не срабатывали,
        /// а падение случалось позже и в невнятном месте:
        /// "Value cannot be null. (Parameter 'g')" на new Guid(null).
        /// Пустой результат — это не сбой, это отсутствие данных.
        /// Нули в колонках (NULL) переносятся в поля как null.
        /// </summary>
        public static T Get<T>(string query)
        {

           // Logger.Log<DatabaseService>("Info", $"60 Выполняется запрос к базе данных: {query}");

            var connectionString = SettingsService.DatabaseConnectionString;

            using (var connection = new FbConnection(connectionString))
            {
                try
                {
                    connection.Open();

                    var cmd = new FbCommand(query, connection);

                    using (var dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                        {
                            var instance = (T) Activator.CreateInstance(typeof(T));

                            int i = 0;
                            var fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Static |
                                BindingFlags.NonPublic | BindingFlags.Public);

                            foreach (var field in fields)
                            {
                                object value = dr.IsDBNull(i) ? null : dr.GetValue(i);
                                field.SetValue(instance, value == null ? null : value.ToString());
                                i++;
                            }

                            return instance;
                        }

                        Logger.Log<DatabaseService>("Warning", $"91 Результат запроса пустой");
                        return default(T);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log<DatabaseService>("Exception", $"97 {ex.Message}");
                }
            }

            return default(T);
        }

        /// <summary>
        /// Читает состояние карты из базы СКУД Артонит (CARD + PEOPLE.GUID): срок
        /// действия, тип карты, ACTIVE и GUID человека в Парсеке.
        /// Нужно и для добавления идентификатора (операция 9), и для изменения его
        /// свойств (операция 11): тип объекта в SOAP выбирается по TIMEEND, а лимит
        /// проходов — по ACTIVE.
        /// Возвращает false, только если карты нет в базе (или сбой чтения).
        /// NULL в TIMESTART/TIMEEND — нормальное значение «срок не задан», карта при
        /// этом найдена.
        /// </summary>
        public static bool TryGetCardState(string idCard, out DbModelCardState state)
        {
            state = null;

            if (string.IsNullOrWhiteSpace(idCard))
            {
                Logger.Log<DatabaseService>("Warning", "TryGetCardState: не задан ID_CARD, состояние карты не читается");
                return false;
            }

            var query = "select c.id_card, c.id_pep, p.guid, c.id_cardtype, c.timestart, c.timeend, c.\"ACTIVE\" " +
                        "from card c left join people p on p.id_pep = c.id_pep " +
                        $"where c.id_card='{idCard.Replace("'", "''")}'";
            Logger.Log<DatabaseService>("Debug", $"TryGetCardState: {query}");

            try
            {
                var connectionString = SettingsService.DatabaseConnectionString;

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();

                    using (var cmd = new FbCommand(query, connection))
                    using (var dr = cmd.ExecuteReader())
                    {
                        if (!dr.Read())
                        {
                            Logger.Log<DatabaseService>("Warning", $"TryGetCardState: карта {idCard} не найдена в таблице CARD");
                            return false;
                        }

                        state = new DbModelCardState
                        {
                            ID_CARD = Convert.ToString(dr.GetValue(0)),
                            ID_PEP = ReadIntValue(dr, 1, "id_pep", idCard),
                            GUID_PEP = dr.IsDBNull(2) ? null : Convert.ToString(dr.GetValue(2)),
                            ID_CARDTYPE = ReadIntValue(dr, 3, "id_cardtype", idCard),
                            TIME_START = ReadDateValue(dr, 4, "timestart", idCard),
                            TIME_END = ReadDateValue(dr, 5, "timeend", idCard),
                            ACTIVE = ReadIntValue(dr, 6, "ACTIVE", idCard)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log<DatabaseService>("Exception", $"TryGetCardState: ошибка чтения состояния карты {idCard}: {ex.Message}");
                state = null;
                return false;
            }

            Logger.Log<DatabaseService>("Info",
                $"TryGetCardState: карта {state.ID_CARD} ID_PEP={state.ID_PEP} GUID_PEP={state.GUID_PEP} " +
                $"ID_CARDTYPE={Nullable(state.ID_CARDTYPE)} ACTIVE={Nullable(state.ACTIVE)} " +
                $"TIMESTART={Nullable(state.TIME_START)} TIMEEND={Nullable(state.TIME_END)}");
            return true;
        }

        /// <summary>
        /// Значение колонки срока действия карты → DateTime. В базе Артонит это
        /// TIMESTAMP/DATE (Firebird отдаёт DateTime); строку разбираем на случай
        /// других типов колонки. TIME (TimeSpan) датой не является: без даты
        /// срок действия карты задать нельзя.
        /// </summary>
        private static DateTime? ReadDateValue(FbDataReader dr, int index, string columnName, string idCard)
        {
            if (dr.IsDBNull(index))
            {
                return null;
            }

            var value = dr.GetValue(index);

            if (value is DateTime dateTime)
            {
                return dateTime;
            }

            if (value is string text)
            {
                if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
                    || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    return parsed;
                }
            }

            Logger.Log<DatabaseService>("Error",
                $"TryGetCardState: CARD.{columnName}={value} у карты {idCard} не является датой (тип {value?.GetType().Name})");
            return null;
        }

        /// <summary>Целочисленное поле карты (SMALLINT/INTEGER) → int?; NULL остаётся null.</summary>
        private static int? ReadIntValue(FbDataReader dr, int index, string columnName, string idCard)
        {
            if (dr.IsDBNull(index))
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(dr.GetValue(index));
            }
            catch (Exception ex)
            {
                Logger.Log<DatabaseService>("Error",
                    $"TryGetCardState: CARD.{columnName}={dr.GetValue(index)} у карты {idCard} не является числом: {ex.Message}");
                return null;
            }
        }

        private static string Nullable(object value)
        {
            return value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Проверяет, что база Артонит доступна: открывает соединение и выполняет простой запрос.
        /// Нужна, чтобы ошибка подключения была видна в журнале сразу, а не только в момент
        /// обработки очереди (раньше при недоступном Парсеке до БД дело вообще не доходило).
        /// </summary>
        public static bool CheckConnection(out string error)
        {
            error = null;

            string connectionString = SettingsService.DatabaseConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                error = "строка подключения не задана (Service:DatabaseConnectionString в appsettings.json)";
                return false;
            }

            try
            {
                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();

                    using (var command = new FbCommand("select 1 from rdb$database", connection))
                    {
                        command.ExecuteScalar();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        // ==================== Очередь PARSEC_QUEUE ====================

        /// <summary>
        /// Первые (по id) незавершённые события. Порядок по id обязателен: интегратор
        /// обрабатывает очередь строго последовательно и останавливается на первой ошибке.
        /// </summary>
        public static List<DbModelQueueRow> GetQueueBatch(int maxRows)
        {
            if (maxRows <= 0) maxRows = 100;
            if (maxRows > 1000) maxRows = 1000;

            var rows = new List<DbModelQueueRow>();
            string query =
                "select first " + maxRows + " q.id, q.operation, q.id_pep, q.id_card, " +
                "q.attempts, q.max_attempts, q.status, q.next_attempt " +
                "from parsec_queue q where q.status in (0, 3) order by q.id";

            using (var connection = new FbConnection(SettingsService.DatabaseConnectionString))
            {
                connection.Open();

                using (var cmd = new FbCommand(query, connection))
                using (var dr = cmd.ExecuteReader())
                {
                    while (dr.Read())
                    {
                        rows.Add(new DbModelQueueRow
                        {
                            ID = Convert.ToInt64(dr.GetValue(0)),
                            OPERATION = Convert.ToInt32(dr.GetValue(1)),
                            ID_PEP = dr.IsDBNull(2) ? (int?)null : Convert.ToInt32(dr.GetValue(2)),
                            ID_CARD = dr.IsDBNull(3) ? null : Convert.ToString(dr.GetValue(3)),
                            ATTEMPTS = Convert.ToInt32(dr.GetValue(4)),
                            MAX_ATTEMPTS = Convert.ToInt32(dr.GetValue(5)),
                            STATUS = Convert.ToInt32(dr.GetValue(6))
                        });
                    }
                }
            }

            return rows;
        }

        /// <summary>Забирает строку в работу. false — строку уже забрал другой экземпляр.</summary>
        public static bool TryClaimQueueRow(long id, string workerId, DateTime leaseUntil)
        {
            const string sql =
                "update parsec_queue set status = 1, started_at = current_timestamp, " +
                "updated_at = current_timestamp, worker_id = @worker, lease_until = @lease " +
                "where id = @id and status in (0, 3)";

            using (var connection = new FbConnection(SettingsService.DatabaseConnectionString))
            {
                connection.Open();

                using (var cmd = new FbCommand(sql, connection))
                {
                    cmd.Parameters.Add("@id", FbDbType.BigInt).Value = id;
                    cmd.Parameters.Add("@worker", FbDbType.VarChar).Value = workerId;
                    cmd.Parameters.Add("@lease", FbDbType.TimeStamp).Value = leaseUntil;
                    return cmd.ExecuteNonQuery() == 1;
                }
            }
        }

        public static void CompleteQueueRow(long id, int attempts, int durationMs)
        {
            const string sql =
                "update parsec_queue set status = 2, finished_at = current_timestamp, " +
                "updated_at = current_timestamp, attempts = @attempts, duration_ms = @duration, " +
                "error_code = null, error_text = null, lease_until = null where id = @id";

            ExecuteQueueUpdate(sql, cmd =>
            {
                cmd.Parameters.Add("@id", FbDbType.BigInt).Value = id;
                cmd.Parameters.Add("@attempts", FbDbType.Integer).Value = attempts;
                cmd.Parameters.Add("@duration", FbDbType.Integer).Value = durationMs;
            });
        }

        public static void SkipQueueRow(long id, int attempts, int durationMs, string reason)
        {
            const string sql =
                "update parsec_queue set status = 5, finished_at = current_timestamp, " +
                "updated_at = current_timestamp, attempts = @attempts, duration_ms = @duration, " +
                "error_code = null, error_text = @reason, lease_until = null where id = @id";

            ExecuteQueueUpdate(sql, cmd =>
            {
                cmd.Parameters.Add("@id", FbDbType.BigInt).Value = id;
                cmd.Parameters.Add("@attempts", FbDbType.Integer).Value = attempts;
                cmd.Parameters.Add("@duration", FbDbType.Integer).Value = durationMs;
                cmd.Parameters.Add("@reason", FbDbType.VarChar).Value = Shorten(reason, 1000);
            });
        }

        public static void FailQueueRow(long id, int attempts, int status, int? errorCode,
            string errorText, int durationMs)
        {
            const string sql =
                "update parsec_queue set status = @status, finished_at = current_timestamp, " +
                "updated_at = current_timestamp, attempts = @attempts, duration_ms = @duration, " +
                "error_code = @code, error_text = @text, lease_until = null, " +
                "next_attempt = current_timestamp where id = @id";

            ExecuteQueueUpdate(sql, cmd =>
            {
                cmd.Parameters.Add("@id", FbDbType.BigInt).Value = id;
                cmd.Parameters.Add("@status", FbDbType.Integer).Value = status;
                cmd.Parameters.Add("@attempts", FbDbType.Integer).Value = attempts;
                cmd.Parameters.Add("@duration", FbDbType.Integer).Value = durationMs;
                cmd.Parameters.Add("@code", FbDbType.Integer).Value =
                    errorCode.HasValue ? (object)errorCode.Value : DBNull.Value;
                cmd.Parameters.Add("@text", FbDbType.VarChar).Value = Shorten(errorText, 1000);
            });
        }

        /// <summary>Пишет попытку в историю. Ошибка журнала не должна ломать обработку.</summary>
        public static void WriteQueueLog(long queueId, int attemptNo, DateTime startedAt, int status,
            int? errorCode, string errorText, int durationMs, string workerId, string details)
        {
            const string sql =
                "insert into parsec_queue_log (queue_id, attempt_no, started_at, finished_at, " +
                "status, error_code, error_text, duration_ms, worker_id, details) values " +
                "(@queue, @attempt, @started, current_timestamp, @status, @code, @text, " +
                "@duration, @worker, @details)";

            try
            {
                using (var connection = new FbConnection(SettingsService.DatabaseConnectionString))
                {
                    connection.Open();

                    using (var cmd = new FbCommand(sql, connection))
                    {
                        cmd.Parameters.Add("@queue", FbDbType.BigInt).Value = queueId;
                        cmd.Parameters.Add("@attempt", FbDbType.Integer).Value = attemptNo;
                        cmd.Parameters.Add("@started", FbDbType.TimeStamp).Value = startedAt;
                        cmd.Parameters.Add("@status", FbDbType.Integer).Value = status;
                        cmd.Parameters.Add("@code", FbDbType.Integer).Value =
                            errorCode.HasValue ? (object)errorCode.Value : DBNull.Value;
                        cmd.Parameters.Add("@text", FbDbType.VarChar).Value = Shorten(errorText, 1000);
                        cmd.Parameters.Add("@duration", FbDbType.Integer).Value = durationMs;
                        cmd.Parameters.Add("@worker", FbDbType.VarChar).Value = workerId;
                        cmd.Parameters.Add("@details", FbDbType.Text).Value =
                            string.IsNullOrWhiteSpace(details) ? (object)DBNull.Value : details;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log<DatabaseService>("Exception", "Не удалось записать историю очереди: " + ex.Message);
            }
        }

        /// <summary>
        /// Возвращает в работу строки, зависшие в статусе 1 (служба упала во время SOAP-вызова).
        /// Вызывается при старте Worker.
        /// </summary>
        public static int RecoverStaleQueueRows()
        {
            const string sql =
                "update parsec_queue set status = 3, next_attempt = current_timestamp, " +
                "lease_until = null, updated_at = current_timestamp, " +
                "error_text = 'Сброшено при старте службы' " +
                "where status = 1 and (lease_until is null or lease_until < current_timestamp)";

            using (var connection = new FbConnection(SettingsService.DatabaseConnectionString))
            {
                connection.Open();

                using (var cmd = new FbCommand(sql, connection))
                {
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        private static void ExecuteQueueUpdate(string sql, Action<FbCommand> bind)
        {
            using (var connection = new FbConnection(SettingsService.DatabaseConnectionString))
            {
                connection.Open();

                using (var cmd = new FbCommand(sql, connection))
                {
                    bind(cmd);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }

        public static void DeleteIdInDevById(string id)
        {
            try
            {
                Logger.Log<DatabaseService>("Warning", $"108 Удаление записи в таблице CardInDev | ID {id}");

                var connectionString = SettingsService.DatabaseConnectionString;

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();

                    var stringCommand = $"delete from CARDINDEV cg where cg.id_cardindev = {id}";
                    var cmd = new FbCommand(stringCommand, connection);
                    var affectedRows = cmd.ExecuteNonQuery();
                    Logger.Log<DatabaseService>("Warning", $"109 Удалено записей: {affectedRows} | ID {id}");
                    
                    if (affectedRows == 0)
                    {
                        Logger.Log<DatabaseService>("Error", $"110 Не удалось удалить запись | ID {id} - записи не найдены");
                    }
                }
            }
            catch (Exception ex) 
            {
                Logger.Log<DatabaseService>("Exception", $"123 {ex.Message}");
            }
        }

        public static void IncrementAttemp(DbModelRowIDInDev row)
        {
            try
            {
                //Logger.Log<DatabaseService>("Warning", $"131 Инкремент попыток | ID: {row.ID}");

                var attemps = Convert.ToInt32(row.ATTEMPS);
                attemps++;

                var connectionString = SettingsService.DatabaseConnectionString;

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();

                    var stringCommand = $"update cardindev cd set cd.attempts = {attemps} where cd.id_cardindev={row.ID}";
                    var cmd = new FbCommand(stringCommand, connection);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger.Log<DatabaseService>("Exception", $"149 {ex.Message}");
            }
        }

        public static void DeleteMethod(string id)
        {
            try
            {
                Logger.Log<DatabaseService>("Warning", $"DeleteMethod вызван для ID: {id}");

                var connectionString = SettingsService.DatabaseConnectionString;

                using (var connection = new FbConnection(connectionString))
                {
                    connection.Open();

                    var stringCommand = $"delete from CARDINDEV cg where cg.id_cardindev = {id}";
                    var cmd = new FbCommand(stringCommand, connection);
                    var affectedRows = cmd.ExecuteNonQuery();
                    Logger.Log<DatabaseService>("Warning", $"DeleteMethod удалено записей: {affectedRows} | ID {id}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log<DatabaseService>("Exception", $"DeleteMethod ошибка: {ex.Message}");
            }
        }
    }
}

