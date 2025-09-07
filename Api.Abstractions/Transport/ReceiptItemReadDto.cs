using System;

namespace Api.Abstractions.Transport;

public sealed record ReceiptItemReadDto(
    Guid Id,
    string Label,
    decimal Qty,
    decimal UnitPrice,
    decimal LineSubtotal,
    decimal LineTotal,
    int Position,
    bool IsSystemGenerated,
    string? Unit = null,
    string? Category = null,
    string? Notes = null
);
