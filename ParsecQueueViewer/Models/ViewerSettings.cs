namespace ParsecQueueViewer.Models
{
    public sealed class ViewerSettings
    {
        public string ConfigPath { get; set; }
        public bool AutoRefresh { get; set; } = true;
        public int RefreshSeconds { get; set; } = 10;
        public int HistoryLimit { get; set; } = 1000;
    }
}
