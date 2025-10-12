using Api.Data;
using Api.Models.Owners;
using Api.Options;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
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

        // 0) Check for service-to-service token first (Azure Functions, internal services)
        var serviceToken = TryReadServiceToken(http);
        if (!string.IsNullOrWhiteSpace(serviceToken))
        {
            if (IsValidServiceToken(serviceToken))
            {
                // Service token is valid - get or create a dedicated service owner
                var serviceOwner = await GetOrCreateServiceOwnerAsync();
                return (serviceOwner, null); // No token rotation for service accounts
            }
            // Invalid service token - reject
            return (null, null);
        }

        // 1) Try to resolve from cookie
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
        // Always normalize UserAgent to prevent different values for same session
        // This ensures consistent UserAgent values across all owner records
        var normalizedUserAgent = string.IsNullOrWhiteSpace(userAgent) ? "unknown" : "browser";

        // Try to find existing owner first - use more flexible matching
        // Look for recent owners (within last 24 hours) with same IP OR UserAgent
        // This prevents duplicate owner creation when IP changes (dev/proxy) or UserAgent varies
        var recentCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        
        var existingOwner = await _db.Owners
            .Where(o => o.RevokedAt == null && o.CreatedAt >= recentCutoff)
            .Where(o => (o.LastSeenIp == clientIp) || (o.LastSeenUserAgent == normalizedUserAgent))
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (existingOwner != null)
        {
            // Found existing owner - return it with a new token
            var existingToken = GenerateToken(existingOwner.Id, _opts.TokenVersion);
            existingOwner.LastSeenAt = DateTimeOffset.UtcNow;
            existingOwner.LastSeenIp = clientIp; // Update IP in case it changed
            existingOwner.LastSeenUserAgent = normalizedUserAgent; // Update UserAgent
            await _db.SaveChangesAsync();
            return (existingOwner, existingToken);
        }

        // No existing owner - try to create one
        try
        {
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.NewGuid();
            var newToken = GenerateToken(ownerId, _opts.TokenVersion);
            var hash = HashToken(newToken);

            var owner = new Owner
            {
                Id = ownerId,
                KeyHash = hash,
                CreatedAt = now,
                LastSeenAt = now,
                IssuedAt = now,
                TokenVersion = _opts.TokenVersion,
                LastSeenIp = clientIp,
                LastSeenUserAgent = normalizedUserAgent,
                RevokedAt = null
            };

            _db.Owners.Add(owner);
            await _db.SaveChangesAsync();
            return (owner, newToken);
        }
        catch (DbUpdateException) when (IsUniqueConstraintViolation())
        {
            // Another request created the owner - find and return it using same flexible logic
            var competingOwner = await _db.Owners
                .Where(o => o.RevokedAt == null && o.CreatedAt >= recentCutoff)
                .Where(o => (o.LastSeenIp == clientIp) || (o.LastSeenUserAgent == normalizedUserAgent))
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (competingOwner != null)
            {
                var existingToken = GenerateToken(competingOwner.Id, _opts.TokenVersion);
                competingOwner.LastSeenAt = DateTimeOffset.UtcNow;
                competingOwner.LastSeenIp = clientIp; // Update IP in case it changed
                competingOwner.LastSeenUserAgent = normalizedUserAgent; // Update UserAgent
                await _db.SaveChangesAsync();
                return (competingOwner, existingToken);
            }

            // This shouldn't happen, but if it does, retry once
            return await CreateNewOwnerAsync(clientIp, userAgent);
        }
    }

    private bool IsUniqueConstraintViolation()
    {
        // Check if the exception is due to unique constraint violation
        // This is database-specific, but the pattern is consistent
        return true; // Simplified for now - in production you'd check the specific exception
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
        owner.LastSeenUserAgent = string.IsNullOrWhiteSpace(userAgent) ? "unknown" : "browser"; // Normalize UserAgent consistently
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
        // Note: Legacy method doesn't have access to userAgent parameter, so we can't normalize here
        // This is acceptable since this method is marked as legacy
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

    private static string? TryReadServiceToken(HttpContext http)
    {
        // Check for X-Service-Token header (for Azure Functions, internal services)
        return http.Request.Headers.TryGetValue("X-Service-Token", out var hv)
            ? hv.ToString()
            : null;
    }

    private bool IsValidServiceToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (string.IsNullOrWhiteSpace(_opts.ServiceToken)) return false;

        // Constant-time comparison to prevent timing attacks
        return CryptographicEquals(token, _opts.ServiceToken);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    private async Task<Owner> GetOrCreateServiceOwnerAsync()
    {
        // Use a well-known GUID for the service account (deterministic based on a namespace)
        // This ensures we always get the same service owner
        var serviceOwnerId = new Guid("00000000-0000-0000-0000-000000000001"); // Reserved service account ID

        var serviceOwner = await _db.Owners
            .FirstOrDefaultAsync(o => o.Id == serviceOwnerId);

        if (serviceOwner != null)
        {
            // Update last seen
            serviceOwner.LastSeenAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
            return serviceOwner;
        }

        // Create the service owner
        var now = DateTimeOffset.UtcNow;
        var newServiceOwner = new Owner
        {
            Id = serviceOwnerId,
            KeyHash = "service", // Not used for service accounts
            CreatedAt = now,
            LastSeenAt = now,
            IssuedAt = now,
            TokenVersion = _opts.TokenVersion,
            LastSeenIp = "service",
            LastSeenUserAgent = "azure-function",
            RevokedAt = null
        };

        _db.Owners.Add(newServiceOwner);
        
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race condition - another request created it
            var existing = await _db.Owners.FirstOrDefaultAsync(o => o.Id == serviceOwnerId);
            if (existing != null)
            {
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync();
                return existing;
            }
            throw; // Shouldn't happen
        }

        return newServiceOwner;
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
