using System.Globalization;
using FirebirdSql.Data.FirebirdClient;
using ParsecQueueViewer.Models;

namespace ParsecQueueViewer.Services
{
    /// <summary>
    /// Чтение очереди. Только SELECT: монитор не имеет ни одного пути, который
    /// меняет PARSEC_QUEUE, PARSEC_QUEUE_LOG или бизнес-таблицы Артонит.
    /// </summary>
    public sealed class QueueRepository
    {
        private const int MaxActiveRows = 5000;
        private const int CommandTimeoutSeconds = 30;

        private const string QueueColumns =
            "q.id, q.operation, q.id_pep, q.id_card, q.status, q.attempts, q.max_attempts, " +
            "q.next_attempt, q.lease_until, q.created_at, q.updated_at, q.started_at, q.finished_at, " +
            "q.error_code, q.error_text, q.duration_ms, q.worker_id, q.source_table, q.dedup_key ";

        public Task<QueueSnapshot> LoadAsync(
            IntegratorConfiguration configuration,
            int historyLimit,
            CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (historyLimit < 1)
            {
                historyLimit = 1000;
            }

            return Task.Run(
                () => Load(configuration, historyLimit, cancellationToken),
                cancellationToken);
        }

        public Task<IReadOnlyList<QueueLogEntry>> LoadHistoryAsync(
            IntegratorConfiguration configuration,
            long queueId,
            CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return Task.Run(
                () => LoadHistory(configuration, queueId, cancellationToken),
                cancellationToken);
        }

        private static QueueSnapshot Load(
            IntegratorConfiguration configuration,
            int historyLimit,
            CancellationToken cancellationToken)
        {
            using var connection = new FbConnection(configuration.ConnectionString);
            connection.Open();

            cancellationToken.ThrowIfCancellationRequested();

            Dictionary<int, int> counts = ReadStatusCounts(connection);
            int total = counts.Values.Sum();
            bool eventTriggerPresent = ReadEventTriggerPresence(connection);

            // Активные события читаем отдельно и без ограничения «последние N»:
            // иначе старое необработанное событие могло бы потеряться среди тысяч
            // успешных строк и не попасть на глаза интегратору.
            Dictionary<long, QueueRow> activeRows = ReadRows(
                connection,
                "select first " + (MaxActiveRows + 1) + " " + QueueColumns +
                "from parsec_queue q where q.status in (0, 1, 3, 4) order by q.id",
                cancellationToken);

            bool activeTruncated = activeRows.Count > MaxActiveRows;
            if (activeTruncated)
            {
                activeRows = activeRows.Values
                    .OrderBy(row => row.Id)
                    .Take(MaxActiveRows)
                    .ToDictionary(row => row.Id);
            }

            cancellationToken.ThrowIfCancellationRequested();

            Dictionary<long, QueueRow> historyRows = ReadRows(
                connection,
                "select first " + (historyLimit + 1) + " " + QueueColumns +
                "from parsec_queue q order by q.id desc",
                cancellationToken);

            bool historyTruncated = historyRows.Count > historyLimit;
            if (historyTruncated)
            {
                historyRows = historyRows.Values
                    .OrderByDescending(row => row.Id)
                    .Take(historyLimit)
                    .ToDictionary(row => row.Id);
            }

            // Активные строки имеют приоритет: они уже прочитаны без ограничения
            // по возрасту и должны остаться в выдаче, даже если не попали в последние N.
            Dictionary<long, QueueRow> rows = new(activeRows);
            foreach (KeyValuePair<long, QueueRow> pair in historyRows)
            {
                rows.TryAdd(pair.Key, pair.Value);
            }

            return new QueueSnapshot
            {
                Rows = rows.Values.ToArray(),
                StatusCounts = counts,
                TotalCount = total,
                ActiveRowsTruncated = activeTruncated,
                HistoryTruncated = historyTruncated,
                EventTriggerPresent = eventTriggerPresent,
                LoadedAt = DateTime.Now
            };
        }

        private static Dictionary<int, int> ReadStatusCounts(FbConnection connection)
        {
            Dictionary<int, int> counts = new();

            using FbCommand command = new(
                "select status, count(*) from parsec_queue group by status", connection)
            {
                CommandTimeout = CommandTimeoutSeconds
            };

            using FbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int status = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                int count = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                counts[status] = count;
            }

            return counts;
        }

        /// <summary>
        /// Есть ли в базе активный триггер, публикующий событие об изменении
        /// очереди. Если метаданные недоступны (нет прав на RDB$TRIGGERS),
        /// считаем, что триггер есть: предупреждение не должно пугать зря.
        /// </summary>
        private static bool ReadEventTriggerPresence(FbConnection connection)
        {
            try
            {
                using FbCommand command = new(
                    "select count(*) from rdb$triggers " +
                    "where rdb$trigger_name = 'PARSEC_QUEUE_EVENTS' " +
                    "and coalesce(rdb$trigger_inactive, 0) = 0", connection)
                {
                    CommandTimeout = CommandTimeoutSeconds
                };

                object value = command.ExecuteScalar();
                return Convert.ToInt32(value, CultureInfo.InvariantCulture) > 0;
            }
            catch
            {
                return true;
            }
        }

        private static Dictionary<long, QueueRow> ReadRows(
            FbConnection connection,
            string sql,
            CancellationToken cancellationToken)
        {
            Dictionary<long, QueueRow> rows = new();

            using FbCommand command = new(sql, connection)
            {
                CommandTimeout = CommandTimeoutSeconds
            };

            using FbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                QueueRow row = ReadQueueRow(reader);
                rows[row.Id] = row;
            }

            return rows;
        }

        private static QueueRow ReadQueueRow(FbDataReader reader)
        {
            return new QueueRow
            {
                Id = ReadLong(reader, 0) ?? 0,
                Operation = ReadInt(reader, 1) ?? 0,
                IdPep = ReadInt(reader, 2),
                IdCard = ReadString(reader, 3),
                Status = ReadInt(reader, 4) ?? 0,
                Attempts = ReadInt(reader, 5) ?? 0,
                MaxAttempts = ReadInt(reader, 6) ?? 0,
                NextAttempt = ReadDate(reader, 7),
                LeaseUntil = ReadDate(reader, 8),
                CreatedAt = ReadDate(reader, 9),
                UpdatedAt = ReadDate(reader, 10),
                StartedAt = ReadDate(reader, 11),
                FinishedAt = ReadDate(reader, 12),
                ErrorCode = ReadInt(reader, 13),
                ErrorText = ReadString(reader, 14),
                DurationMs = ReadInt(reader, 15),
                WorkerId = ReadString(reader, 16),
                SourceTable = ReadString(reader, 17),
                DedupKey = ReadString(reader, 18)
            };
        }

        private static IReadOnlyList<QueueLogEntry> LoadHistory(
            IntegratorConfiguration configuration,
            long queueId,
            CancellationToken cancellationToken)
        {
            List<QueueLogEntry> result = new();

            using var connection = new FbConnection(configuration.ConnectionString);
            connection.Open();

            using FbCommand command = new(
                "select first 200 l.id, l.queue_id, l.attempt_no, l.started_at, l.finished_at, " +
                "l.status, l.error_code, l.error_text, l.duration_ms, l.worker_id, l.details " +
                "from parsec_queue_log l where l.queue_id = @queue_id order by l.id desc", connection)
            {
                CommandTimeout = CommandTimeoutSeconds
            };
            command.Parameters.Add("@queue_id", FbDbType.BigInt).Value = queueId;

            using FbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                result.Add(new QueueLogEntry
                {
                    Id = ReadLong(reader, 0) ?? 0,
                    QueueId = ReadLong(reader, 1) ?? 0,
                    AttemptNo = ReadInt(reader, 2) ?? 0,
                    StartedAt = ReadDate(reader, 3),
                    FinishedAt = ReadDate(reader, 4),
                    Status = ReadInt(reader, 5),
                    ErrorCode = ReadInt(reader, 6),
                    ErrorText = ReadString(reader, 7),
                    DurationMs = ReadInt(reader, 8),
                    WorkerId = ReadString(reader, 9),
                    Details = ReadText(reader, 10)
                });
            }

            return result;
        }

        private static int? ReadInt(FbDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return null;
            }

            return Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static long? ReadLong(FbDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return null;
            }

            return Convert.ToInt64(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static string ReadString(FbDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return null;
            }

            object value = reader.GetValue(index);
            return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string ReadText(FbDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return null;
            }

            try
            {
                return reader.GetString(index);
            }
            catch
            {
                object value = reader.GetValue(index);
                return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static DateTime? ReadDate(FbDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                return null;
            }

            object value = reader.GetValue(index);
            if (value is DateTime dateTime)
            {
                return dateTime;
            }

            if (value is string text &&
                DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
