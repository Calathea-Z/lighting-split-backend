namespace Api.Abstractions.Math;

public static class Money
{
    public static decimal Round2(decimal v) =>
        decimal.Round(v, 2, System.MidpointRounding.AwayFromZero);

    public static bool EqualsWithin(decimal a, decimal b, decimal tolerance = 0.02m) =>
        System.Math.Abs(a - b) <= tolerance;
}
