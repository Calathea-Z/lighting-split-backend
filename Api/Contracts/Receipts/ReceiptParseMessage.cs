namespace Api.Contracts.Receipts;

public record ReceiptParseMessage(string Container, string Blob, string? ReceiptId);
