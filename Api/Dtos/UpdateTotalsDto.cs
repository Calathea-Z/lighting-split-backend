namespace Api.Dtos;

public record UpdateTotalsDto(
    decimal? SubTotal, 
    decimal? Tax, 
    decimal? Tip, 
    decimal? Total
);