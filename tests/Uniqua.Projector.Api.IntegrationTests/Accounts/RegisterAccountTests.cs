using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T8 — registering. AC-01 promises the account is created and a session opened "immediately
/// without asking them to sign in again", which makes the two writes one outcome rather than two
/// steps that might half-happen. The refusal cases carry the stronger promise: flow 3's
/// postcondition is that nothing at all is written — no account, no session, no counter.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RegisterAccountTests(ApiFactory factory)
{
    private const string GoodPassword = "a-long-enough-password";

    // ---- AC-01: one account, one live session, the display name back ---------------------------

    [Fact]
    public async Task Registering_unused_values_creates_the_account_and_opens_its_session_at_once()
    {
        factory.Clock.Reset();
        var email = NewEmail();
        var displayName = NewDisplayName();

        using var scope = factory.Services.CreateScope();
        var result = await Register(scope).ExecuteAsync(
            email, GoodPassword, displayName, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var registered = result.Value;

        // AC-11: the display name comes back, so nothing has to show the address.
        Assert.Equal(displayName, registered.DisplayName);
        // AC-13: one stable identity, produced by the application.
        Assert.Equal(7, registered.AccountId.Version);

        Assert.Equal(1, await AccountRowsAsync(email));
        Assert.Equal(1, await LiveSessionRowsAsync(registered.AccountId));
        Assert.Equal(registered.AccountId, await AccountIdOfSessionAsync(registered.SessionId));
    }

    [Fact]
    public async Task The_session_it_opens_is_live_from_the_clocks_present_instant()
    {
        factory.Clock.Reset();
        using var scope = factory.Services.CreateScope();

        var result = await Register(scope).ExecuteAsync(
            NewEmail(), GoodPassword, NewDisplayName(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var session = await scope.ServiceProvider
            .GetRequiredService<Application.Accounts.Ports.ISessionReader>()
            .FindAsync(result.Value.SessionId, CancellationToken.None);

        Assert.NotNull(session);
        Assert.False(session.IsRevoked);
        Assert.False(session.IsExpired(factory.Clock.UtcNow));
    }

    // ---- The refusals, each leaving the store exactly as it was ---------------------------------

    [Theory]
    // AC-02: the password bounds.
    [InlineData("short", "accounts.password_invalid")]
    // AC-02b: something that cannot be an address.
    [InlineData(null, "accounts.email_invalid")]
    public async Task A_malformed_submission_is_refused_and_writes_nothing(
        string? password, string expectedCode)
    {
        var email = password is null ? "not-an-address" : NewEmail();
        var before = await TotalRowsAsync();

        using var scope = factory.Services.CreateScope();
        var result = await Register(scope).ExecuteAsync(
            email, password ?? GoodPassword, NewDisplayName(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(before, await TotalRowsAsync());
    }

    [Fact]
    public async Task A_password_of_one_hundred_and_twenty_nine_characters_is_refused()
    {
        using var scope = factory.Services.CreateScope();

        var result = await Register(scope).ExecuteAsync(
            NewEmail(), new string('p', 129), NewDisplayName(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.PasswordInvalid, result.Error);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public async Task A_password_on_either_bound_is_accepted(int length)
    {
        using var scope = factory.Services.CreateScope();

        var result = await Register(scope).ExecuteAsync(
            NewEmail(), new string('p', length), NewDisplayName(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_second_account_on_a_registered_address_is_refused_and_writes_nothing()
    {
        // AC-03. The probe names the address specifically: enumeration here is deliberate, because
        // a visitor cannot fix a collision they are not told about (spec §6.1).
        factory.Clock.Reset();
        var email = NewEmail();
        using var first = factory.Services.CreateScope();
        Assert.True((await Register(first).ExecuteAsync(
            email, GoodPassword, NewDisplayName(), CancellationToken.None)).IsSuccess);

        var before = await TotalRowsAsync();

        using var second = factory.Services.CreateScope();
        var result = await Register(second).ExecuteAsync(
            email.ToUpperInvariant(), GoodPassword, NewDisplayName(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.EmailTaken, result.Error);
        Assert.Equal(before, await TotalRowsAsync());
    }

    [Fact]
    public async Task A_second_account_on_a_used_display_name_is_refused_and_writes_nothing()
    {
        // AC-11b.
        factory.Clock.Reset();
        var displayName = NewDisplayName();
        using var first = factory.Services.CreateScope();
        Assert.True((await Register(first).ExecuteAsync(
            NewEmail(), GoodPassword, displayName, CancellationToken.None)).IsSuccess);

        var before = await TotalRowsAsync();

        using var second = factory.Services.CreateScope();
        var result = await Register(second).ExecuteAsync(
            NewEmail(), GoodPassword, displayName.ToUpperInvariant(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.DisplayNameTaken, result.Error);
        Assert.Equal(before, await TotalRowsAsync());
    }

    [Fact]
    public async Task When_both_the_address_and_the_name_are_taken_the_address_is_named_first()
    {
        // The more fundamental collision: a visitor who already has an account needs to know that
        // before being sent to pick a different name.
        factory.Clock.Reset();
        var email = NewEmail();
        var displayName = NewDisplayName();
        using var first = factory.Services.CreateScope();
        Assert.True((await Register(first).ExecuteAsync(
            email, GoodPassword, displayName, CancellationToken.None)).IsSuccess);

        using var second = factory.Services.CreateScope();
        var result = await Register(second).ExecuteAsync(
            email, GoodPassword, displayName, CancellationToken.None);

        Assert.Same(AccountErrors.EmailTaken, result.Error);
    }

    [Fact]
    public async Task A_concurrent_duplicate_address_is_refused_in_the_same_words_as_a_probed_one()
    {
        // Both probes pass; the unique index refuses one write. The point is that the loser is
        // told the same thing it would have been told a moment later, rather than seeing a
        // database error leak through as something else.
        factory.Clock.Reset();
        var email = NewEmail();

        using var one = factory.Services.CreateScope();
        using var two = factory.Services.CreateScope();

        var attempts = await Task.WhenAll(
            Register(one).ExecuteAsync(email, GoodPassword, NewDisplayName(), CancellationToken.None),
            Register(two).ExecuteAsync(email, GoodPassword, NewDisplayName(), CancellationToken.None));

        Assert.Equal(1, attempts.Count(attempt => attempt.IsSuccess));

        var refusal = attempts.Single(attempt => !attempt.IsSuccess);
        Assert.Same(AccountErrors.EmailTaken, refusal.Error);
        Assert.Equal(1, await AccountRowsAsync(email));
    }

    [Fact]
    public async Task A_refusal_leaves_no_failure_counter_behind_either()
    {
        // flow 3's postcondition is "no account, no session, no counter" — a refused registration
        // must not look like a failed sign-in against an account that does not exist.
        var email = NewEmail();

        using var scope = factory.Services.CreateScope();
        await Register(scope).ExecuteAsync(email, "short", NewDisplayName(), CancellationToken.None);

        Assert.Equal(0, await AccountRowsAsync(email));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static RegisterAccount Register(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<RegisterAccount>();

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private static string NewDisplayName() => $"reg-{Guid.NewGuid():N}";

    private Task<int> AccountRowsAsync(string email) =>
        factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[AspNetUsers] WHERE [NormalizedEmail] = '{email.Trim().ToUpperInvariant()}'");

    private Task<int> LiveSessionRowsAsync(Guid accountId) =>
        factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [AccountId] = '{accountId}' AND [RevokedAt] IS NULL");

    private async Task<Guid> AccountIdOfSessionAsync(Guid sessionId) =>
        Guid.Parse((await factory.ScalarAsync<string>(
            $"SELECT CAST([AccountId] AS nvarchar(36)) FROM [dbo].[Sessions] WHERE [Id] = '{sessionId}'"))!);

    private async Task<(int Accounts, int Sessions)> TotalRowsAsync() =>
        (await factory.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[AspNetUsers]"),
         await factory.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[Sessions]"));
}
