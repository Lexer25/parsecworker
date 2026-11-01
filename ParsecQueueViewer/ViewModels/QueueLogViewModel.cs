using System.Globalization;
using ParsecQueueViewer.Models;

namespace ParsecQueueViewer.ViewModels
{
    public sealed class QueueLogViewModel
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        public QueueLogViewModel(QueueLogEntry entry)
        {
            Model = entry ?? throw new ArgumentNullException(nameof(entry));

            AttemptNo = entry.AttemptNo;
            Status = entry.Status;
            StatusName = entry.Status.HasValue
                ? QueueRowViewModel.GetStatusName(entry.Status.Value)
                : "—";
            StartedDisplay = FormatDate(entry.StartedAt);
            FinishedDisplay = FormatDate(entry.FinishedAt);
            DurationDisplay = QueueRowViewModel.FormatDuration(entry.DurationMs);
            WorkerDisplay = string.IsNullOrWhiteSpace(entry.WorkerId) ? "—" : entry.WorkerId;
            ErrorCodeDisplay = entry.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "—";
            ErrorText = entry.ErrorText;
            ErrorDisplay = string.IsNullOrWhiteSpace(entry.ErrorText) ? "—" : entry.ErrorText;
            Details = entry.Details;
            DetailsDisplay = string.IsNullOrWhiteSpace(entry.Details)
                ? "—"
                : entry.Details.Replace("\r", " ").Replace("\n", " ");
        }

        public QueueLogEntry Model { get; }

        public int AttemptNo { get; }
        public int? Status { get; }
        public string StatusName { get; }
        public string StartedDisplay { get; }
        public string FinishedDisplay { get; }
        public string DurationDisplay { get; }
        public string WorkerDisplay { get; }
        public string ErrorCodeDisplay { get; }
        public string ErrorText { get; }
        public string ErrorDisplay { get; }
        public string Details { get; }
        public string DetailsDisplay { get; }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("dd.MM.yyyy HH:mm:ss", Ru) : "—";
        }
    }
}
