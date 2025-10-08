using Api.Data;
using Api.Dtos.Aok;
using Api.Infrastructure.Middleware;
using Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Controllers;

[ApiController]
[Route("api/aok")]
public sealed class AokController : ControllerBase
{
    private readonly LightningDbContext _db;
    private readonly AokSecurityOptions _opts;

    public AokController(LightningDbContext db, IOptions<AokSecurityOptions> opts)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
    }

    /// GET /api/aok/me - Returns current owner status and diagnostics
    [HttpGet("me")]
    public async Task<ActionResult<AokStatusDto>> GetStatus()
    {
        var owner = HttpContext.GetOwner();

        // Load payout methods count
        var payoutCount = await _db.OwnerPayoutMethods
            .Where(m => m.OwnerId == owner.Id)
            .CountAsync();

        // Calculate token age and days until expiration
        var tokenAge = DateTimeOffset.UtcNow - owner.IssuedAt;
        var daysUntilExpiration = Math.Max(0, _opts.MaxTokenAgeDays - (int)tokenAge.TotalDays);
        var willRotateSoon = daysUntilExpiration <= _opts.ProactiveRotationThresholdDays;

        var status = new AokStatusDto
        {
            OwnerId = owner.Id,
            CreatedAt = owner.CreatedAt,
            LastSeenAt = owner.LastSeenAt,
            TokenIssuedAt = owner.IssuedAt,
            TokenVersion = owner.TokenVersion,
            DaysUntilExpiration = daysUntilExpiration,
            WillRotateSoon = willRotateSoon,
            IsRevoked = owner.RevokedAt.HasValue,
            PayoutMethodCount = payoutCount,
            LastSeenIp = owner.LastSeenIp,
            LastSeenUserAgent = owner.LastSeenUserAgent
        };

        return Ok(status);
    }

    /// GET /api/aok/health - Simple health check endpoint
    [HttpGet("health")]
    public IActionResult Health()
    {
        var owner = HttpContext.TryGetOwner();

        return Ok(new
        {
            status = "healthy",
            hasOwner = owner is not null,
            timestamp = DateTimeOffset.UtcNow
        });
    }
}

