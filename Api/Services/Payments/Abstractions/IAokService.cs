using Api.Models.Owners;

namespace Api.Services.Payments.Abstractions
{
    public interface IAokService
    {
        Task<(Owner? owner, string? rawToken)> ResolveOrProvisionOwnerAsync(HttpContext http);

        Task<Owner?> ResolveOwnerAsync(HttpContext http);

        void SetAokCookie(HttpResponse res, string rawToken);
    }
}
