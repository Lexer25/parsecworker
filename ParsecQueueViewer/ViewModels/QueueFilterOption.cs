using ParsecQueueViewer.Infrastructure;

namespace ParsecQueueViewer.ViewModels
{
    public enum QueueFilterKind
    {
        Active,
        Pending,
        InWork,
        Problems,
        History,
        All
    }

    public sealed class QueueFilterOption : ObservableObject
    {
        private int _count;

        public QueueFilterOption(QueueFilterKind kind, string displayName, string description)
        {
            Kind = kind;
            DisplayName = displayName;
            Description = description;
        }

        public QueueFilterKind Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public int Count
        {
            get => _count;
            set
            {
                if (SetProperty(ref _count, value))
                {
                    OnPropertyChanged(nameof(CountDisplay));
                }
            }
        }

        public string CountDisplay => Count.ToString("N0");

        public bool Matches(QueueRowViewModel row)
        {
            if (row == null)
            {
                return false;
            }

            return Kind switch
            {
                QueueFilterKind.Active => row.Status is 0 or 1 or 3 or 4,
                QueueFilterKind.Pending => row.Status == 0,
                QueueFilterKind.InWork => row.Status == 1,
                QueueFilterKind.Problems => row.Status is 3 or 4,
                QueueFilterKind.History => row.Status is 2 or 5 or 6,
                _ => true
            };
        }

        public bool SortAscending => Kind is QueueFilterKind.Active or QueueFilterKind.Pending
            or QueueFilterKind.InWork or QueueFilterKind.Problems;
    }
}
