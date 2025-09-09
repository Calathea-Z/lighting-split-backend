namespace Api.Dtos.Splits.Responses
{
    public sealed record FinalizeSplitResponse(
        Guid SplitId,
        string ShareCode,
        string ShareUrl,
        IReadOnlyList<FinalizeParticipantDto> Participants);
}
