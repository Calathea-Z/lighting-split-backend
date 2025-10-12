namespace Api.Dtos.Splits.Responses;

public sealed class ListSplitResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ReceiptId { get; set; }
    public bool IsFinalized { get; set; }
    public bool HasShareCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
