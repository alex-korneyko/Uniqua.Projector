using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Infrastructure.Accounts;

/// <summary>
/// ASP.NET Core Identity, kept behind <see cref="IAccountStore"/>. ADR 0010 uses Identity for what
/// it is good at — the password hash and the failure counter — and takes over the decision it
/// makes badly for this product: its lockout, which would make an account unusable to its owner.
/// Lockout is switched off in configuration and the count is treated as data for the delay curve.
/// </summary>
internal sealed class IdentityAccountStore(
    UserManager<ProjectorUser> users,
    IPasswordHasher<ProjectorUser> hasher,
    DummyCredential dummy,
    IClock clock,
    ILogger<IdentityAccountStore> logger) : IAccountStore
{
    /// <summary>How many times a failure write is retried after losing a race to another one.</summary>
    private const int MaxFailureWriteAttempts = 8;

    public async Task<StoredAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Trim, then through Identity's own normalizer, which upper-cases. This is the pair
        // data-model.md specifies and the same pair CreateAsync writes with — so whether two
        // addresses collide is decided by code that travels with the application rather than by
        // the database's collation. The trim is ours: Identity's normalizer only changes case, so
        // without it a typed-in leading space makes a registered address look unregistered.
        var normalized = users.NormalizeEmail(email.Trim());

        var found = await users.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.NormalizedEmail == normalized, cancellationToken);

        return found is null ? null : Project(found);
    }

    public async Task<StoredAccount?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var found = await users.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == accountId, cancellationToken);

        return found is null ? null : Project(found);
    }

    public async Task<Result<StoredAccount, AccountError>> CreateAsync(
        Account account,
        string password,
        CancellationToken cancellationToken)
    {
        if (await IsDisplayNameTakenAsync(account.DisplayName, cancellationToken))
        {
            return Result<StoredAccount, AccountError>.Failure(AccountErrors.DisplayNameTaken);
        }

        var user = new ProjectorUser
        {
            Id = account.Id,
            // UserName holds the address: it is Identity's login key, and it is NOT the display
            // name, which other members see.
            UserName = account.Email,
            Email = account.Email,
            DisplayName = account.DisplayName,
            NormalizedDisplayName = NormalizeDisplayName(account.DisplayName),
            LockoutEnabled = false,
        };

        IdentityResult created;
        try
        {
            created = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException failure) when (UniquenessRefusal(failure) is { } refusal)
        {
            // The probes in the use case can be stale: two visitors submitting the same address at
            // once both pass them, and Identity's own validators pass too, because they probe the
            // same way. The unique index is the only authority that cannot be raced, so its
            // refusal is translated into the error the probe would have returned rather than
            // being allowed to surface as a database failure.
            logger.LogInformation(
                "module=accounts event=account_creation_refused reason={Reason} source=unique_index",
                refusal.Code);

            return Result<StoredAccount, AccountError>.Failure(refusal);
        }

        if (created.Succeeded)
        {
            return Result<StoredAccount, AccountError>.Success(Project(user));
        }

        // The unique indexes are the authority on whether an address is taken, so the refusal is
        // translated from what the store actually said rather than from a prior probe that
        // another request could have invalidated in between. A taken display name never arrives
        // here: Identity knows nothing of display names, so that refusal comes from the probe
        // above or from the unique index.
        if (created.Errors.Any(IsDuplicateEmail))
        {
            // module=accounts, and no address: a creation failure names the reason, never the value.
            logger.LogInformation(
                "module=accounts event=account_creation_refused reason={Reason}",
                AccountErrors.EmailTaken.Code);

            return Result<StoredAccount, AccountError>.Failure(AccountErrors.EmailTaken);
        }

        // Anything else is a configuration or programming fault, not something the visitor did.
        // Reporting it as a uniqueness refusal would send them to change a field that was fine,
        // so it surfaces as a server error — with the error codes only, never the values.
        throw new InvalidOperationException(
            "Identity refused to create an account for a reason the account rules do not cover: "
            + string.Join(", ", created.Errors.Select(error => error.Code)));
    }

    public async Task<bool> VerifyPasswordAsync(
        Guid accountId,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await users.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);

        if (user?.PasswordHash is null)
        {
            // Identity leaves the column nullable, so this state is reachable. It is a failed
            // verification, never a success — and it still costs the time a real one would.
            await VerifyDummyPasswordAsync(password, cancellationToken);
            return false;
        }

        return hasher.VerifyHashedPassword(user, user.PasswordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    public Task VerifyDummyPasswordAsync(string password, CancellationToken cancellationToken)
    {
        // The result is deliberately discarded. This call exists for its cost, and the cost is
        // only equal to a real verification because the dummy hash was produced at the configured
        // iteration count — a default-cost hash here would be several times cheaper and the wait
        // would become the account-enumeration oracle AC-05b exists to close.
        hasher.VerifyHashedPassword(new ProjectorUser(), dummy.Hash, password);
        return Task.CompletedTask;
    }

    public async Task<int> RecordFailureAsync(
        Guid accountId,
        int knownConsecutiveFailures,
        DateTimeOffset? knownLastFailedAttemptAt,
        CancellationToken cancellationToken)
    {
        // Compare-and-set rather than a blind increment. The next count depends on the stored
        // instant (GuessingDelay owns the 15-minute reset), so it is computed from a read and then
        // written only if nobody else wrote in between; a concurrent failure makes the write miss
        // and the loop reads again. Each of N parallel guesses therefore ends up with its own,
        // higher number instead of all of them sharing the one they read.
        //
        // The first round's expectation is the pair the caller already read (from the same lookup
        // that found the account), not a fresh SELECT: an uncontended failure is then one UPDATE
        // and nothing else. Only a lost race — the write affecting zero rows — pays for a re-read.
        var now = clock.UtcNow;
        var next = 1;
        var storedCount = knownConsecutiveFailures;
        var storedAt = knownLastFailedAttemptAt;

        for (var attempt = 0; attempt < MaxFailureWriteAttempts; attempt++)
        {
            if (attempt > 0)
            {
                var current = await users.Users
                    .AsNoTracking()
                    .Where(user => user.Id == accountId)
                    .Select(user => new { user.AccessFailedCount, user.LastFailedAttemptAt })
                    .SingleOrDefaultAsync(cancellationToken);

                if (current is null)
                {
                    return 0;
                }

                storedCount = current.AccessFailedCount;
                storedAt = current.LastFailedAttemptAt;
            }

            next = GuessingDelay.CountAfterFailure(storedCount, storedAt, now);

            var written = await users.Users
                .Where(user => user.Id == accountId
                    && user.AccessFailedCount == storedCount
                    && user.LastFailedAttemptAt == storedAt)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(user => user.AccessFailedCount, next)
                        .SetProperty(user => user.LastFailedAttemptAt, now),
                    cancellationToken);

            if (written == 1)
            {
                // No address here, and no credential: otherwise the log becomes the
                // account-enumeration oracle AC-05b exists to close.
                logger.LogInformation("module=accounts event=sign_in_failure_recorded");
                return next;
            }
        }

        // Sustained contention on one account: fall back to a blind increment so the failure is
        // still counted. `next` was computed from the last read and is stale by construction —
        // this round is only reached because something else kept writing after that read — so the
        // count actually stored is read back rather than returned. Reporting the stale `next`
        // would understate the delay exactly when many guesses are racing (N-09).
        await users.Users
            .Where(user => user.Id == accountId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(user => user.AccessFailedCount, user => user.AccessFailedCount + 1)
                    .SetProperty(user => user.LastFailedAttemptAt, now),
                cancellationToken);

        var stored = await users.Users
            .AsNoTracking()
            .Where(user => user.Id == accountId)
            .Select(user => user.AccessFailedCount)
            .SingleOrDefaultAsync(cancellationToken);

        logger.LogInformation("module=accounts event=sign_in_failure_recorded contended=true");
        return stored;
    }

    public Task ResetFailuresAsync(Guid accountId, CancellationToken cancellationToken) =>
        users.Users
            .Where(user => user.Id == accountId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(user => user.AccessFailedCount, 0)
                    .SetProperty(user => user.LastFailedAttemptAt, (DateTimeOffset?)null),
                cancellationToken);

    public Task<bool> IsDisplayNameTakenAsync(string displayName, CancellationToken cancellationToken)
    {
        var normalized = NormalizeDisplayName(displayName);

        return users.Users
            .AsNoTracking()
            .AnyAsync(user => user.NormalizedDisplayName == normalized, cancellationToken);
    }

    /// <summary>
    /// The display name's comparison key, produced the same way Identity produces the email's —
    /// trim, then invariant upper-case — so AC-11b is decided by this code rather than by a
    /// collation that a restore onto another server could change.
    /// </summary>
    private static string NormalizeDisplayName(string displayName) =>
        displayName.Trim().ToUpperInvariant();

    private static StoredAccount Project(ProjectorUser user) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.DisplayName,
        user.AccessFailedCount,
        user.LastFailedAttemptAt);

    /// <summary>
    /// Which uniqueness rule the database refused on, or <c>null</c> if the failure was something
    /// else entirely and must not be swallowed. The index name is what distinguishes them, since
    /// both violations arrive as the same SQL Server error number.
    /// </summary>
    private static AccountError? UniquenessRefusal(DbUpdateException failure)
    {
        // 2601 duplicate key in a unique index, 2627 unique constraint violation.
        if (failure.InnerException is not SqlException { Number: 2601 or 2627 } violation)
        {
            return null;
        }

        if (violation.Message.Contains("DisplayNameIndex", StringComparison.Ordinal))
        {
            return AccountErrors.DisplayNameTaken;
        }

        return violation.Message.Contains("EmailIndex", StringComparison.Ordinal)
            || violation.Message.Contains("UserNameIndex", StringComparison.Ordinal)
                ? AccountErrors.EmailTaken
                : null;
    }

    private static bool IsDuplicateEmail(IdentityError error) =>
        error.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
            or nameof(IdentityErrorDescriber.DuplicateUserName);
}

/// <summary>
/// A credential no account owns, hashed once at the application's configured cost. Verifying
/// against it is what makes an unregistered address cost the same as a registered one (AC-05b).
/// </summary>
/// <remarks>
/// It builds its own hasher from the configured options rather than taking the scoped
/// <see cref="IPasswordHasher{T}"/>, so the hash can be computed once for the whole process
/// instead of once per request — at this iteration count that difference is most of a second.
/// </remarks>
internal sealed class DummyCredential(IOptions<PasswordHasherOptions> options)
{
    private const string NobodysPassword = "a-password-no-account-has-or-will-ever-have";

    private readonly Lazy<string> _hash = new(() =>
        new PasswordHasher<ProjectorUser>(options).HashPassword(new ProjectorUser(), NobodysPassword));

    public string Hash => _hash.Value;
}

/// <summary>The system clock, which is the only thing allowed to actually know the time.</summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        duration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(duration, cancellationToken);
}
