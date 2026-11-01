using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FirebirdSql.Data.FirebirdClient;

namespace ParsecQueueViewer.Win32
{
    internal sealed class QueueRepository
    {
        private const int MaxActiveRows = 5000;
        private const int CommandTimeoutSeconds = 30;

        private const string QueueColumns =
            "q.id, q.operation, q.id_pep, q.id_card, q.status, q.attempts, q.max_attempts, " +
            "q.next_attempt, q.lease_until, q.created_at, q.updated_at, q.started_at, q.finished_at, " +
            "q.error_code, q.error_text, q.duration_ms, q.worker_id, q.source_table, q.dedup_key ";

        private readonly string _connectionString;

        public QueueRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public QueueSnapshot LoadSnapshot(int historyLimit)
        {
            if (historyLimit < 1)
            {
                historyLimit = 1000;
            }

            using (FbConnection connection = new FbConnection(_connectionString))
            {
                connection.Open();

                Dictionary<int, int> counts = ReadStatusCounts(connection);
                Dictionary<long, QueueRow> active = ReadRows(
                    connection,
                    "select first " + (MaxActiveRows + 1) + " " + QueueColumns +
                    "from parsec_queue q where q.status in (0, 1, 3, 4) order by q.id");

                bool activeTruncated = active.Count > MaxActiveRows;
                if (activeTruncated)
                {
                    active = active.Values.OrderBy(r => r.Id).Take(MaxActiveRows).ToDictionary(r => r.Id);
                }

                Dictionary<long, QueueRow> history = ReadRows(
                    connection,
                    "select first " + (historyLimit + 1) + " " + QueueColumns +
                    "from parsec_queue q order by q.id desc");

                bool historyTruncated = history.Count > historyLimit;
                if (historyTruncated)
                {
                    history = history.Values.OrderByDescending(r => r.Id).Take(historyLimit)
                        .ToDictionary(r => r.Id);
                }

                Dictionary<long, QueueRow> merged = new Dictionary<long, QueueRow>(active);
                foreach (KeyValuePair<long, QueueRow> pair in history)
                {
                    if (!merged.ContainsKey(pair.Key))
                    {
                        merged.Add(pair.Key, pair.Value);
                    }
                }

                return new QueueSnapshot
                {
                    Rows = merged.Values.ToList(),
                    StatusCounts = counts,
                    TotalCount = counts.Values.Sum(),
                    ActiveRowsTruncated = activeTruncated,
                    HistoryTruncated = historyTruncated,
                    LoadedAt = DateTime.Now
                };
            }
        }

        public List<QueueLogEntry> LoadHistory(long queueId)
        {
            List<QueueLogEntry> result = new List<QueueLogEntry>();
            using (FbConnection connection = new FbConnection(_connectionString))
            {
                connection.Open();
                using (FbCommand command = new FbCommand(
                           "select first 200 l.id, l.queue_id, l.attempt_no, l.started_at, l.finished_at, " +
                           "l.status, l.error_code, l.error_text, l.duration_ms, l.worker_id, l.details " +
                           "from parsec_queue_log l where l.queue_id = @queue_id order by l.id desc",
                           connection))
                {
                    command.CommandTimeout = CommandTimeoutSeconds;
                    command.Parameters.Add("@queue_id", FbDbType.BigInt).Value = queueId;
                    using (FbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
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
                    }
                }
            }

            return result;
        }

        private static Dictionary<int, int> ReadStatusCounts(FbConnection connection)
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            using (FbCommand command = new FbCommand(
                       "select status, count(*) from parsec_queue group by status", connection))
            {
                command.CommandTimeout = CommandTimeoutSeconds;
                using (FbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        counts[Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)] =
                            Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                    }
                }
            }

            return counts;
        }

        private static Dictionary<long, QueueRow> ReadRows(FbConnection connection, string sql)
        {
            Dictionary<long, QueueRow> rows = new Dictionary<long, QueueRow>();
            using (FbCommand command = new FbCommand(sql, connection))
            {
                command.CommandTimeout = CommandTimeoutSeconds;
                using (FbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        QueueRow row = ReadQueueRow(reader);
                        rows[row.Id] = row;
                    }
                }
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
            if (value is DateTime)
            {
                return (DateTime)value;
            }

            DateTime parsed;
            if (value is string && DateTime.TryParse((string)value, CultureInfo.CurrentCulture,
                    DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
