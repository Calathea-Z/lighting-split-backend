using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateRawTextDto(
    [param: Required(AllowEmptyStrings = false)]
    [param: MaxLength(100_000)]
    string RawText
)
{
    public string RawText { get; init; } = RawText.Trim();
}
