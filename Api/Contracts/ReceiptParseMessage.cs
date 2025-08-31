namespace Api.Contracts;

public record ReceiptParseMessage(string Container, string Blob, string? ReceiptId);
