namespace ParsecIntegrationClient.Models
{
    /// <summary>
    /// Итог одного прохода по очереди. Failed = true означает, что обработка
    /// остановлена на неудачной операции: следующие строки в этом проходе не трогались.
    /// </summary>
    public sealed class QueueRunResult
    {
        public bool Failed { get; set; }
        public int Processed { get; set; }
        public long FailedId { get; set; }
        public string Error { get; set; }
    }
}
