using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T9 — signing in. The interesting promises here are the ones about what a refusal does
/// <em>not</em> say: AC-05 and AC-05b require a wrong password and an unregistered address to be
/// refused in the same words and at a comparable cost, so neither the message nor the wait can be
/// used to discover which addresses are registered. AC-12 requires the delay to grow while the
/// owner's correct password is still answered at once.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SignInTests(ApiFactory factory)
{
    private const string GoodPassword = "a-long-enough-password";

    // ---- AC-04: the happy path -----------------------------------------------------------------

    [Fact]
    public async Task The_registered_address_and_password_open_a_session()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        using var scope = factory.Services.CreateScope();
        var result = await SignIn(scope).ExecuteAsync(
            account.Email, GoodPassword, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(account.DisplayName, result.Value.DisplayName);
        Assert.Equal(account.AccountId, result.Value.AccountId);

        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM [dbo].[Sessions]
            WHERE [Id] = '{result.Value.SessionId}' AND [RevokedAt] IS NULL
            """));
    }

    [Fact]
    public async Task A_successful_sign_in_is_never_delayed_and_zeroes_the_failure_count()
    {
        // The sad §10 QG-1 assertion, at use-case level: nine failures then the right password.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        await FailAsync(account.Email, times: 9);

        factory.Clock.ClearRequestedDelays();
        using var scope = factory.Services.CreateScope();
        var result = await SignIn(scope).ExecuteAsync(
            account.Email, GoodPassword, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.Zero, factory.Clock.LongestRequestedDelay);
        Assert.Equal(0, await FailureCountAsync(account.AccountId));
        Assert.Null(await LastFailedAtAsync(account.AccountId));
    }

    [Fact]
    public async Task The_address_is_accepted_however_it_was_cased_or_spaced()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        using var scope = factory.Services.CreateScope();
        var result = await SignIn(scope).ExecuteAsync(
            $"  {account.Email.ToUpperInvariant()} ", GoodPassword, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ---- AC-05 and AC-05b: one refusal, indistinguishable ---------------------------------------

    [Fact]
    public async Task A_wrong_password_and_an_unregistered_address_are_refused_identically()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        using var scope = factory.Services.CreateScope();
        var wrongPassword = await SignIn(scope).ExecuteAsync(
            account.Email, "not-the-password", CancellationToken.None);
        var unknownAddress = await SignIn(scope).ExecuteAsync(
            $"{Guid.NewGuid():N}@example.test", GoodPassword, CancellationToken.None);

        Assert.False(wrongPassword.IsSuccess);
        Assert.False(unknownAddress.IsSuccess);

        // Not merely equal wording — the same object, so nothing can make them diverge later.
        Assert.Same(AccountErrors.CredentialsInvalid, wrongPassword.Error);
        Assert.Same(AccountErrors.CredentialsInvalid, unknownAddress.Error);
    }

    [Fact]
    public async Task An_unregistered_address_still_pays_for_a_password_verification()
    {
        // AC-05b's other half: the wait must reveal nothing the wording withholds. The dummy
        // verification is what buys that, and it is only comparable because the dummy hash was
        // produced at the configured cost — see IdentityAccountStoreTests.
        using var scope = factory.Services.CreateScope();

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await SignIn(scope).ExecuteAsync(
            $"{Guid.NewGuid():N}@example.test", GoodPassword, CancellationToken.None);
        elapsed.Stop();

        Assert.True(
            elapsed.Elapsed >= PasswordHashingCost.MinimumVerificationTime,
            $"refusing an unknown address took only {elapsed.ElapsedMilliseconds} ms, so the wait "
            + "tells an attacker the address is unregistered");
    }

    [Fact]
    public async Task An_unregistered_address_leaves_no_counter_behind()
    {
        // There is no account to count against, so nothing is recorded — a counter here would be a
        // row that exists only because someone guessed at an address.
        var before = await factory.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[AspNetUsers]");

        using var scope = factory.Services.CreateScope();
        await SignIn(scope).ExecuteAsync(
            $"{Guid.NewGuid():N}@example.test", GoodPassword, CancellationToken.None);

        Assert.Equal(before, await factory.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[AspNetUsers]"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("short")]
    public async Task A_malformed_submission_at_sign_in_gets_the_same_refusal_as_a_wrong_password(
        string value)
    {
        // The contract deliberately omits format: email here. Validating the address shape at
        // sign-in would answer faster for a malformed address than for a wrong password, which is
        // another way of telling an attacker something.
        using var scope = factory.Services.CreateScope();

        var result = await SignIn(scope).ExecuteAsync(value, value, CancellationToken.None);

        Assert.Same(AccountErrors.CredentialsInvalid, result.Error);
    }

    // ---- AC-12: the delay curve, driven rather than waited for ----------------------------------

    [Theory]
    [InlineData(5, 2)]
    [InlineData(9, 30)]
    public async Task The_next_failure_is_held_for_at_least_the_curves_floor(
        int priorFailures, int atLeastSeconds)
    {
        // AC-12's Given is "5 consecutive attempts have already failed": the attempt under test is
        // the 6th failure, which §6 holds for at least 2 s. With 9 prior, it is the 10th (>= 30 s).
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        await FailAsync(account.Email, priorFailures);

        factory.Clock.ClearRequestedDelays();
        using var scope = factory.Services.CreateScope();
        await SignIn(scope).ExecuteAsync(account.Email, "not-the-password", CancellationToken.None);

        Assert.True(
            factory.Clock.LongestRequestedDelay >= TimeSpan.FromSeconds(atLeastSeconds),
            $"after {priorFailures} failures the attempt was held only "
            + $"{factory.Clock.LongestRequestedDelay.TotalSeconds:F1} s");
    }

    [Fact]
    public async Task A_delayed_refusal_reads_exactly_like_an_undelayed_one()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        await FailAsync(account.Email, times: 10);

        using var scope = factory.Services.CreateScope();
        var delayed = await SignIn(scope).ExecuteAsync(
            account.Email, "not-the-password", CancellationToken.None);

        Assert.Same(AccountErrors.CredentialsInvalid, delayed.Error);
    }

    [Fact]
    public async Task The_fifth_failure_is_still_free()
    {
        // The last free attempt: 4 prior failures, so this one is the 5th and is not held at all.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        await FailAsync(account.Email, times: 4);

        factory.Clock.ClearRequestedDelays();
        using var scope = factory.Services.CreateScope();
        await SignIn(scope).ExecuteAsync(account.Email, "not-the-password", CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, factory.Clock.LongestRequestedDelay);
        Assert.Equal(5, await FailureCountAsync(account.AccountId));
    }

    [Fact]
    public async Task Fifteen_quiet_minutes_return_the_stored_count_to_zero()
    {
        // AC-12: "the count of consecutive failures returns to zero ... after 15 minutes in which
        // no attempt is made". So after the quiet period the next failure is the 1st of a fresh
        // count, and an owner who mistypes twice is not held on the second typo either.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        await FailAsync(account.Email, times: 20);

        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        factory.Clock.ClearRequestedDelays();

        using var scope = factory.Services.CreateScope();
        await SignIn(scope).ExecuteAsync(account.Email, "not-the-password", CancellationToken.None);
        await SignIn(scope).ExecuteAsync(account.Email, "not-the-password", CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, factory.Clock.LongestRequestedDelay);
        Assert.Equal(2, await FailureCountAsync(account.AccountId));
        factory.Clock.Reset();
    }

    [Fact]
    public async Task An_abandoned_delayed_attempt_still_counts_as_a_failure()
    {
        // A guesser who hangs up once the verification cost has passed must not skip the count:
        // otherwise the curve never grows and guessing is limited only by processor time.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        await FailAsync(account.Email, times: 6);

        factory.Clock.AbandonDelays = true;
        try
        {
            using var scope = factory.Services.CreateScope();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SignIn(scope).ExecuteAsync(
                account.Email, "not-the-password", CancellationToken.None));
        }
        finally
        {
            factory.Clock.AbandonDelays = false;
        }

        Assert.Equal(7, await FailureCountAsync(account.AccountId));
        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_failure_one_second_inside_the_window_is_still_held()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        await FailAsync(account.Email, times: 10);

        factory.Clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        factory.Clock.ClearRequestedDelays();

        using var scope = factory.Services.CreateScope();
        await SignIn(scope).ExecuteAsync(account.Email, "not-the-password", CancellationToken.None);

        Assert.True(factory.Clock.LongestRequestedDelay > TimeSpan.Zero);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task Every_failure_records_the_instant_the_reset_will_be_measured_from()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        using var scope = factory.Services.CreateScope();
        await SignIn(scope).ExecuteAsync(account.Email, "not-the-password", CancellationToken.None);

        Assert.Equal(1, await FailureCountAsync(account.AccountId));
        Assert.NotNull(await LastFailedAtAsync(account.AccountId));
    }

    [Fact]
    public async Task A_refused_sign_in_opens_no_session()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var before = await SessionCountAsync(account.AccountId);

        using var scope = factory.Services.CreateScope();
        await SignIn(scope).ExecuteAsync(account.Email, "not-the-password", CancellationToken.None);

        Assert.Equal(before, await SessionCountAsync(account.AccountId));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static SignIn SignIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<SignIn>();

    /// <summary>An account plus the address it was registered with, which registration does not return.</summary>
    private sealed record Registered(Guid AccountId, string Email, string DisplayName);

    private async Task<Registered> ARegisteredAccountAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.test";

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<RegisterAccount>()
            .ExecuteAsync(email, GoodPassword, $"signin-{Guid.NewGuid():N}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return new Registered(result.Value.AccountId, email, result.Value.DisplayName);
    }

    private async Task FailAsync(string email, int times)
    {
        using var scope = factory.Services.CreateScope();
        var signIn = SignIn(scope);

        for (var attempt = 0; attempt < times; attempt++)
        {
            await signIn.ExecuteAsync(email, "not-the-password", CancellationToken.None);
        }
    }

    private Task<int> FailureCountAsync(Guid accountId) =>
        factory.ScalarAsync<int>(
            $"SELECT [AccessFailedCount] FROM [dbo].[AspNetUsers] WHERE [Id] = '{accountId}'");

    private Task<string?> LastFailedAtAsync(Guid accountId) =>
        factory.ScalarAsync<string>(
            $"""
            SELECT CONVERT(nvarchar(40), [LastFailedAttemptAt], 127)
            FROM [dbo].[AspNetUsers] WHERE [Id] = '{accountId}'
            """);

    private Task<int> SessionCountAsync(Guid accountId) =>
        factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [AccountId] = '{accountId}'");
}
