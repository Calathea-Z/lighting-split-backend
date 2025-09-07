namespace Api.Abstractions.Parsing;

public sealed record NormalizerHints(
    string Currency,
    decimal? CandidateSubtotal,
    decimal? CandidateTax,
    decimal? CandidateTip,
    decimal? CandidateTotal,
    string? MerchantName,
    string? DatetimeIso,
    string[]? IgnorePhrases = null
);
