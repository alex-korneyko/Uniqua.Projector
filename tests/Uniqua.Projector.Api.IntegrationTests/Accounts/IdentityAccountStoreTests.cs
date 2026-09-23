using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;
using Uniqua.Projector.Infrastructure.Accounts;

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
            await Fail(store, account.Id);
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

        await Fail(store, account.Id);
        await Fail(store, account.Id);

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
        // Every caller shares the seed a fresh account actually has (no failures yet), the same
        // way six sign-in requests would each start from their own FindByEmailAsync.
        factory.Clock.Reset();
        Guid accountId;
        using (var setup = factory.Services.CreateScope())
        {
            accountId = (await CreateAccountAsync(Store(setup))).Id;
        }

        var numbers = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope();
            return await Store(scope).RecordFailureAsync(accountId, 0, null, CancellationToken.None);
        }));

        Assert.Equal(Enumerable.Range(1, 6), numbers.Order());
    }

    // ---- N-09 / N-10: the seeded compare-and-set, and an honest count under contention ----------

    [Fact]
    public async Task An_uncontended_failure_issues_one_update_and_no_extra_select()
    {
        // N-10: FindByEmailAsync already read the count and the instant; seeding the first
        // compare-and-set round with them must make an uncontended failure cost exactly the one
        // UPDATE, never a SELECT to re-fetch what the caller already had.
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);
        factory.Commands.Clear();

        await store.RecordFailureAsync(
            account.Id, account.ConsecutiveFailures, account.LastFailedAttemptAt, CancellationToken.None);

        var statements = factory.Commands.Statements;
        Assert.Single(statements);
        Assert.Contains("UPDATE", statements.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_stale_seed_costs_one_re_read_and_still_records_the_true_count()
    {
        // N-10's other half: a seed that no longer matches (another request already wrote) must
        // not be trusted blindly — the store re-reads once and writes the count that read implies,
        // not something derived from the stale seed.
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);
        await store.RecordFailureAsync(
            account.Id, account.ConsecutiveFailures, account.LastFailedAttemptAt, CancellationToken.None);

        // Seeded with the account's original (now stale) values — as if this caller's own
        // FindByEmailAsync had raced a moment before the failure above landed.
        var returned = await store.RecordFailureAsync(
            account.Id, account.ConsecutiveFailures, account.LastFailedAttemptAt, CancellationToken.None);

        Assert.Equal(2, returned);

        var reread = await store.FindByEmailAsync(account.Email, CancellationToken.None);
        Assert.NotNull(reread);
        Assert.Equal(2, reread.ConsecutiveFailures);
    }

    [Fact]
    public async Task Sustained_contention_returns_the_count_actually_written_not_a_stale_one()
    {
        // N-09: once the retry budget is spent, the fallback's blind increment must be reported
        // honestly — the count the write actually landed as, never the `next` a stale earlier
        // read predicted.
        //
        // Q-13(a): the previous version of this test depended on real concurrent workers racing
        // the call under test in real time, could fail with entirely correct code (it did once in
        // the gate run that raised this finding), and never actually confirmed the fallback path
        // had run at all — a bug that always took the uncontended, single-UPDATE path could still
        // pass it by coincidence. ContentionForcer instead forces every one of the retry budget's
        // compare-and-set attempts to lose deterministically, by bumping the row itself, on its
        // own connection, immediately before each conditional UPDATE the store sends — no races,
        // no busy-polling for the hammer to get ahead.
        //
        // IdentityAccountStore.MaxFailureWriteAttempts (IdentityAccountStore.cs:26) is 8 and is
        // private, so it is restated here; a mismatch would mean the interceptor stopped matching
        // the SQL shape the retry loop actually sends, which is exactly the thing this test must
        // catch rather than silently pass around.
        const int ExpectedRetryBudget = 8;

        Guid accountId;
        using (var setup = factory.Services.CreateScope())
        {
            accountId = (await CreateAccountAsync(Store(setup))).Id;
        }

        factory.Contention.Reset();
        factory.Contention.TargetAccountId = accountId;
        factory.Contention.Enabled = true;
        factory.Logs.Clear();

        int returned;
        using (var scope = factory.Services.CreateScope())
        {
            returned = await Store(scope).RecordFailureAsync(
                accountId, 0, null, CancellationToken.None);
        }

        factory.Contention.Enabled = false;

        // Every single conditional UPDATE the retry loop could possibly send was bumped out from
        // under it, so the loop must have exhausted its whole retry budget before falling back.
        Assert.Equal(ExpectedRetryBudget, factory.Contention.InterceptedAttempts);

        var stored = await factory.ScalarAsync<int>(
            $"SELECT [AccessFailedCount] FROM [dbo].[AspNetUsers] WHERE [Id] = '{accountId}'");

        // The count the fallback reports must be the count actually stored — never the stale
        // `next` a much earlier, already-superseded read predicted.
        Assert.Equal(stored, returned);

        // And it must have taken, and logged, the fallback path rather than merely landing on the
        // right number by coincidence.
        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Message.Contains("event=sign_in_failure_recorded", StringComparison.Ordinal)
            && entry.Message.Contains("contended=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resetting_clears_the_count_and_the_instant_together()
    {
        using var scope = factory.Services.CreateScope();
        var store = Store(scope);
        var account = await CreateAccountAsync(store);
        await Fail(store, account.Id);

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

    // ---- N-11: an unexpected Identity error is a fault, never a uniqueness refusal --------------

    [Fact]
    public async Task An_unexpected_identity_error_surfaces_as_a_fault_not_a_uniqueness_refusal()
    {
        // Neither probe nor unique index caught anything, yet Identity's own CreateAsync still
        // failed — for a reason the account rules do not cover (a configuration or programming
        // fault). That must never be reported to a visitor as "email taken" or "display name
        // taken": IdentityAccountStore.CreateAsync throws instead, which the application's one
        // exception handler turns into a 500 problem (proven generically by
        // ProblemDetailsTests.An_unmapped_failure_reveals_nothing_about_the_exception_behind_it).
        // A real Identity misconfiguration is not reachable through Account.Create's own
        // validation, so the failure is forced with a test double store rather than real input.
        var store = StoreOverAFakeThatFailsCreateWith(
            new IdentityError { Code = "ConcurrencyFailure", Description = "stale row version" });

        var account = Account.Create(
            $"{Guid.NewGuid():N}@example.test", GoodPassword, $"name-{Guid.NewGuid():N}");
        Assert.True(account.IsSuccess);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateAsync(account.Value, GoodPassword, CancellationToken.None));

        Assert.Contains("ConcurrencyFailure", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccountErrors.EmailTaken.Code, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccountErrors.DisplayNameTaken.Code, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="IdentityAccountStore"/> over a <see cref="UserManager{TUser}"/> whose only store
    /// is <see cref="FailingUserStore"/>: no database, no validators, so
    /// <see cref="UserManager{TUser}.CreateAsync(TUser, string)"/> reaches the store's
    /// <c>CreateAsync</c> directly and returns exactly the failure handed in.
    /// </summary>
    private static IAccountStore StoreOverAFakeThatFailsCreateWith(IdentityError failure)
    {
        var users = new UserManager<ProjectorUser>(
            new FailingUserStore(failure),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ProjectorUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ProjectorUser>>.Instance);

        return new IdentityAccountStore(
            users,
            new PasswordHasher<ProjectorUser>(),
            new DummyCredential(Options.Create(new PasswordHasherOptions())),
            new TestClock(),
            NullLogger<IdentityAccountStore>.Instance);
    }

    /// <summary>
    /// The minimum <see cref="IUserStore{TUser}"/> surface <see cref="IdentityAccountStore"/>
    /// touches on the way to <c>CreateAsync</c> — enough to hold a user in memory, never a
    /// database — whose <c>CreateAsync</c> always returns one configured failure.
    /// </summary>
    private sealed class FailingUserStore(IdentityError failure) :
        IUserStore<ProjectorUser>, IUserPasswordStore<ProjectorUser>, IQueryableUserStore<ProjectorUser>
    {
        // IdentityAccountStore.CreateAsync probes IsDisplayNameTakenAsync via `users.Users
        // .AsNoTracking().AnyAsync(...)` before it ever reaches CreateAsync below — EF Core's
        // AnyAsync throws against a plain in-memory IQueryable, so this needs the async-provider
        // shim (EmptyAsyncQueryable) rather than `Enumerable.Empty<ProjectorUser>().AsQueryable()`.
        public IQueryable<ProjectorUser> Users { get; } = new EmptyAsyncQueryable<ProjectorUser>();

        public Task<IdentityResult> CreateAsync(ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Failed(failure));

        public Task<IdentityResult> UpdateAsync(ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<ProjectorUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult<ProjectorUser?>(null);

        public Task<ProjectorUser?> FindByNameAsync(
            string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult<ProjectorUser?>(null);

        public Task<string> GetUserIdAsync(ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString());

        public Task<string?> GetUserNameAsync(ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(
            ProjectorUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(
            ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(
            ProjectorUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetPasswordHashAsync(
            ProjectorUser user, string? passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash);

        public Task<bool> HasPasswordAsync(ProjectorUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash is not null);

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Always empty, and answerable through EF Core's <c>*Async</c> LINQ operators
    /// (<see cref="IAsyncQueryProvider"/>), which a plain <c>Enumerable.Empty&lt;T&gt;().AsQueryable()</c>
    /// is not — <c>AnyAsync</c> against it throws rather than returning <see langword="false"/>.
    /// Every EF Core query shape the store might someday send is out of scope on purpose: this
    /// double exists only to let <c>Users</c> answer "is anything here" with "no".
    /// </summary>
    private sealed class EmptyAsyncQueryable<T> : IQueryable<T>, IAsyncEnumerable<T>
    {
        private readonly IQueryable<T> _empty = Enumerable.Empty<T>().AsQueryable();

        public Type ElementType => _empty.ElementType;

        public Expression Expression => _empty.Expression;

        public IQueryProvider Provider { get; }

        public EmptyAsyncQueryable() => Provider = new EmptyAsyncQueryProvider(_empty.Provider);

        public IEnumerator<T> GetEnumerator() => _empty.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new EmptyAsyncEnumerator();

        private sealed class EmptyAsyncEnumerator : IAsyncEnumerator<T>
        {
            public T Current => throw new InvalidOperationException("The sequence is empty.");

            public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>The synchronous provider <see cref="EmptyAsyncQueryable{T}"/> hands EF Core's async operators.</summary>
    private sealed class EmptyAsyncQueryProvider(IQueryProvider inner) : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression) => inner.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new EmptyAsyncQueryable<TElement>();

        public object Execute(Expression expression) => inner.Execute(expression)!;

        public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

        // Every EF Core `*Async` extension (AnyAsync, SingleOrDefaultAsync, ...) against an empty
        // source resolves to the same constant its synchronous counterpart would (false, null,
        // ...), so running the expression synchronously and wrapping the result is exact here —
        // not merely a stand-in.
        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).IsGenericType
                ? typeof(TResult).GetGenericArguments()[0]
                : typeof(TResult);

            var executed = inner.Execute(expression);

            var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType);

            return (TResult)fromResult.Invoke(null, [executed])!;
        }
    }

    /// <summary>
    /// Records a failure without caring about the seed's accuracy — tests that only need the
    /// count to advance, not to prove anything about the compare-and-set itself, seed with (0,
    /// null) and let the store's own re-read fall back when that no longer matches.
    /// </summary>
    private static Task<int> Fail(IAccountStore store, Guid accountId) =>
        store.RecordFailureAsync(accountId, 0, null, CancellationToken.None);

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
