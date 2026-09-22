using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// QG-1 — the security of the single authentication boundary, through the HTTP surface a guesser
/// would actually attack.
/// </summary>
/// <remarks>
/// The last test in this file is the one sad §11 asks for by name. ADR 0010 switched the
/// framework's account lockout off deliberately, and the risk it records is that someone re-enables
/// it because lockout looks like the safe default — silently turning AC-12's "without the account
/// ever becoming unusable to its owner" into its opposite. Nothing about that change would look
/// wrong in review. This suite is what makes it fail.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class GuessingProtectionTests(ApiFactory factory)
{
    private const string Sessions = "/api/v1/sessions";

    // ---- The delay curve, driven rather than waited for -----------------------------------------

    [Theory]
    [InlineData(5, 2)]
    [InlineData(9, 30)]
    public async Task The_next_attempt_is_held_for_the_floor_the_criterion_names(
        int priorFailures, int atLeastSeconds)
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountUnderGuessingAsync(priorFailures);
        var client = await factory.AWritingClientAsync();
        factory.Clock.ClearRequestedDelays();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(
            factory.Clock.LongestRequestedDelay >= TimeSpan.FromSeconds(atLeastSeconds),
            $"after {priorFailures} failures the attempt was held only "
            + $"{factory.Clock.LongestRequestedDelay.TotalSeconds:F1} s, against a floor of "
            + $"{atLeastSeconds} s");
    }

    [Fact]
    public async Task Fifteen_quiet_minutes_return_the_count_to_zero()
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountUnderGuessingAsync(20);
        var client = await factory.AWritingClientAsync();

        // Derived from the stored instant on the next read, so a restart in the middle of this
        // would change nothing — there is no timer to lose.
        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        factory.Clock.ClearRequestedDelays();

        // Two typos, not one: the first proves the delay is gone, the second proves the count
        // itself went back to zero rather than carrying on from 20.
        await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });
        await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });

        Assert.Equal(TimeSpan.Zero, factory.Clock.LongestRequestedDelay);
        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT [AccessFailedCount] FROM [dbo].[AspNetUsers] WHERE [Id] = '{account.Id}'"));
        factory.Clock.Reset();
    }

    // ---- AC-12: the per-source failed-sign-in cap, counted before verification (N-01) -----------

    [Fact]
    public async Task Parallel_attempts_from_one_source_are_each_counted_even_when_none_wait_for_the_other()
    {
        // The reservation happens before SignIn.ExecuteAsync runs, not after it returns — so a
        // batch fired without waiting for one another to finish (the shape a client that hangs up
        // takes: it never observes its own response) still exhausts the cap exactly once. If the
        // slot were instead taken from the result of verification, a burst like this could race
        // past the cap because every attempt would still see the pre-burst count when it checked.
        factory.Clock.Reset();
        var account = await factory.AnAccountUnderGuessingAsync(0);
        var client = await factory.AWritingClientAsync();

        const int burst = SignInRateLimit.PermittedFailuresPerWindow + 5;

        var responses = await Task.WhenAll(Enumerable.Range(0, burst).Select(_ =>
            client.PostAsJsonAsync(Sessions, new { email = account.Email, password = "not-the-password" })));

        var capped = responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests);

        Assert.Equal(burst - SignInRateLimit.PermittedFailuresPerWindow, capped);
    }

    // ---- AC-05b: the refusal that costs the same either way -------------------------------------

    [Fact]
    public async Task An_unknown_address_and_a_wrong_password_take_comparable_time_to_refuse()
    {
        // Distributions with a margin rather than single samples: a flaky oracle is worse than a
        // coarse one, and one unlucky sample either way would make this test lie in both
        // directions over time.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync();

        var wrongPassword = await MedianRefusalTimeAsync(
            client, account.Email, "not-the-password");
        var unknownAddress = await MedianRefusalTimeAsync(
            client, $"{Guid.NewGuid():N}@example.test", "any-password-at-all");

        Assert.InRange(unknownAddress, wrongPassword * 0.4, wrongPassword * 2.5);
    }

    [Fact]
    public async Task After_five_failures_an_unregistered_address_is_held_exactly_like_a_registered_one()
    {
        // AC-05b: "neither the message nor the wait reveals whether the address is registered".
        // Once the curve starts, a registered address is held for seconds; if an unregistered one
        // kept answering at hashing speed, the wait alone would tell a guesser which is which.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var unknown = $"{Guid.NewGuid():N}@example.test";
        var client = await factory.AWritingClientAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = "not-the-password" });

            // Casing and padding vary on purpose: one address, however it is typed, is one count.
            var typed = attempt % 2 == 0 ? unknown : $" {unknown.ToUpperInvariant()} ";
            await client.PostAsJsonAsync(
                Sessions, new { email = typed, password = "not-the-password" });
        }

        factory.Clock.ClearRequestedDelays();
        await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });
        var registeredDelay = factory.Clock.LongestRequestedDelay;

        factory.Clock.ClearRequestedDelays();
        await client.PostAsJsonAsync(
            Sessions, new { email = unknown, password = "not-the-password" });
        var unknownDelay = factory.Clock.LongestRequestedDelay;

        Assert.True(registeredDelay >= TimeSpan.FromSeconds(2));
        Assert.Equal(registeredDelay, unknownDelay);
    }

    [Fact]
    public async Task The_two_refusals_are_identical_in_what_they_say()
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync();

        var wrongPassword = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });
        var unknownAddress = await client.PostAsJsonAsync(
            Sessions,
            new { email = $"{Guid.NewGuid():N}@example.test", password = "any-password-at-all" });

        Assert.Equal(unknownAddress.StatusCode, wrongPassword.StatusCode);
        Assert.Equal(
            await WithoutTraceAsync(unknownAddress), await WithoutTraceAsync(wrongPassword));
    }

    // ---- The regression sad §11 asks for by name -------------------------------------------------

    [Fact]
    public void The_frameworks_lockout_is_off_in_configuration()
    {
        // The first half of the guard: the switch itself. A test on the switch alone would pass
        // while some other mechanism made the account unusable, which is why the next test exists
        // as well.
        var identity = factory.Services
            .GetRequiredService<IOptions<IdentityOptions>>()
            .Value;

        Assert.False(identity.Lockout.AllowedForNewUsers);
    }

    [Fact]
    public async Task An_account_under_sustained_guessing_still_admits_its_owner_at_once()
    {
        // THE ANTI-LOCKOUT REGRESSION (sad §10 QG-1, sad §11 risk row).
        //
        // Twenty recent failures — far past any lockout threshold the framework would have used —
        // and then the owner arrives with the correct password. They must be let in immediately and
        // without delay. If anyone re-enables lockout, this is the test that goes red, and the
        // message below is written for whoever sees it.
        factory.Clock.Reset();
        var account = await factory.AnAccountUnderGuessingAsync(20);
        var client = await factory.AWritingClientAsync();
        factory.Clock.ClearRequestedDelays();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = AccountFixtures.Password });

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            "an account with 20 recent failed attempts refused its own correct password. If "
            + "account lockout has just been switched on because it looked like the safe default, "
            + "that is what has happened: ADR 0010 replaced it with a progressive delay precisely "
            + "so that guessing becomes futile WITHOUT the account becoming unusable to its owner "
            + "(AC-12). Read ADR 0010 before changing this.");

        // And not merely admitted — admitted without waiting. A correct password is never held.
        Assert.Equal(TimeSpan.Zero, factory.Clock.LongestRequestedDelay);
    }

    [Fact]
    public void The_verification_path_never_consults_the_frameworks_lockout()
    {
        // Verified by flipping the switch during implementation: turning lockout on made the
        // configuration test above go red, but left this suite's behavioural assertions green.
        // The reason is that the store verifies against the hasher directly and never through
        // UserManager.CheckPasswordAsync, which is the API that consults lockout — so the sign-in
        // path is structurally immune to that setting rather than merely configured away from it.
        //
        // That immunity is worth keeping, and it is the thing that would actually be lost. Someone
        // "simplifying" the store to CheckPasswordAsync would reconnect the lockout machinery in
        // one line, and no configuration test would notice. This is that guard.
        var source = File.ReadAllText(Path.Combine(
            SolutionDirectory(), "src", "Uniqua.Projector.Infrastructure", "Accounts",
            "IdentityAccountStore.cs"));

        foreach (var lockoutAware in new[]
        {
            "CheckPasswordAsync", "AccessFailedAsync", "IsLockedOutAsync", "SetLockoutEnabledAsync",
        })
        {
            Assert.False(
                source.Contains(lockoutAware, StringComparison.Ordinal),
                $"IdentityAccountStore now calls UserManager.{lockoutAware}, which consults the "
                + "framework's account lockout. ADR 0010 replaced lockout with a progressive delay "
                + "so an account never becomes unusable to its owner (AC-12); routing verification "
                + "back through the lockout-aware API undoes that whatever the configuration says.");
        }
    }

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

    [Fact]
    public async Task A_correct_password_returns_the_count_to_zero()
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountUnderGuessingAsync(9);
        var client = await factory.AWritingClientAsync();

        await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = AccountFixtures.Password });

        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT [AccessFailedCount] FROM [dbo].[AspNetUsers] WHERE [Id] = '{account.Id}'"));
    }

    [Fact]
    public async Task Nothing_in_a_refusal_reveals_that_an_account_is_under_attack()
    {
        // AC-12's quieter requirement. A response that differed once the delay began would tell a
        // guesser they had found a real address and were being throttled.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync();

        var undelayed = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });

        var hammered = await factory.AnAccountUnderGuessingAsync(30);
        var delayed = await client.PostAsJsonAsync(
            Sessions, new { email = hammered.Email, password = "not-the-password" });

        Assert.Equal(undelayed.StatusCode, delayed.StatusCode);
        Assert.Equal(await WithoutTraceAsync(undelayed), await WithoutTraceAsync(delayed));
        Assert.Null(delayed.Headers.RetryAfter);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return string.Join('|', document.RootElement.EnumerateObject()
            .Where(member => member.Name is not "traceId")
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}={member.Value}"));
    }

    private async Task<double> MedianRefusalTimeAsync(
        HttpClient client,
        string email,
        string password)
    {
        var samples = new List<double>();

        for (var sample = 0; sample < 5; sample++)
        {
            // The clock's recorded delays are cleared each time so the guessing protection, which
            // this client will start to accrue against a real address, cannot creep into the
            // measurement as real time.
            factory.Clock.ClearRequestedDelays();

            var elapsed = Stopwatch.StartNew();
            await client.PostAsJsonAsync(Sessions, new { email, password });
            elapsed.Stop();

            samples.Add(elapsed.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }
}
