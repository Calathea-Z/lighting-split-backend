namespace Api.Services.Payments.Abstractions;

public interface IParticipantTokenService
{
    /// <summary>
    /// Generates a signed token for a participant to self-report payment
    /// </summary>
    /// <param name="splitId">ID of the split</param>
    /// <param name="participantId">ID of the participant</param>
    /// <returns>Signed token that can be used to self-report payment</returns>
    string GenerateToken(Guid splitId, Guid participantId);

    /// <summary>
    /// Validates a signed token and extracts participant information
    /// </summary>
    /// <param name="token">Signed token to validate</param>
    /// <returns>Tuple containing splitId and participantId if valid, null if invalid</returns>
    (Guid splitId, Guid participantId)? ValidateToken(string token);
}
