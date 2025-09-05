namespace Api.Common.Interfaces
{
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
