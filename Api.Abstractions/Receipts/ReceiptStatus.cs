using System.Text.Json.Serialization;

namespace Api.Abstractions.Receipts
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReceiptStatus
    {
        PendingParse = 0,
        Parsed = 1,
        ParsedNeedsReview = 2,
        FailedParse = 3
    }
}
