using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateRawTextDto(
    [property: Required]
    [property: MaxLength(100_000)]
    string RawText
);
