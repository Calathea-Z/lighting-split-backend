using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateStatusDto(
    [property: Required]
    [property: MaxLength(32)]
    string Status
);
