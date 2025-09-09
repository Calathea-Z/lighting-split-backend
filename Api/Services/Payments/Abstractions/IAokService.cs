using Api.Models.Owners;

namespace Api.Services.Payments.Abstractions
{
    public interface IAokService
    {
        Task<Owner?> ResolveOwnerAsync(HttpContext http);   // reads cookie/header and returns Owner or null
        void SetAokCookie(HttpResponse res, string rawToken); // optional (if you add a bootstrap route)
    }
}
