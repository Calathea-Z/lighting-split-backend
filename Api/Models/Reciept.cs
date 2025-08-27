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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ReceiptItem> Items { get; set; } = [];
    
}