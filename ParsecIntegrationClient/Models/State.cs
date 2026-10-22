using Newtonsoft.Json;
using System;

namespace ParsecIntegrationClient.Models
{
    public class State
    {
        [JsonProperty("id_cardindev")]
        public string IdCardindev { get; set; }

        [JsonProperty("operation_code")]
        public string OperationCode { get; set; }

        [JsonProperty("operation")]
        public string Operation { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("desc")]
        public string desc { get; set; }

        [JsonProperty("error")]
        public string ErrorMessage { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("attempts")]
        public string Attempts { get; set; }
        [JsonProperty("nextStart")]
        //public DateTime NextStart { get; set; }
        public DateTime? NextStart { get; set; }
        [JsonProperty("ErrorCode")]
        public int ErrorCode { get; set; }
        [JsonProperty("keyNumber")]
        public string keyNum { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}
