using Api.Data;
using Api.Models.Owners;
using Api.Options;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Api.Services.Payments;

public sealed class AokService : IAokService
{
    // __Host- prefix requires: Secure; Path=/; and NO Domain attribute
    private const string CookieName = "__Host-aok";

    private readonly LightningDbContext _db;
    private readonly byte[] _pepper; // HMAC key (server-side secret)

    public AokService(LightningDbContext db, IOptions<AokSecurityOptions> opts)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        if (opts?.Value is null) throw new ArgumentNullException(nameof(opts));

        if (string.IsNullOrWhiteSpace(opts.Value.PepperBase64))
            throw new ArgumentException("AOK pepper is not configured.", nameof(opts));

        try
        {
            _pepper = Convert.FromBase64String(opts.Value.PepperBase64);
            if (_pepper.Length < 32) // 256-bit minimum
                throw new ArgumentException("AOK pepper must be at least 256 bits.");
        }
        catch (FormatException e)
        {
            throw new ArgumentException("AOK pepper must be Base64-encoded.", e);
        }
    }

    public async Task<Owner?> ResolveOwnerAsync(HttpContext http)
    {
        if (http is null) throw new ArgumentNullException(nameof(http));

        // 1) Strict cookie precedence if non-blank
        var cookieToken = http.Request.Cookies[CookieName];
        if (!string.IsNullOrWhiteSpace(cookieToken))
        {
            return await ResolveAndTouchAsync(cookieToken);
        }

        // 2) Fallback to header only when cookie is missing/blank
        string? headerToken = TryReadBearer(http) ?? TryReadXAok(http);
        if (string.IsNullOrWhiteSpace(headerToken))
            return null;

        return await ResolveAndTouchAsync(headerToken);
    }

    public void SetAokCookie(HttpResponse res, string rawToken)
    {
        if (res is null) throw new ArgumentNullException(nameof(res));
        if (rawToken is null) throw new ArgumentNullException(nameof(rawToken));

        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax, // PWA deep-link friendly
            Path = "/",                  // required for __Host-
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(365),
            Expires = DateTimeOffset.UtcNow.AddDays(365)
        };

        res.Cookies.Append(CookieName, rawToken, opts);
    }

    /* -------------------- helpers -------------------- */

    private async Task<Owner?> ResolveAndTouchAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = HashToken(token);

        // Fail-closed on revocation
        var owner = await _db.Owners
            .FirstOrDefaultAsync(o => o.KeyHash == hash && o.RevokedAt == null);

        if (owner is null) return null;

        owner.LastSeenAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return owner;
    }

    private static string? TryReadBearer(HttpContext http)
    {
        if (!http.Request.Headers.TryGetValue("Authorization", out var auth)) return null;
        var s = auth.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;

        const string prefix = "Bearer ";
        if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && s.Length > prefix.Length)
            return s.Substring(prefix.Length).Trim();

        return null;
    }

    private static string? TryReadXAok(HttpContext http)
    {
        // HeaderDictionary is case-insensitive
        return http.Request.Headers.TryGetValue("X-AOK", out var hv)
            ? hv.ToString()
            : null;
    }

    private string HashToken(string token)
    {
        // HMAC-SHA256 with server-side pepper
        var data = Encoding.UTF8.GetBytes(token);
        using var hmac = new HMACSHA256(_pepper);
        var mac = hmac.ComputeHash(data);
        return Base64Url(mac);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}