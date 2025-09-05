using System.Text.Json.Serialization;

namespace Api.Abstractions.Receipts
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BaselineSource
    {
        Subtotal = 0,
        Total = 1,
        Items = 2
    }
}
