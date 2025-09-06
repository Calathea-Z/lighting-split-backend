// Api/Dtos/Receipts/Requests/UpdateStatusDto.cs
using Api.Abstractions.Receipts;
using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateStatusDto(
    [param: Required] ReceiptStatus Status
);