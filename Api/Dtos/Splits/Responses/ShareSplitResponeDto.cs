namespace Api.Dtos.Splits.Responses
{
    public sealed record ShareSplitResponseDto(
        Guid SplitId,
        string Code,
        IReadOnlyList<ShareParticipantDto> Participants
    );
}
