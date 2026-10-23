using System;
using Newtonsoft.Json;

namespace ParsecIntegrationClient.Models
{
    /// <summary>
    /// Сохраняет в state.json прежние строки статуса: OK / ERR / SKIP / WARN.
    /// </summary>
    public sealed class LegacyStatusConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(OperationStatus) || objectType == typeof(OperationStatus?);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(ToLegacyString((OperationStatus)value));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return objectType == typeof(OperationStatus?) ? (object)null : OperationStatus.None;
            }

            var text = reader.Value == null ? null : reader.Value.ToString();
            var parsed = FromLegacyString(text);

            if (objectType == typeof(OperationStatus?))
            {
                return parsed;
            }

            return parsed ?? OperationStatus.None;
        }

        public static string ToLegacyString(OperationStatus status)
        {
            switch (status)
            {
                case OperationStatus.Ok: return "OK";
                case OperationStatus.Error: return "ERR";
                case OperationStatus.Skip: return "SKIP";
                case OperationStatus.Warn: return "WARN";
                default: return null;
            }
        }

        public static OperationStatus? FromLegacyString(string text)
        {
            switch ((text ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "OK": return OperationStatus.Ok;
                case "ERR": return OperationStatus.Error;
                case "SKIP": return OperationStatus.Skip;
                case "WARN": return OperationStatus.Warn;
                case "": return null;
                default: return OperationStatus.None;
            }
        }
    }
}