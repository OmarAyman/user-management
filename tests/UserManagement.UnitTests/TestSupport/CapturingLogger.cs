using Microsoft.Extensions.Logging;

namespace UserManagement.UnitTests.TestSupport;

/// <summary>
/// A logger that keeps everything it was given, so a test can assert on what would reach a log sink.
/// </summary>
/// <remarks>
/// Captures the rendered message <b>and</b> every structured value separately. That matters: a credential can
/// leak through a template parameter that never appears in the rendered text of a console line but does end up
/// in a JSON sink.
/// </remarks>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<CapturedEntry> _entries = [];

    public IReadOnlyList<CapturedEntry> Entries => _entries;

    /// <summary>Everything written, rendered messages and structured values alike, as one searchable string.</summary>
    public string AllText =>
        string.Join(
            '\n',
            _entries.Select(entry => $"{entry.Message}|{string.Join('|', entry.Values.Select(value => value.Value))}"));

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var values = state is IEnumerable<KeyValuePair<string, object?>> structured
            ? structured.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value?.ToString())).ToList()
            : [];

        _entries.Add(new CapturedEntry(logLevel, formatter(state, exception), values, exception));
    }

    public sealed record CapturedEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, string?>> Values,
        Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

