namespace UserManagement.Api.Configuration;

/// <summary>
/// Retries a startup step that can fail because a dependency is not ready yet.
/// </summary>
/// <remarks>
/// <para>
/// Written for the database migration, and for one failure in particular. A SQL Server container answers
/// <c>SELECT 1</c> - and therefore passes its health check - before it has finished bringing user databases
/// online after a restart. EF Core asks whether the database exists, is told it does not, issues
/// <c>CREATE DATABASE</c>, and gets <b>error 1801: database already exists</b>. The process then dies on
/// startup, so the second <c>docker compose up</c> against an existing volume fails where the first succeeded.
/// </para>
/// <para>
/// Retrying is the right answer rather than a stronger health check. A health check can only ever say the
/// engine was ready a moment ago, and the same race exists for a managed database that fails over while an
/// instance is starting. The step is idempotent - migrations and the seeder both are - so attempting it again
/// costs a delay and nothing else.
/// </para>
/// <para>
/// It is deliberately not a general-purpose resilience policy: no jitter, no circuit breaker, no exception
/// filtering. Startup either succeeds within a bounded number of attempts or the process exits, which is what
/// an orchestrator wants.
/// </para>
/// </remarks>
public static class StartupRetry
{
    public static async Task RunAsync(
        Func<Task> step,
        string description,
        ILogger logger,
        int attempts = 5,
        TimeSpan? delay = null,
        Func<TimeSpan, Task>? wait = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        var pause = delay ?? TimeSpan.FromSeconds(3);
        var sleep = wait ?? Task.Delay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await step();

                if (attempt > 1)
                {
                    logger.LogInformation("{Description} succeeded on attempt {Attempt}", description, attempt);
                }

                return;
            }
            catch (Exception exception) when (attempt < attempts)
            {
                // Warning, not error: this is expected while a dependency finishes starting, and an error here
                // would page someone for a container that goes on to work.
                logger.LogWarning(
                    exception,
                    "{Description} failed on attempt {Attempt} of {Attempts}; retrying in {Delay}",
                    description,
                    attempt,
                    attempts,
                    pause);

                await sleep(pause);
            }
        }
    }
}
