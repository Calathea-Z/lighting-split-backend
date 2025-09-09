using Api.Contracts.Payment;
using Api.Models.Owners;

namespace Api.Services.Payments.Abstractions
{
    public interface IPaymentLinkBuilder
    {
        Task<PaymentLink> BuildAsync(OwnerPayoutMethod method, decimal amount, string note);
        Task<IReadOnlyList<PaymentLink>> BuildManyAsync(IEnumerable<OwnerPayoutMethod> methods, decimal amount, string note);
    }
}
