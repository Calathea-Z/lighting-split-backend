using System.Text.Json.Serialization;

namespace Api.Abstractions.Receipts
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ParseEngine
    {
        Heuristics = 0,
        Llm        = 1
    }
}