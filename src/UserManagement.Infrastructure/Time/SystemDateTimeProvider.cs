using UserManagement.Application.Common.Abstractions;

namespace UserManagement.Infrastructure.Time;

/// <summary>The real clock, always UTC. Registered as a singleton; it holds no state.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
