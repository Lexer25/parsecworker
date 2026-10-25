using System;
using Newtonsoft.Json;
using ParsecIntegrationClient.Services;

namespace ParsecIntegrationClient.Models
{
    /// <summary>
    /// То, что пишется в state.json: контекст задачи + результат операции.
    /// Имена JSON-полей сохранены прежними (status, desc, errorMessage, ...),
    /// чтобы внешний монитор не сломался.
    /// </summary>
    public sealed class StateSnapshot
    {
        [JsonProperty("idCardindev")]
        public string IdCardindev { get; set; }

        [JsonProperty("operation")]
        public string Operation { get; set; }

        [JsonProperty("operationCode")]
        public string OperationCode { get; set; }

        [JsonProperty("attempts")]
        public string Attempts { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("keyNum")]
        public string KeyNum { get; set; }

        [JsonProperty("status")]
        public OperationStatus Status { get; set; }

        [JsonProperty("desc")]
        public string Description { get; set; }

        [JsonProperty("errorMessage")]
        public string ErrorMessage { get; set; }

        [JsonProperty("errorCode")]
        public int? ErrorCode { get; set; }

        [JsonProperty("nextStart")]
        public DateTime? NextStart { get; set; }

        /// <summary>Снимок «задач нет» — без контекста задачи.</summary>
        public static StateSnapshot Idle()
        {
            return new StateSnapshot
            {
                Status = OperationStatus.None,
                Description = "Задачи для обработки отсутствуют",
                Timestamp = DateTime.Now,
                NextStart = DateTime.Now.AddMinutes(SettingsService.ErrorTimeoutMinutes),
                KeyNum = Key.keyNumber
            };
        }

        /// <summary>Снимок по результату операции над строкой PARSEC_QUEUE.</summary>
        public static StateSnapshot From(DbModelQueueRow row, OperationResult result)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (result == null) throw new ArgumentNullException(nameof(result));

            var now = DateTime.Now;
            string operationCode = row.OPERATION.ToString();

            return new StateSnapshot
            {
                IdCardindev = row.ID.ToString(),
                Operation = StateService.GetOperationName(operationCode),
                OperationCode = operationCode,
                Attempts = row.ATTEMPTS.ToString(),
                Timestamp = now,
                KeyNum = Key.keyNumber,
                Status = result.Status,
                Description = result.Description,
                ErrorMessage = result.ErrorMessage,
                ErrorCode = result.ErrorCode,
                NextStart = result.IsError
                    ? now.AddMinutes(SettingsService.ErrorTimeoutMinutes)
                    : now
            };
        }

        /// <summary>Снимок по результату операции над конкретной задачей.</summary>
        public static StateSnapshot From(DbModelRowIDInDev row, OperationResult result)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (result == null) throw new ArgumentNullException(nameof(result));

            var now = DateTime.Now;
            return new StateSnapshot
            {
                IdCardindev = row.ID,
                Operation = StateService.GetOperationName(row.OPERATION),
                OperationCode = row.OPERATION,
                Attempts = row.ATTEMPS,
                Timestamp = now,
                KeyNum = Key.keyNumber,
                Status = result.Status,
                Description = result.Description,
                ErrorMessage = result.ErrorMessage,
                ErrorCode = result.ErrorCode,
                NextStart = result.IsError
                    ? now.AddMinutes(SettingsService.ErrorTimeoutMinutes)
                    : now
            };
        }
    }
}