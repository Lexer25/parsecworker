namespace ParsecQueueViewer.Models
{
    /// <summary>Строка PARSEC_QUEUE. Модель только для чтения.</summary>
    public sealed class QueueRow
    {
        public long Id { get; set; }
        public int Operation { get; set; }
        public int? IdPep { get; set; }
        public string IdCard { get; set; }
        public int Status { get; set; }
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime? NextAttempt { get; set; }
        public DateTime? LeaseUntil { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int? ErrorCode { get; set; }
        public string ErrorText { get; set; }
        public int? DurationMs { get; set; }
        public string WorkerId { get; set; }
        public string SourceTable { get; set; }
        public string DedupKey { get; set; }
    }

    /// <summary>Попытка обработки из PARSEC_QUEUE_LOG.</summary>
    public sealed class QueueLogEntry
    {
        public long Id { get; set; }
        public long QueueId { get; set; }
        public int AttemptNo { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string ErrorText { get; set; }
        public int? DurationMs { get; set; }
        public string WorkerId { get; set; }
        public string Details { get; set; }
    }

    public sealed class QueueSnapshot
    {
        public IReadOnlyList<QueueRow> Rows { get; init; } = Array.Empty<QueueRow>();
        public IReadOnlyDictionary<int, int> StatusCounts { get; init; } = new Dictionary<int, int>();
        public int TotalCount { get; init; }
        public bool ActiveRowsTruncated { get; init; }
        public bool HistoryTruncated { get; init; }
        public DateTime LoadedAt { get; init; } = DateTime.Now;
    }
}
