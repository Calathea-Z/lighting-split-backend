using System.Collections.Generic;

namespace Api.Abstractions.Transport;

/// <summary>Atomic replace request for a receipt’s items collection.</summary>
public sealed record ReplaceReceiptItemsRequest(
    IReadOnlyList<ReplaceReceiptItemDto> Items
);
