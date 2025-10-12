using Api.Models.Splits;

namespace Api.Dtos.Splits.Requests;

public sealed class UpdatePaymentStatusDto
{
    public PaymentStatus Status { get; set; }
    public string? Source { get; set; }
    public string? Note { get; set; }
    public string? CorrelationId { get; set; }
}
