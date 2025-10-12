using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Services.Payments.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Api.Services.Payments;

public sealed class ParticipantTokenService : IParticipantTokenService
{
    private readonly IDataProtector _protector;

    public ParticipantTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("ParticipantTokens");
    }

    public string GenerateToken(Guid splitId, Guid participantId)
    {
        var payload = new { splitId, participantId, timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json);
    }

    public (Guid splitId, Guid participantId)? ValidateToken(string token)
    {
        try
        {
            var json = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<JsonElement>(json);

            var splitId = payload.GetProperty("splitId").GetGuid();
            var participantId = payload.GetProperty("participantId").GetGuid();
            var timestamp = payload.GetProperty("timestamp").GetInt64();

            // Check if token is not too old (e.g., 30 days)
            var tokenTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            if (DateTimeOffset.UtcNow - tokenTime > TimeSpan.FromDays(30))
            {
                return null;
            }

            return (splitId, participantId);
        }
        catch
        {
            return null;
        }
    }
}
