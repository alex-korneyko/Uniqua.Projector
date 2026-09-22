using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T6 — the session ports over the real store. Three things are proven that only a real database
/// can show: that recognising a session is one primary-key lookup and nothing more (the shape
/// behind spec §6's 30 ms budget), that an ordinary read usually costs no write at all, and that
/// ending one session leaves the same account's other sessions alone (AC-08).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SessionStoreTests(ApiFactory factory)
{
    // ---- Opening -------------------------------------------------------------------------------

    [Fact]
    public async Task Opening_a_session_stores_it_live_against_its_account()
    {
        factory.Clock.Reset();
        var accountId = await AnAccountAsync();
        using var scope = factory.Services.CreateScope();

        var session = await Store(scope).OpenAsync(accountId, CancellationToken.None);

        Assert.Equal(accountId, session.AccountId);
        Assert.Equal(factory.Clock.UtcNow, session.CreatedAt);
        Assert.Equal(factory.Clock.UtcNow, session.LastSeenAt);
        Assert.False(session.IsRevoked);
        Assert.Equal(7, session.Id.Version);

        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [Id] = '{session.Id}' AND [RevokedAt] IS NULL"));
    }

    [Fact]
    public async Task An_opened_session_takes_its_instants_from_the_clock_port_not_the_system_clock()
    {
        // If any of this reached for DateTimeOffset.UtcNow the stored instant would be today's,
        // not the one the test chose.
        var chosen = new DateTimeOffset(2031, 3, 4, 5, 6, 7, TimeSpan.Zero);
        factory.Clock.Set(chosen);
        try
        {
            var accountId = await AnAccountAsync();
            using var scope = factory.Services.CreateScope();

            var session = await Store(scope).OpenAsync(accountId, CancellationToken.None);

            Assert.Equal(chosen, session.CreatedAt);
        }
        finally
        {
            factory.Clock.Reset();
        }
    }

    // ---- Finding: the one query the 30 ms budget is about --------------------------------------

    [Fact]
    public async Task Finding_a_session_returns_it()
    {
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        using var scope = factory.Services.CreateScope();

        var found = await Reader(scope).FindAsync(session.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(session.Id, found.Id);
        Assert.Equal(session.AccountId, found.AccountId);
    }

    [Fact]
    public async Task An_id_that_was_never_issued_is_simply_not_found()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Null(await Reader(scope).FindAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task A_revoked_session_is_still_returned_so_the_refusal_is_a_record()
    {
        // AC-10 refuses "regardless of what their browser still holds". That refusal has to come
        // from a row that says revoked, not from the row being gone — otherwise a revoked session
        // and a forged id would be indistinguishable, and the sweep deleting rows 14 days later
        // would silently change the meaning of a request.
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        using (var scope = factory.Services.CreateScope())
        {
            await Store(scope).RevokeAsync(session.Id, CancellationToken.None);
        }

        using var reading = factory.Services.CreateScope();
        var found = await Reader(reading).FindAsync(session.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.True(found.IsRevoked);
        Assert.Equal(factory.Clock.UtcNow, found.RevokedAt);
    }

    [Fact]
    public async Task Recognition_is_one_statement_against_sessions_alone()
    {
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        using var scope = factory.Services.CreateScope();
        factory.Commands.Clear();

        await Reader(scope).FindAsync(session.Id, CancellationToken.None);

        var statements = factory.Commands.Statements;
        Assert.Single(statements);

        var sql = statements.Single();
        Assert.Contains("[Sessions]", sql, StringComparison.Ordinal);
        // No account is materialised: the recognition path must not touch AspNetUsers at all.
        Assert.DoesNotContain("AspNetUsers", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ---- The activity stamp, at most once an hour ----------------------------------------------

    [Fact]
    public async Task An_ordinary_read_inside_the_hour_costs_no_write_at_all()
    {
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        factory.Clock.Advance(TimeSpan.FromMinutes(59));

        using var scope = factory.Services.CreateScope();
        factory.Commands.Clear();
        var stamped = await Store(scope).StampActivityAsync(session, CancellationToken.None);

        Assert.False(stamped);
        Assert.DoesNotContain(
            factory.Commands.Statements,
            sql => sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_read_after_the_hour_moves_the_stamp_once()
    {
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        var anHourOn = factory.Clock.Advance(TimeSpan.FromMinutes(61));

        using var scope = factory.Services.CreateScope();
        Assert.True(await Store(scope).StampActivityAsync(session, CancellationToken.None));

        using var reading = factory.Services.CreateScope();
        var reread = await Reader(reading).FindAsync(session.Id, CancellationToken.None);
        Assert.NotNull(reread);
        Assert.Equal(anHourOn, reread.LastSeenAt);

        // And the session's life is extended either way, because the idle rule counts from here.
        Assert.False(reread.IsExpired(anHourOn));
    }

    [Fact]
    public async Task Stamping_a_session_whose_row_has_gone_reports_that_nothing_was_written()
    {
        // The sweep can remove a row between a request being recognised and the stamp being
        // written. That is not an error — there is simply nothing left to stamp.
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        await factory.ExecuteAsync($"DELETE FROM [dbo].[Sessions] WHERE [Id] = '{session.Id}';");

        factory.Clock.Advance(TimeSpan.FromHours(2));
        using var scope = factory.Services.CreateScope();

        Assert.False(await Store(scope).StampActivityAsync(session, CancellationToken.None));
        factory.Clock.Reset();
    }

    // ---- Revoking ------------------------------------------------------------------------------

    [Fact]
    public async Task Ending_one_session_leaves_the_accounts_other_sessions_live()
    {
        // AC-08: signing out on the phone must not sign the laptop out.
        factory.Clock.Reset();
        var accountId = await AnAccountAsync();

        Session phone;
        Session laptop;
        using (var scope = factory.Services.CreateScope())
        {
            phone = await Store(scope).OpenAsync(accountId, CancellationToken.None);
            laptop = await Store(scope).OpenAsync(accountId, CancellationToken.None);
            await Store(scope).RevokeAsync(phone.Id, CancellationToken.None);
        }

        using var reading = factory.Services.CreateScope();
        var reader = Reader(reading);

        Assert.True((await reader.FindAsync(phone.Id, CancellationToken.None))!.IsRevoked);
        Assert.False((await reader.FindAsync(laptop.Id, CancellationToken.None))!.IsRevoked);
    }

    [Fact]
    public async Task Revoking_twice_keeps_the_instant_the_session_actually_ended()
    {
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();

        using (var scope = factory.Services.CreateScope())
        {
            await Store(scope).RevokeAsync(session.Id, CancellationToken.None);
        }

        var firstEnding = factory.Clock.UtcNow;
        factory.Clock.Advance(TimeSpan.FromHours(3));

        using (var scope = factory.Services.CreateScope())
        {
            await Store(scope).RevokeAsync(session.Id, CancellationToken.None);
        }

        using var reading = factory.Services.CreateScope();
        var reread = await Reader(reading).FindAsync(session.Id, CancellationToken.None);

        Assert.Equal(firstEnding, reread!.RevokedAt);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task Revoking_a_session_that_does_not_exist_is_not_an_error()
    {
        using var scope = factory.Services.CreateScope();

        await Store(scope).RevokeAsync(Guid.CreateVersion7(), CancellationToken.None);
    }

    // ---- The sweep -----------------------------------------------------------------------------

    [Fact]
    public async Task The_sweep_removes_only_what_can_no_longer_matter()
    {
        factory.Clock.Reset();
        var accountId = await AnAccountAsync();
        var opened = factory.Clock.UtcNow;

        Session live;
        Session ancient;
        Session longRevoked;
        Session justRevoked;
        using (var scope = factory.Services.CreateScope())
        {
            var store = Store(scope);
            live = await store.OpenAsync(accountId, CancellationToken.None);
            ancient = await store.OpenAsync(accountId, CancellationToken.None);
            longRevoked = await store.OpenAsync(accountId, CancellationToken.None);
            justRevoked = await store.OpenAsync(accountId, CancellationToken.None);

            await store.RevokeAsync(longRevoked.Id, CancellationToken.None);
        }

        // Age the two that should go: one opened past the 90-day ceiling, one revoked past the
        // 14 days the record is kept for.
        await factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Sessions]
            SET [CreatedAt] = DATEADD(day, -91, [CreatedAt]), [LastSeenAt] = DATEADD(day, -91, [LastSeenAt])
            WHERE [Id] = '{ancient.Id}';
            UPDATE [dbo].[Sessions]
            SET [RevokedAt] = DATEADD(day, -15, [RevokedAt])
            WHERE [Id] = '{longRevoked.Id}';
            """);

        using (var scope = factory.Services.CreateScope())
        {
            await Store(scope).RevokeAsync(justRevoked.Id, CancellationToken.None);
            factory.Clock.Set(opened);
            Assert.Equal(2, await Store(scope).DeleteExpiredAsync(CancellationToken.None));
        }

        Assert.Equal(1, await RowCountAsync(live.Id));
        Assert.Equal(0, await RowCountAsync(ancient.Id));
        Assert.Equal(0, await RowCountAsync(longRevoked.Id));
        Assert.Equal(1, await RowCountAsync(justRevoked.Id));
    }

    [Fact]
    public async Task A_sweep_that_finds_nothing_is_a_success()
    {
        factory.Clock.Set(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        try
        {
            using var scope = factory.Services.CreateScope();

            // With the clock in the past, nothing in the table can be old enough to remove.
            Assert.Equal(0, await Store(scope).DeleteExpiredAsync(CancellationToken.None));
        }
        finally
        {
            factory.Clock.Reset();
        }
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static ISessionStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISessionStore>();

    private static ISessionReader Reader(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISessionReader>();

    private Task<int> RowCountAsync(Guid sessionId) =>
        factory.ScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [Id] = '{sessionId}'");

    private async Task<Guid> AnAccountAsync()
    {
        var accountId = Guid.CreateVersion7();
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"session-{Guid.NewGuid():N}"));

        return accountId;
    }

    private async Task<Session> AnOpenSessionAsync()
    {
        var accountId = await AnAccountAsync();
        using var scope = factory.Services.CreateScope();

        return await Store(scope).OpenAsync(accountId, CancellationToken.None);
    }
}
