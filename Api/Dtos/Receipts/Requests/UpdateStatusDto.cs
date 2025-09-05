using Api.Abstractions.Receipts;
using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateStatusDto(
    [property: Required]
    ReceiptStatus Status
);
