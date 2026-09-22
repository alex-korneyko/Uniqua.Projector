using Microsoft.AspNetCore.Identity;
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

        var created = await users.CreateAsync(user, password);
        if (created.Succeeded)
        {
            return Result<StoredAccount, AccountError>.Success(Project(user));
        }

        // The unique indexes are the authority on whether an address or a name is taken, so the
        // refusal is translated from what the store actually said rather than from a prior probe
        // that another request could have invalidated in between.
        var error = created.Errors.Any(IsDuplicateEmail)
            ? AccountErrors.EmailTaken
            : created.Errors.Any(IsDuplicateName)
                ? AccountErrors.DisplayNameTaken
                : AccountErrors.EmailTaken;

        // module=accounts, and no address: a creation failure names the reason, never the value.
        logger.LogInformation(
            "module=accounts event=account_creation_refused reason={Reason}", error.Code);

        return Result<StoredAccount, AccountError>.Failure(error);
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

    public async Task RecordFailureAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // A single statement, so two concurrent failures both count. A lost update would
        // understate the count by one and delay very slightly less, which the curve tolerates —
        // it is a floor, not an exact ledger.
        await users.Users
            .Where(user => user.Id == accountId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(user => user.AccessFailedCount, user => user.AccessFailedCount + 1)
                    .SetProperty(user => user.LastFailedAttemptAt, clock.UtcNow),
                cancellationToken);

        // No address here, and no credential: otherwise the log becomes the account-enumeration
        // oracle AC-05b exists to close.
        logger.LogInformation("module=accounts event=sign_in_failure_recorded");
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

    private static bool IsDuplicateEmail(IdentityError error) =>
        error.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
            or nameof(IdentityErrorDescriber.DuplicateUserName);

    private static bool IsDuplicateName(IdentityError error) =>
        error.Code == nameof(IdentityErrorDescriber.InvalidUserName);
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
}
