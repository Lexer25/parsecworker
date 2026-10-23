using System;

namespace ParsecIntegrationClient.Models
{
    /// <summary>
    /// Результат одной операции ParsecService.*. Не содержит контекста задачи
    /// (IdCardindev, Attempts, keyNum) — только то, что относится к самой операции.
    /// Создаётся только через фабрики, чтобы нельзя было собрать противоречивое
    /// состояние (например, Ok с ErrorCode).
    /// </summary>
    public sealed class OperationResult
    {
        public OperationStatus Status { get; private set; }
        public string Description { get; private set; }
        public string ErrorMessage { get; private set; }
        public int? ErrorCode { get; private set; }

        private OperationResult() { }

        public static OperationResult Ok(string description)
        {
            return new OperationResult
            {
                Status = OperationStatus.Ok,
                Description = description
            };
        }

        public static OperationResult Error(string description, string errorMessage, int errorCode)
        {
            if (errorCode <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode), "ErrorCode должен быть > 0");
            }

            return new OperationResult
            {
                Status = OperationStatus.Error,
                Description = description,
                ErrorMessage = CleanMessage(errorMessage),
                ErrorCode = errorCode
            };
        }

        public static OperationResult Skip(string description)
        {
            return new OperationResult
            {
                Status = OperationStatus.Skip,
                Description = description
            };
        }

        public static OperationResult Warn(string description, string errorMessage)
        {
            return new OperationResult
            {
                Status = OperationStatus.Warn,
                Description = description,
                ErrorMessage = CleanMessage(errorMessage)
            };
        }

        public bool IsError
        {
            get { return Status == OperationStatus.Error; }
        }

        private static string CleanMessage(string message)
        {
            return message == null ? null : message.Replace("\r\n", " ").Replace("\n", " ");
        }
    }
}