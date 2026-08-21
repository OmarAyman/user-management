using Microsoft.Extensions.Logging;
using UserManagement.Api.Configuration;
using UserManagement.UnitTests.TestSupport;

namespace UserManagement.UnitTests.Configuration;

/// <summary>
/// The startup retry, which exists because of one specific failure.
/// </summary>
/// <remarks>
/// A SQL Server container answers <c>SELECT 1</c> before its user databases are online, so EF asks whether the
/// database exists, is told it does not, issues <c>CREATE DATABASE</c> and gets error 1801. Before this, the
/// process died - the second <c>docker compose up</c> against an existing volume failed where the first
/// succeeded. These tests pin the behaviour rather than the incident: succeed late, give up eventually, and say
/// so at a level that does not page anyone for a container that goes on to work.
/// </remarks>
public sealed class StartupRetryTests
{
    private readonly CapturingLogger<StartupRetryTests> _logger = new();

    /// <summary>No real waiting: the delay is injected so the test does not spend fifteen seconds proving it.</summary>
    private readonly List<TimeSpan> _waits = [];

    private Task RecordWait(TimeSpan delay)
    {
        _waits.Add(delay);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_step_that_succeeds_first_time_runs_once_and_says_nothing()
    {
        var calls = 0;

        await StartupRetry.RunAsync(
            () => { calls++; return Task.CompletedTask; },
            "Migration",
            _logger,
            wait: RecordWait);

        Assert.Equal(1, calls);
        Assert.Empty(_waits);

        // A quiet success is the normal case; logging it would only add noise to every start.
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task A_step_that_fails_then_succeeds_is_retried_and_reported()
    {
        var calls = 0;

        await StartupRetry.RunAsync(
            () =>
            {
                calls++;

                return calls < 3
                    ? Task.FromException(new InvalidOperationException("Database 'UserManagement' already exists."))
                    : Task.CompletedTask;
            },
            "Migration",
            _logger,
            delay: TimeSpan.FromMilliseconds(1),
            wait: RecordWait);

        Assert.Equal(3, calls);
        Assert.Equal(2, _waits.Count);

        // Warning, not Error: the dependency was still starting, which is expected rather than wrong.
        Assert.Equal(2, _logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
        Assert.Contains("succeeded on attempt", _logger.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_that_never_succeeds_gives_up_and_rethrows()
    {
        var calls = 0;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => StartupRetry.RunAsync(
            () =>
            {
                calls++;
                return Task.FromException(new InvalidOperationException("still not ready"));
            },
            "Migration",
            _logger,
            attempts: 3,
            wait: RecordWait));

        // Exactly the configured number of attempts, and then the process is allowed to die: an orchestrator
        // restarting a container is a better answer than an API serving requests against no schema.
        Assert.Equal(3, calls);
        Assert.Equal("still not ready", failure.Message);
        Assert.Equal(2, _waits.Count);
    }

    [Fact]
    public async Task The_configured_delay_is_used_between_attempts()
    {
        await StartupRetry.RunAsync(
            () => _waits.Count == 0 ? Task.FromException(new TimeoutException()) : Task.CompletedTask,
            "Migration",
            _logger,
            delay: TimeSpan.FromSeconds(7),
            wait: RecordWait);

        Assert.Equal([TimeSpan.FromSeconds(7)], _waits);
    }

    [Fact]
    public async Task Fewer_than_one_attempt_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => StartupRetry.RunAsync(
            () => Task.CompletedTask,
            "Migration",
            _logger,
            attempts: 0,
            wait: RecordWait));
    }
}
