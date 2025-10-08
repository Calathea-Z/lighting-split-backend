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
    //  Cookie name varies by environment
    // __Host- prefix requires HTTPS (Secure; Path=/; NO Domain)
    // In dev we use a simpler name since we're on HTTP via Next.js proxy
    private const string CookieNameSecure = "__Host-aok";  // Production (HTTPS)
    private const string CookieNameDev = "aok";            // Development (HTTP)

    private readonly LightningDbContext _db;
    private readonly byte[] _pepper; // HMAC key (server-side secret)
    private readonly AokSecurityOptions _opts;
    private readonly IWebHostEnvironment _env;

    public AokService(LightningDbContext db, IOptions<AokSecurityOptions> opts, IWebHostEnvironment env)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        if (opts?.Value is null) throw new ArgumentNullException(nameof(opts));
        _opts = opts.Value;
        _env = env ?? throw new ArgumentNullException(nameof(env));

        if (string.IsNullOrWhiteSpace(_opts.PepperBase64))
            throw new ArgumentException("AOK pepper is not configured.", nameof(opts));

        try
        {
            _pepper = Convert.FromBase64String(_opts.PepperBase64);
            if (_pepper.Length < 32) // 256-bit minimum
                throw new ArgumentException("AOK pepper must be at least 256 bits.");
        }
        catch (FormatException e)
        {
            throw new ArgumentException("AOK pepper must be Base64-encoded.", e);
        }
    }

    private string GetCookieName() => _env.IsDevelopment() ? CookieNameDev : CookieNameSecure;

    public async Task<(Owner? owner, string? rawToken)> ResolveOrProvisionOwnerAsync(HttpContext http)
    {
        if (http is null) throw new ArgumentNullException(nameof(http));

        var clientIp = GetClientIp(http);
        var userAgent = GetUserAgent(http);

        // 1) Try to resolve from cookie first
        var cookieToken = http.Request.Cookies[GetCookieName()];
        if (!string.IsNullOrWhiteSpace(cookieToken))
        {
            var (owner, needsRotation) = await ResolveAndTouchAsync(cookieToken, clientIp, userAgent);
            if (owner is not null)
            {
                // Valid cookie - check if rotation is needed
                if (needsRotation)
                {
                    // Token is old version or expired - issue new one
                    var rotatedToken = GenerateToken(owner.Id, _opts.TokenVersion);
                    owner.TokenVersion = _opts.TokenVersion;
                    owner.IssuedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync();
                    return (owner, rotatedToken);
                }
                return (owner, null);
            }
            // Cookie exists but invalid/revoked - this is suspicious, return null for 401
            return (null, null);
        }

        // 2) Fallback to header (for mobile/non-browser clients)
        string? headerToken = TryReadBearer(http) ?? TryReadXAok(http);
        if (!string.IsNullOrWhiteSpace(headerToken))
        {
            var (owner, needsRotation) = await ResolveAndTouchAsync(headerToken, clientIp, userAgent);
            if (owner is not null)
            {
                if (needsRotation)
                {
                    var rotatedToken = GenerateToken(owner.Id, _opts.TokenVersion);
                    owner.TokenVersion = _opts.TokenVersion;
                    owner.IssuedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync();
                    return (owner, rotatedToken);
                }
                return (owner, null);
            }
            // Header token invalid - suspicious
            return (null, null);
        }

        // 3) No token at all - auto-provision a new Owner
        var (newOwner, newToken) = await CreateNewOwnerAsync(clientIp, userAgent);
        return (newOwner, newToken);
    }

    public async Task<Owner?> ResolveOwnerAsync(HttpContext http)
    {
        if (http is null) throw new ArgumentNullException(nameof(http));

        // 1) Strict cookie precedence if non-blank
        var cookieToken = http.Request.Cookies[GetCookieName()];
        if (!string.IsNullOrWhiteSpace(cookieToken))
        {
            return await ResolveAndTouchLegacyAsync(cookieToken);
        }

        // 2) Fallback to header only when cookie is missing/blank
        string? headerToken = TryReadBearer(http) ?? TryReadXAok(http);
        if (string.IsNullOrWhiteSpace(headerToken))
            return null;

        return await ResolveAndTouchLegacyAsync(headerToken);
    }

    public void SetAokCookie(HttpResponse res, string rawToken)
    {
        if (res is null) throw new ArgumentNullException(nameof(res));
        if (rawToken is null) throw new ArgumentNullException(nameof(rawToken));

        // Cookie settings vary by environment
        var cookieName = GetCookieName();
        var isProduction = !_env.IsDevelopment();

        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,        // HTTPS required in production, HTTP OK in dev
            SameSite = isProduction

                ? SameSiteMode.None       // Cross-origin with credentials in production
                : SameSiteMode.Lax,       // Same-origin via Next.js proxy in dev
            Path = "/",
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(365),
            Expires = DateTimeOffset.UtcNow.AddDays(365)
        };

        res.Cookies.Append(cookieName, rawToken, opts);
    }

    /* -------------------- helpers -------------------- */
    private string GenerateToken(Guid ownerId, byte version)
    {
        // Structured format: version.ownerId.issuedAt.signature
        // Example: 1.550e8400-e29b-41d4-a716-446655440000.1696800000.Axy...

        var now = DateTimeOffset.UtcNow;
        var issuedAtUnix = now.ToUnixTimeSeconds();

        // Payload: version|ownerId|issuedAt

        var payload = $"{version}.{ownerId:N}.{issuedAtUnix}";

        // Sign the payload

        var signature = SignPayload(payload);

        // Final token: payload.signature

        return $"{payload}.{signature}";
    }

    private bool TryParseToken(string token, out byte version, out Guid ownerId, out DateTimeOffset issuedAt)
    {
        version = 0;
        ownerId = Guid.Empty;
        issuedAt = default;

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 4) return false;

            // Parse version
            if (!byte.TryParse(parts[0], out version)) return false;

            // Parse ownerId (32 hex chars without hyphens)
            if (!Guid.TryParseExact(parts[1], "N", out ownerId)) return false;

            // Parse issuedAt
            if (!long.TryParse(parts[2], out var issuedAtUnix)) return false;
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnix);

            // Verify signature
            var payload = $"{parts[0]}.{parts[1]}.{parts[2]}";
            var expectedSignature = SignPayload(payload);


            if (parts[3] != expectedSignature) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private string SignPayload(string payload)
    {
        var data = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(_pepper);
        var mac = hmac.ComputeHash(data);
        return Base64Url(mac);
    }

    private async Task<(Owner owner, string rawToken)> CreateNewOwnerAsync(string? clientIp, string? userAgent)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.NewGuid();

        // Phase 2: Generate structured token with version

        var rawToken = GenerateToken(ownerId, _opts.TokenVersion);
        var hash = HashToken(rawToken);

        var owner = new Owner
        {
            Id = ownerId,
            KeyHash = hash,
            CreatedAt = now,
            LastSeenAt = now,
            IssuedAt = now,
            TokenVersion = _opts.TokenVersion,
            LastSeenIp = clientIp,
            LastSeenUserAgent = userAgent,
            RevokedAt = null
        };

        _db.Owners.Add(owner);
        await _db.SaveChangesAsync();

        return (owner, rawToken);
    }

    private async Task<(Owner? owner, bool needsRotation)> ResolveAndTouchAsync(string token, string? clientIp, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(token)) return (null, false);

        // Phase 2: Parse and validate structured token
        if (!TryParseToken(token, out var tokenVersion, out var ownerId, out var issuedAt))
            return (null, false);

        var hash = HashToken(token);

        // Fail-closed on revocation
        var owner = await _db.Owners
            .FirstOrDefaultAsync(o => o.KeyHash == hash && o.RevokedAt == null);

        if (owner is null) return (null, false);

        // Phase 2: Replay protection - check token age
        var tokenAge = DateTimeOffset.UtcNow - issuedAt;
        if (tokenAge.TotalDays > _opts.MaxTokenAgeDays)
        {
            // Token too old - reject (replay protection)
            return (null, false);
        }

        // Check if rotation is needed (version mismatch)
        var needsRotation = false;
        if (tokenVersion < _opts.TokenVersion)
        {
            // Old version - check grace period
            var ownerAge = DateTimeOffset.UtcNow - owner.IssuedAt;
            if (ownerAge.TotalDays > _opts.VersionGracePeriodDays)
            {
                // Outside grace period - reject
                return (null, false);
            }
            // Within grace period - allow but flag for rotation
            needsRotation = true;
        }

        // Proactive rotation if token is nearing expiration
        var daysUntilExpiration = _opts.MaxTokenAgeDays - tokenAge.TotalDays;
        if (daysUntilExpiration <= _opts.ProactiveRotationThresholdDays)
        {
            // Token will expire soon - rotate proactively
            needsRotation = true;
        }

        // Update audit fields
        owner.LastSeenAt = DateTimeOffset.UtcNow;
        owner.LastSeenIp = clientIp;
        owner.LastSeenUserAgent = userAgent;
        await _db.SaveChangesAsync();


        return (owner, needsRotation);
    }

    // Legacy method for backwards compatibility (no audit/rotation)
    private async Task<Owner?> ResolveAndTouchLegacyAsync(string token)
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

    private static string? GetClientIp(HttpContext http)
    {
        // Try X-Forwarded-For first (proxy/load balancer)
        var forwardedFor = http.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            // Take first IP if multiple (client -> proxy chain)
            var ip = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(ip)) return ip;
        }

        // Fallback to direct connection
        return http.Connection.RemoteIpAddress?.ToString();
    }

    private static string? GetUserAgent(HttpContext http)
    {
        return http.Request.Headers.UserAgent.FirstOrDefault();
    }

    /* -------------------- Legacy Helpers -------------------- */

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
