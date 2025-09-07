namespace Api.Abstractions.Transport;

public sealed record UpdateTotalsRequest(
    decimal? SubTotal,
    decimal? Tax,
    decimal? Tip,
    decimal? Total
);
