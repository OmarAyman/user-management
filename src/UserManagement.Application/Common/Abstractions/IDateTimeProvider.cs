namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// The clock. Injected rather than called statically so lockout windows, token expiry and audit timestamps are
/// deterministic in tests instead of depending on wall time.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
