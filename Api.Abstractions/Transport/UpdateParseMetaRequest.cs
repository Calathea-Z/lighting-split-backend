using Api.Abstractions.Receipts;

namespace Api.Abstractions.Transport;

public sealed record UpdateParseMetaRequest(
    ParseEngine ParsedBy,
    bool LlmAttempted,
    bool? LlmAccepted = null,
    string? LlmModel = null,
    string? ParserVersion = null,
    string? RejectReason = null
);
