using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// The sweep of sad §6 flow 7: once at startup, then once a day, remove the session rows that can
/// no longer affect any decision.
/// </summary>
/// <remarks>
/// <para>
/// This is hygiene, never enforcement. A row that has expired but has not yet been swept is
/// already refused at recognition time, so the sweep is not what makes a session dead — which is
/// why a missed run costs nothing but table size, and why a failure is logged and left for the
/// next run rather than retried into the ground.
/// </para>
/// <para>
/// It issues no query of its own. Every deletion goes through
/// <see cref="ISessionStore.DeleteExpiredAsync"/>, because only Infrastructure may write SQL — and
/// a background service is the easiest place for that rule to be broken without anyone noticing.
/// </para>
/// </remarks>
public sealed class ExpiredSessionCleanupService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<ExpiredSessionCleanupService> logger) : BackgroundService
{
    /// <summary>Once a day. The startup run is the fallback for a restart, not the mechanism.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>
    /// How long without a successful sweep is worth someone's attention — sad §7's alert
    /// threshold, raised by <see cref="RaiseAlertIfStale"/> after every run.
    /// </summary>
    public static readonly TimeSpan HealthyInterval = TimeSpan.FromHours(48);

    /// <summary>
    /// flow 7's idempotency guard. The work has no key of its own, so two overlapping runs would
    /// simply issue the same delete against the same rows.
    /// </summary>
    private readonly SemaphoreSlim _guard = new(1, 1);

    /// <summary>How many rows the last successful run removed — the first §7 figure.</summary>
    public int LastRemovedCount { get; private set; }

    /// <summary>When a run last succeeded — the second §7 figure.</summary>
    public DateTimeOffset? LastSucceededAt { get; private set; }

    /// <summary>
    /// Whether the gap since the last success has grown past <see cref="HealthyInterval"/>. A
    /// process that has never yet swept counts as unhealthy, because "no evidence of success" and
    /// "evidence of failure" deserve the same attention here.
    /// </summary>
    public bool HasNotSucceededRecently =>
        LastSucceededAt is null || clock.UtcNow - LastSucceededAt.Value > HealthyInterval;

    /// <summary>
    /// Runs one sweep under the guard, reporting whether it actually ran and succeeded.
    /// </summary>
    /// <param name="sweep">
    /// The work, injected so a test can drive the guard and the failure path without needing to
    /// make a real database unavailable.
    /// </param>
    public async Task<bool> RunOnceAsync(
        Func<CancellationToken, Task<int>> sweep,
        CancellationToken cancellationToken)
    {
        if (!await _guard.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            // Skipped, not queued: a run that is still going will remove these rows anyway.
            logger.LogInformation(
                "module=accounts event=session_cleanup_skipped reason=already_running");

            return false;
        }

        try
        {
            var removed = await sweep(cancellationToken);

            LastRemovedCount = removed;
            LastSucceededAt = clock.UtcNow;

            logger.LogInformation(
                "module=accounts event=session_cleanup_succeeded removed={Removed}", removed);

            return true;
        }
        catch (Exception failure)
        {
            // Not retried here. Cleanup is hygiene, the application keeps serving, and the next
            // startup or the next day will carry it — an immediate retry against an unavailable
            // database would only turn one failure into many.
            logger.LogError(
                failure, "module=accounts event=session_cleanup_failed retry=next_scheduled_run");

            return false;
        }
        finally
        {
            // Released even on failure, so one bad run cannot lock the sweep out for good.
            _guard.Release();

            RaiseAlertIfStale();
        }
    }

    /// <summary>
    /// sad §6 flow 7: "No run has succeeded for more than 48 hours — raise the section 7 alert".
    /// There is no queue to replay and no retry to lean on; the operator is the escalation path,
    /// so this is an error-level line with a stable event name for a log search to key on.
    /// </summary>
    private void RaiseAlertIfStale()
    {
        if (HasNotSucceededRecently)
        {
            logger.LogError(
                "module=accounts event=session_cleanup_stale last_succeeded_at={LastSucceededAt} "
                + "threshold_hours={ThresholdHours}",
                LastSucceededAt?.ToString("O") ?? "never",
                HealthyInterval.TotalHours);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The startup run first: the common case is a redeploy, and waiting a day after each one
        // would mean the sweep effectively never ran on a frequently-deployed service.
        await SweepAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await SweepAsync(stoppingToken);
        }
    }

    private Task SweepAsync(CancellationToken cancellationToken) =>
        RunOnceAsync(
            async token =>
            {
                // A scope of its own: this service is a singleton and the store is scoped.
                using var scope = scopeFactory.CreateScope();

                return await scope.ServiceProvider
                    .GetRequiredService<ISessionStore>()
                    .DeleteExpiredAsync(token);
            },
            cancellationToken);

    public override void Dispose()
    {
        _guard.Dispose();
        base.Dispose();
    }
}
