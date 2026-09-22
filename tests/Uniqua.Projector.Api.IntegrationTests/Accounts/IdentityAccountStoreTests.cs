using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T7 — ASP.NET Core Identity behind a port. Three promises are proven here: an address that owns
/// no account still costs a full password verification (AC-05b, so neither the message nor the
/// wait reveals whether an address is registered), the framework's lockout is off and stays off
/// (ADR 0010 and the sad §11 regression), and the failure counter is persisted in a form the delay
/// curve can read back after a restart (AC-12).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IdentityAccountStoreTests(ApiFactory factory)
{
    private const string GoodPassword = "a-long-enough-password";

    private static IAccountStore Store(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IAccountStore>();

    // ---- The port speaks Domain, not Identity ---------------------------------------------------

    [Fact]
    public void No_identity_or_ef_type_appears_anywhere_on_the_port()
    {
        // The whole point of the port is that Application never learns who fulfils it. A single
        // Identity type in a signature would leak the choice of framework upward through the
        // layering that sad §2 fixes.
        var leaked = typeof(IAccountStore).GetMethods()
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .SelectMany(Unwrap)
            .Select(type => type.Assembly.GetName().Name ?? string.Empty)
            .Where(assembly =>
                assembly.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal)
                || assembly.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Distinct()
            .ToArray();

        Assert.Empty(leaked);

        static IEnumerable<Type> Unwrap(Type type) =>
            type.IsGenericType ? type.GetGenericArguments().Append(type) : [type];
    }

    // ---- AC-05b: an unregistered address costs the same as a registered one ---------------------

    [Fact]
    public async Task An_address_no_account_owns_is_simply_not_found()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Null(await Store(scope).FindByEmailAsync(
            $"{Guid.NewGuid():N}@example.test", CancellationToken.None));
    }

    [Fact]
    public async Task Verifying_against_no_account_costs_a_time_comparable_to_a_real_verification()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);

        // The fastest of several runs, so a scheduling hiccup cannot fail the test while a
        // genuinely absent verification still would.
        var real = await FastestAsync(() =>
            store.VerifyPasswordAsync(account.Id, "the-wrong-password", CancellationToken.None));
        var dummy = await FastestAsync(() =>
            store.VerifyDummyPasswordAsync("the-wrong-password", CancellationToken.None));

        // "Comparable" generously: the dummy must be the same order of magnitude, which is what
        // makes the wait uninformative. It must emphatically not be near-instant.
        Assert.InRange(dummy.TotalMilliseconds, real.TotalMilliseconds * 0.5, real.TotalMilliseconds * 2.5);
    }

    [Fact]
    public async Task One_verification_costs_at_least_the_hundred_milliseconds_spec_six_asks_for()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);

        var elapsed = await FastestAsync(() =>
            store.VerifyPasswordAsync(account.Id, GoodPassword, CancellationToken.None));

        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(100),
            $"a verification took {elapsed.TotalMilliseconds:F0} ms; spec §6 asks for at least 100 ms");
    }

    // ---- ADR 0010: lockout is off, and stays off ------------------------------------------------

    [Fact]
    public void The_frameworks_lockout_is_switched_off()
    {
        var options = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<
                Microsoft.AspNetCore.Identity.IdentityOptions>>()
            .Value;

        Assert.False(options.Lockout.AllowedForNewUsers);
    }

    [Fact]
    public async Task An_account_with_twenty_recent_failures_still_accepts_its_correct_password()
    {
        // The sad §11 regression. If someone re-enables lockout because it looks like the safe
        // default, this is the test that goes red rather than AC-12 quietly breaking.
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await store.RecordFailureAsync(account.Id, CancellationToken.None);
        }

        Assert.True(await store.VerifyPasswordAsync(
            account.Id, GoodPassword, CancellationToken.None));
    }

    // ---- AC-12: the counter is persisted, not held in memory ------------------------------------

    [Fact]
    public async Task Recording_a_failure_persists_both_the_count_and_when_it_happened()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);

        await store.RecordFailureAsync(account.Id, CancellationToken.None);
        await store.RecordFailureAsync(account.Id, CancellationToken.None);

        var reread = await store.FindByEmailAsync(account.Email, CancellationToken.None);

        Assert.NotNull(reread);
        Assert.Equal(2, reread.ConsecutiveFailures);
        Assert.NotNull(reread.LastFailedAttemptAt);
    }

    [Fact]
    public async Task Parallel_failures_against_one_account_each_get_their_own_number()
    {
        // Review 2026-09-22 R-03: parallel guesses must not all read one stale count. Each write
        // that wins returns the number it wrote, so six at once are failures 1 to 6, not six 1s.
        factory.Clock.Reset();
        Guid accountId;
        using (var setup = factory.Services.CreateScope())
        {
            accountId = (await CreateAccountAsync(Store(setup))).Id;
        }

        var numbers = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope();
            return await Store(scope).RecordFailureAsync(accountId, CancellationToken.None);
        }));

        Assert.Equal(Enumerable.Range(1, 6), numbers.Order());
    }

    [Fact]
    public async Task Resetting_clears_the_count_and_the_instant_together()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);
        await store.RecordFailureAsync(account.Id, CancellationToken.None);

        await store.ResetFailuresAsync(account.Id, CancellationToken.None);

        var reread = await store.FindByEmailAsync(account.Email, CancellationToken.None);
        Assert.NotNull(reread);
        Assert.Equal(0, reread.ConsecutiveFailures);
        Assert.Null(reread.LastFailedAttemptAt);
    }

    // ---- One normalisation path for registration and sign-in ------------------------------------

    [Theory]
    [InlineData("  {0}  ")]
    [InlineData("{0}")]
    public async Task An_address_is_found_however_it_was_spaced_or_cased(string shape)
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);

        var asTyped = string.Format(shape, account.Email.ToUpperInvariant());

        Assert.NotNull(await store.FindByEmailAsync(asTyped, CancellationToken.None));
    }

    [Fact]
    public async Task A_display_name_already_in_use_is_reported_as_taken()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);

        Assert.True(await store.IsDisplayNameTakenAsync(
            account.DisplayName.ToUpperInvariant(), CancellationToken.None));
        Assert.False(await store.IsDisplayNameTakenAsync(
            $"never-used-{Guid.NewGuid():N}", CancellationToken.None));
    }

    [Fact]
    public async Task An_account_with_no_stored_hash_is_a_failed_verification_not_a_success()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);

        // Identity leaves PasswordHash nullable, so this state is reachable.
        await factory.ExecuteAsync(
            $"UPDATE [dbo].[AspNetUsers] SET [PasswordHash] = NULL WHERE [Id] = '{account.Id}';");

        Assert.False(await store.VerifyPasswordAsync(
            account.Id, GoodPassword, CancellationToken.None));
    }

    // ---- Two accounts cannot share an address or a name, through the port -----------------------

    [Fact]
    public async Task A_second_account_on_a_taken_address_is_refused_with_the_named_reason()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var first = await CreateAccountAsync(store);

        var duplicate = Account.Create(first.Email, GoodPassword, $"other-{Guid.NewGuid():N}");
        Assert.True(duplicate.IsSuccess);

        var refused = await store.CreateAsync(
            duplicate.Value, GoodPassword, CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Same(AccountErrors.EmailTaken, refused.Error);
    }

    private static async Task<StoredAccount> CreateAccountAsync(IAccountStore store)
    {
        var created = Account.Create(
            $"{Guid.NewGuid():N}@example.test", GoodPassword, $"name-{Guid.NewGuid():N}");
        Assert.True(created.IsSuccess);

        var stored = await store.CreateAsync(
            created.Value, GoodPassword, CancellationToken.None);
        Assert.True(stored.IsSuccess);

        return stored.Value;
    }

    private static async Task<TimeSpan> FastestAsync(Func<Task> work)
    {
        var fastest = TimeSpan.MaxValue;

        for (var run = 0; run < 3; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            await work();
            stopwatch.Stop();
            fastest = stopwatch.Elapsed < fastest ? stopwatch.Elapsed : fastest;
        }

        return fastest;
    }
}
