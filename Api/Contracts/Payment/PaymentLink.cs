namespace Api.Contracts.Payment
{
    public sealed record PaymentLink(
        Guid MethodId,
        string PlatformKey,
        string PlatformName,
        string Label,           // DisplayLabel or PlatformName
        string? Url,            // null for instructions-only (Zelle/Apple Cash)
        bool IsInstructionsOnly,
        string? Instructions    // e.g., email/phone or custom URL/notes
    );
}
