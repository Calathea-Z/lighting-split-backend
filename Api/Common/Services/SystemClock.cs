using Api.Common.Interfaces;

namespace Api.Common.Services
{
    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
