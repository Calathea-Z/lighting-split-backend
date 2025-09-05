using System.Text.Json.Serialization;

namespace Api.Abstractions.Receipts
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ParseStatus
    {
        Success = 0,
        Partial = 1,
        Failed = 2
    }
}
