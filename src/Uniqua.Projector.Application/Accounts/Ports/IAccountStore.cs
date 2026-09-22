using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Application.Accounts.Ports;

/// <summary>
/// Everything this feature needs an account store to do. Declared here, in Application, and
/// implemented in Infrastructure, so no use case learns which framework holds the accounts — the
/// reference direction sad §2 fixes depends on nothing in this file naming Identity or EF Core.
/// </summary>
public interface IAccountStore
{
    /// <summary>
    /// The account that owns an address, or <c>null</c>. The address is normalised on the way in
    /// through the same code path registration uses, so case and surrounding whitespace cannot
    /// make one address look like two.
    /// </summary>
    Task<StoredAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a new account and its credential, or refuses with the uniqueness reason that
    /// applies. The refusal comes from the store because only the store can know: the unique
    /// indexes are the authority on whether an address or a name is taken.
    /// </summary>
    Task<Result<StoredAccount, AccountError>> CreateAsync(
        Account account,
        string password,
        CancellationToken cancellationToken);

    /// <summary>Whether this is the account's password.</summary>
    Task<bool> VerifyPasswordAsync(Guid accountId, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies a password against a credential no account owns. The result is discarded — the
    /// point is the cost. AC-05b requires an unregistered address to take a comparable time to
    /// refuse, so that the wait reveals nothing the wording already withholds.
    /// </summary>
    Task VerifyDummyPasswordAsync(string password, CancellationToken cancellationToken);

    /// <summary>
    /// Records one failed attempt and returns the number it carries — the count and the instant,
    /// both persisted, with the count advanced by <see cref="GuessingDelay.CountAfterFailure"/> so
    /// AC-12's 15-minute reset is applied when the failure is written rather than held by a timer.
    /// </summary>
    /// <remarks>
    /// Returning the new count, rather than letting the caller add one to what it read earlier,
    /// is what gives parallel guesses against one account each a higher number: the value comes
    /// from the write that won, not from a read every one of them shared.
    /// </remarks>
    /// <param name="accountId">The account the failure is recorded against.</param>
    /// <param name="knownConsecutiveFailures">
    /// The count the caller already read, from the same lookup that found the account — the store
    /// uses it as its first compare-and-set expectation, so an uncontended failure costs one
    /// UPDATE and no extra SELECT. If it no longer matches what is stored, the store re-reads and
    /// retries; only a lost race costs the extra round trip.
    /// </param>
    /// <param name="knownLastFailedAttemptAt">
    /// The instant that came from the same lookup as <paramref name="knownConsecutiveFailures"/>,
    /// paired with it in the same way.
    /// </param>
    Task<int> RecordFailureAsync(
        Guid accountId,
        int knownConsecutiveFailures,
        DateTimeOffset? knownLastFailedAttemptAt,
        CancellationToken cancellationToken);

    /// <summary>Clears the count and the instant together, on a correct password.</summary>
    Task ResetFailuresAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// The account with this id, or <c>null</c>. Used once a request has already been recognised,
    /// to show an account itself — recognition never needs it, which is why the session lookup
    /// stays free of any join to the account.
    /// </summary>
    Task<StoredAccount?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Whether a display name already identifies an account (AC-11b).</summary>
    Task<bool> IsDisplayNameTakenAsync(string displayName, CancellationToken cancellationToken);
}

/// <summary>
/// An account as the store holds it: the entity's own values plus the two columns the guessing
/// delay is computed from. It is a separate type from <see cref="Account"/> because the failure
/// counter is store state rather than something the entity has an opinion about.
/// </summary>
/// <param name="Id">The stable identity AC-13 promises.</param>
/// <param name="Email">The address, as it was typed.</param>
/// <param name="DisplayName">What other members see (AC-11).</param>
/// <param name="ConsecutiveFailures">The persisted failure count AC-12's curve reads.</param>
/// <param name="LastFailedAttemptAt">When the most recent failure happened, or <c>null</c>.</param>
public sealed record StoredAccount(
    Guid Id,
    string Email,
    string DisplayName,
    int ConsecutiveFailures,
    DateTimeOffset? LastFailedAttemptAt);
