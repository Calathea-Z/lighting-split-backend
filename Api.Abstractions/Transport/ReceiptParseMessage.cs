namespace Api.Abstractions.Transport;

/// <summary>Message used to kick off parsing of an uploaded receipt blob.</summary>
public sealed record ReceiptParseMessage(
    string Container,
    string Blob,
    string ReceiptId
);
