namespace Api.Dtos;

public sealed record UpdateTotalsDto(decimal? SubTotal, decimal? Tax, decimal? Tip, decimal? Total);