using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T20 — the cleanup sweep. It is hygiene and never enforcement: a row that is expired but not yet
/// swept is already refused at recognition time, so the worst a missed run costs is table size.
/// The tests are as much about that restraint as about the deletion — a failing sweep must not
/// take the application down with it, and a revoked row must survive long enough for AC-10 to
/// still be answered by a record.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExpiredSessionCleanupTests(ApiFactory factory)
{
    // ---- What goes, and what emphatically stays ------------------------------------------------

    [Fact]
    public async Task The_sweep_removes_what_can_no_longer_matter_and_keeps_what_can()
    {
        factory.Clock.Reset();
        var accountId = await AnAccountAsync();

        var live = await OpenAsync(accountId);
        var ancient = await OpenAsync(accountId);
        var longRevoked = await OpenAsync(accountId);
        var recentlyRevoked = await OpenAsync(accountId);

        await RevokeAsync(longRevoked);
        await RevokeAsync(recentlyRevoked);

        await AgeAsync(ancient, openedDaysAgo: 91);
        await AgeRevocationAsync(longRevoked, revokedDaysAgo: 15);
        await AgeRevocationAsync(recentlyRevoked, revokedDaysAgo: 13);

        var removed = await RunSweepAsync();

        Assert.True(removed >= 2, $"the sweep removed {removed} rows");
        Assert.Equal(1, await RowsAsync(live));
        Assert.Equal(0, await RowsAsync(ancient));
        Assert.Equal(0, await RowsAsync(longRevoked));

        // AC-10's refusal is answered by this record until the retention has passed. Removing it
        // at 13 days would turn a revoked session into an unknown one a day early.
        Assert.Equal(1, await RowsAsync(recentlyRevoked));
    }

    [Fact]
    public async Task A_run_that_finds_nothing_is_a_success()
    {
        factory.Clock.Set(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        try
        {
            Assert.Equal(0, await RunSweepAsync());
            Assert.NotNull(Cleanup().LastSucceededAt);
        }
        finally
        {
            factory.Clock.Reset();
        }
    }

    [Fact]
    public async Task Repeated_runs_are_harmless_because_the_sweep_is_idempotent()
    {
        factory.Clock.Reset();
        var accountId = await AnAccountAsync();
        var ancient = await OpenAsync(accountId);
        await AgeAsync(ancient, openedDaysAgo: 91);

        Assert.True(await RunSweepAsync() >= 1);
        Assert.Equal(0, await RunSweepAsync());
        Assert.Equal(0, await RowsAsync(ancient));
    }

    // ---- The two monitoring figures sad §7 asks for ---------------------------------------------

    [Fact]
    public async Task Each_run_records_how_much_it_removed_and_when_it_last_succeeded()
    {
        factory.Clock.Reset();
        var accountId = await AnAccountAsync();
        var ancient = await OpenAsync(accountId);
        await AgeAsync(ancient, openedDaysAgo: 91);

        var cleanup = Cleanup();
        await RunSweepAsync();

        Assert.True(cleanup.LastRemovedCount >= 1);
        Assert.Equal(factory.Clock.UtcNow, cleanup.LastSucceededAt);
    }

    [Fact]
    public async Task A_long_gap_since_the_last_success_is_derivable()
    {
        // The §7 figure itself, exposed for anything that wants to read it (a health check, a
        // dashboard). The alert below is raised from the same property.
        factory.Clock.Reset();
        var cleanup = Cleanup();
        await RunSweepAsync();

        Assert.False(cleanup.HasNotSucceededRecently);

        factory.Clock.Advance(TimeSpan.FromHours(49));
        Assert.True(cleanup.HasNotSucceededRecently);

        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_run_that_leaves_the_sweep_stale_raises_the_section_7_alert()
    {
        // Review 2026-09-22 R-26. sad §6 flow 7: "No run has succeeded for more than 48 hours →
        // raise the section 7 alert". The operator is the escalation path, so the alert is an
        // error-level line an operator's log search can key on.
        factory.Clock.Reset();
        var logger = new RecordingLogger<ExpiredSessionCleanupService>();
        using var cleanup = new ExpiredSessionCleanupService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Clock, logger);

        Assert.True(await cleanup.RunOnceAsync(_ => Task.FromResult(0), CancellationToken.None));
        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("event=session_cleanup_stale"));

        factory.Clock.Advance(TimeSpan.FromHours(49));
        Assert.False(await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("the database is unavailable"),
            CancellationToken.None));

        var alert = Assert.Single(
            logger.Entries, entry => entry.Message.Contains("event=session_cleanup_stale"));
        Assert.Equal(LogLevel.Error, alert.Level);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_first_sweep_that_fails_at_construction_time_raises_no_alert()
    {
        // Review 2026-09-22 (re-review) N-08. "Never succeeded" must not by itself count as stale:
        // a process that has just started and whose first sweep fails (for example because the
        // database is not ready yet after a redeploy) has not yet had 48 hours to prove itself.
        factory.Clock.Reset();
        var logger = new RecordingLogger<ExpiredSessionCleanupService>();
        using var cleanup = new ExpiredSessionCleanupService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Clock, logger);

        Assert.False(await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("the database is not ready yet"),
            CancellationToken.None));

        Assert.DoesNotContain(
            logger.Entries, entry => entry.Message.Contains("event=session_cleanup_stale"));

        // Once the process itself is old enough without ever having succeeded, the alert does
        // fire — "no evidence of success" past the threshold still deserves attention.
        factory.Clock.Advance(TimeSpan.FromHours(49));
        Assert.False(await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("still unavailable"),
            CancellationToken.None));

        var alert = Assert.Single(
            logger.Entries, entry => entry.Message.Contains("event=session_cleanup_stale"));
        Assert.Equal(LogLevel.Error, alert.Level);

        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_failure_past_a_short_grace_period_with_no_success_ever_raises_the_alert()
    {
        // Review 2026-09-23 (second re-review) Q-09. T41 measured staleness against the full
        // 48-hour HealthyInterval even before any sweep had ever succeeded, so an instance that is
        // restarted more often than every 48 hours never lives long enough to look "48 hours
        // overdue" and never raises session_cleanup_stale, no matter how many sweeps fail. Before
        // the first success, staleness must instead be measured against a short grace period after
        // process start (for example one hour) — a first failure at t=0 still raises nothing, but a
        // failure once that short grace period has passed, with no success ever recorded, must.
        factory.Clock.Reset();
        var logger = new RecordingLogger<ExpiredSessionCleanupService>();
        using var cleanup = new ExpiredSessionCleanupService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Clock, logger);

        // t=0: a first failure raises nothing — the process has not had even the grace period to
        // prove itself yet.
        Assert.False(await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("the database is not ready yet"),
            CancellationToken.None));
        Assert.DoesNotContain(
            logger.Entries, entry => entry.Message.Contains("event=session_cleanup_stale"));

        // Past a short grace period (90 minutes), but nowhere near the 48-hour HealthyInterval, a
        // failure with no success ever recorded must raise the alert.
        factory.Clock.Advance(TimeSpan.FromMinutes(90));
        Assert.False(await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("still unavailable"),
            CancellationToken.None));

        var alert = Assert.Single(
            logger.Entries, entry => entry.Message.Contains("event=session_cleanup_stale"));
        Assert.Equal(LogLevel.Error, alert.Level);

        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_failure_47_hours_after_a_success_raises_no_alert()
    {
        // Review 2026-09-23 (second re-review) Q-09 regression: once a sweep has succeeded, the
        // grace period no longer applies — the full 48-hour HealthyInterval measured from
        // LastSucceededAt governs, unchanged.
        factory.Clock.Reset();
        var logger = new RecordingLogger<ExpiredSessionCleanupService>();
        using var cleanup = new ExpiredSessionCleanupService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Clock, logger);

        Assert.True(await cleanup.RunOnceAsync(_ => Task.FromResult(0), CancellationToken.None));

        factory.Clock.Advance(TimeSpan.FromHours(47));
        Assert.False(await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("the database is unavailable"),
            CancellationToken.None));

        Assert.DoesNotContain(
            logger.Entries, entry => entry.Message.Contains("event=session_cleanup_stale"));

        factory.Clock.Reset();
    }

    // ---- The guard and the failure behaviour -----------------------------------------------------

    [Fact]
    public async Task A_second_run_is_skipped_while_the_first_is_still_going()
    {
        // flow 7's idempotency guard. The work has no key of its own, so overlapping runs would
        // simply issue the same delete twice against the same rows.
        factory.Clock.Reset();
        var cleanup = Cleanup();

        var blocked = new TaskCompletionSource();
        var first = cleanup.RunOnceAsync(
            async token =>
            {
                await blocked.Task;
                return 0;
            },
            CancellationToken.None);

        var second = await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("the guard let a second run through"),
            CancellationToken.None);

        Assert.False(second, "a second run started while the first was still going");

        blocked.SetResult();
        Assert.True(await first);
    }

    [Fact]
    public async Task A_failing_sweep_neither_stops_the_host_nor_blocks_the_next_run()
    {
        factory.Clock.Reset();
        var cleanup = Cleanup();

        var failed = await cleanup.RunOnceAsync(
            _ => throw new InvalidOperationException("the database is unavailable"),
            CancellationToken.None);

        Assert.False(failed);

        // The application is still serving — proven by asking it something, not by assuming.
        Assert.Equal(
            System.Net.HttpStatusCode.OK,
            (await factory.CreateClient().GetAsync("/health")).StatusCode);

        // And the guard released, so the next run is not locked out by the failure.
        Assert.True(await RunSweepAsync() >= 0);
    }

    [Fact]
    public void The_service_issues_no_query_of_its_own()
    {
        // Only Infrastructure may write SQL. A deletion assembled here would be that rule broken
        // in the one place it is least likely to be noticed.
        var source = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "src", "Uniqua.Projector.Api", "Accounts",
            "ExpiredSessionCleanupService.cs"));

        // The port's name legitimately contains "Delete", so what is looked for is SQL and the
        // things that issue it — not the word.
        foreach (var forbidden in new[]
        {
            "DELETE FROM", "FromSql", "ExecuteSqlRaw", "ExecuteDeleteAsync", "AppDbContext",
            "DbContext", "SqlConnection",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(nameof(ISessionStore.DeleteExpiredAsync), source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sweep_runs_at_startup_and_then_once_a_day()
    {
        Assert.Equal(TimeSpan.FromDays(1), ExpiredSessionCleanupService.Interval);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Uniqua.Projector.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private ExpiredSessionCleanupService Cleanup() =>
        factory.Services.GetRequiredService<ExpiredSessionCleanupService>();

    private async Task<int> RunSweepAsync()
    {
        var removed = 0;
        var succeeded = await Cleanup().RunOnceAsync(
            async token =>
            {
                using var scope = factory.Services.CreateScope();
                removed = await scope.ServiceProvider
                    .GetRequiredService<ISessionStore>()
                    .DeleteExpiredAsync(token);

                return removed;
            },
            CancellationToken.None);

        Assert.True(succeeded);
        return removed;
    }

    private async Task<Guid> AnAccountAsync()
    {
        var accountId = Guid.CreateVersion7();
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"sweep-{Guid.NewGuid():N}"));

        return accountId;
    }

    private async Task<Guid> OpenAsync(Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider
            .GetRequiredService<ISessionStore>()
            .OpenAsync(accountId, CancellationToken.None);

        return session.Id;
    }

    private async Task RevokeAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<ISessionStore>()
            .RevokeAsync(sessionId, CancellationToken.None);
    }

    private Task AgeAsync(Guid sessionId, int openedDaysAgo) =>
        factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Sessions]
            SET [CreatedAt] = DATEADD(day, -{openedDaysAgo}, [CreatedAt]),
                [LastSeenAt] = DATEADD(day, -{openedDaysAgo}, [LastSeenAt])
            WHERE [Id] = '{sessionId}';
            """);

    private Task AgeRevocationAsync(Guid sessionId, int revokedDaysAgo) =>
        factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Sessions]
            SET [RevokedAt] = DATEADD(day, -{revokedDaysAgo}, [RevokedAt])
            WHERE [Id] = '{sessionId}';
            """);

    private Task<int> RowsAsync(Guid sessionId) =>
        factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [Id] = '{sessionId}'");
}
