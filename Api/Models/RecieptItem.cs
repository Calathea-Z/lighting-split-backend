namespace Api.Models;

public class ReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = default!;
    public string Label { get; set; } = "";
    public int Qty { get; set; } = 1;
    public decimal UnitPrice { get; set; } = 0m;
}
