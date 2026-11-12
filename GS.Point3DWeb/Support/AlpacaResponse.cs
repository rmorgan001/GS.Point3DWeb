using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GS.Alpaca.Support
{
    public class AlpacaResponse
    {
        public string MethodName { get; set; } = string.Empty;
        public string MethodType { get; set; } = string.Empty;
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;

        [JsonProperty($"ClientTransactionID")] public int ClientTransactionId { get; set; }
        [JsonProperty($"ServerTransactionID")] public int ServerTransactionId { get; set; }
        [JsonProperty($"ErrorNumber")] public int ErrorNumber { get; set; }
        [JsonProperty($"ErrorMessage")] public string? ErrorMessage { get; set; }
        [JsonProperty($"Value")] public required JToken Value { get; set; }
    }
}
