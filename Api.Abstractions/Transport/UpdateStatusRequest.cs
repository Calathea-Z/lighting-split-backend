using Api.Abstractions.Receipts;

namespace Api.Abstractions.Transport;

public sealed record UpdateStatusRequest(ReceiptStatus Status);
