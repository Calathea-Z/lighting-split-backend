namespace Api.Dtos.Splits.Requests;

public sealed class SelfReportPaymentDto
{
    public string Token { get; set; } = string.Empty;
    public string? Note { get; set; }
}
