using Newtonsoft.Json;

namespace ParsecIntegrationClient.Models
{
    /// <summary>
    /// Статус выполнения одной операции. В state.json сериализуется прежними строками
    /// "OK"/"ERR"/"SKIP"/"WARN" (см. LegacyStatusConverter), чтобы внешний монитор не сломался.
    /// </summary>
    [JsonConverter(typeof(LegacyStatusConverter))]
    public enum OperationStatus
    {
        None,
        Ok,
        Error,
        Skip,
        Warn
    }
}