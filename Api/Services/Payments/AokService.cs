using Api.Data;
using Api.Models.Owners;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Api.Services;

public sealed class AokService : IAokService
{
    private const string CookieName = "aok";
    private readonly LightningDbContext _db;

    public AokService(LightningDbContext db) => _db = db;

    public async Task<Owner?> ResolveOwnerAsync(HttpContext http)
    {
        // read from cookie, else header
        var token = http.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(token) &&
            http.Request.Headers.TryGetValue("X-AOK", out var hdr) &&
            !string.IsNullOrWhiteSpace(hdr))
        {
            token = hdr.ToString();
        }
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = HashToken(token);
        var owner = await _db.Owners
            .FirstOrDefaultAsync(o => o.KeyHash == hash && o.RevokedAt == null);

        if (owner != null)
        {
            owner.LastSeenAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }
        return owner;
    }

    public void SetAokCookie(HttpResponse res, string rawToken)
    {
        res.Cookies.Append(CookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true
        });
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_'); // base64url
    }
}
