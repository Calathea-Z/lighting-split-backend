using Api.Enums;

namespace Api.Models;

public class Receipt {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OwnerUserId { get; set; } //null = anonymous session
    public string OriginalFileUrl { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public decimal? SubTotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Tip { get; set; }
    public decimal? Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ReceiptStatus Status { get; set; } = ReceiptStatus.PendingParse;
    public string? ParseError { get; set; } // Added this property

    public List<ReceiptItem> Items { get; set; } = [];
}