using System;
using System.Collections.Generic;

namespace ParsecQueueViewer.Win32
{
    internal sealed class QueueRow
    {
        public long Id;
        public int Operation;
        public int? IdPep;
        public string IdCard;
        public int Status;
        public int Attempts;
        public int MaxAttempts;
        public DateTime? NextAttempt;
        public DateTime? LeaseUntil;
        public DateTime? CreatedAt;
        public DateTime? UpdatedAt;
        public DateTime? StartedAt;
        public DateTime? FinishedAt;
        public int? ErrorCode;
        public string ErrorText;
        public int? DurationMs;
        public string WorkerId;
        public string SourceTable;
        public string DedupKey;
    }

    internal sealed class QueueLogEntry
    {
        public long Id;
        public long QueueId;
        public int AttemptNo;
        public DateTime? StartedAt;
        public DateTime? FinishedAt;
        public int? Status;
        public int? ErrorCode;
        public string ErrorText;
        public int? DurationMs;
        public string WorkerId;
        public string Details;
    }

    internal sealed class QueueSnapshot
    {
        public List<QueueRow> Rows = new List<QueueRow>();
        public Dictionary<int, int> StatusCounts = new Dictionary<int, int>();
        public int TotalCount;
        public bool ActiveRowsTruncated;
        public bool HistoryTruncated;
        public DateTime LoadedAt = DateTime.Now;
    }

    internal enum QueueFilterKind
    {
        Active,
        Pending,
        InWork,
        Problems,
        History,
        All
    }
}
