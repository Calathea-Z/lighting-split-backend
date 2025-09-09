namespace Api.Dtos.Splits.Responses
{
    public sealed record SplitPreviewDto(
        Guid SplitId,
        decimal ReceiptSubtotal,
        decimal ReceiptTax,
        decimal ReceiptTip,
        decimal ReceiptTotal,
        IReadOnlyList<SplitParticipantTotalDto> Participants,
        IReadOnlyList<SplitUnclaimedItemDto> UnclaimedItems,
        decimal RoundingRemainder // total rounding adjustment applied (signed)
    );
}
