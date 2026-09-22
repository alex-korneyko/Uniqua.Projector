using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Application.Accounts;

/// <summary>
/// AC-01: a stranger becomes a signed-in account in one step. The use case orchestrates and
/// decides nothing — what may become an account is the <see cref="Account"/> entity's judgement,
/// and whether an address or a name is already taken is the store's.
/// </summary>
public sealed class RegisterAccount(
    IAccountStore accounts,
    ISessionStore sessions,
    IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Creates the account and opens its session, or refuses with the one reason that applies.
    /// </summary>
    /// <remarks>
    /// The order is fixed by flow 3's postcondition — "nothing is written: no account, no session,
    /// no counter". Validation comes first, then the uniqueness probes, and only then any write,
    /// so every refusal returns before the store has been touched at all.
    /// </remarks>
    public async Task<Result<RegisteredAccount, AccountError>> ExecuteAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        // The entity owns the shape rules (AC-02, AC-02b, AC-11). Restating any of them here would
        // put one rule in two places that can disagree.
        var candidate = Account.Create(email, password, displayName);
        if (!candidate.IsSuccess)
        {
            return Result<RegisteredAccount, AccountError>.Failure(candidate.Error!);
        }

        var account = candidate.Value;

        // The address is probed before the name: when both collide, a visitor who already has an
        // account needs to hear that rather than be sent off to pick a different name.
        if (await accounts.FindByEmailAsync(account.Email, cancellationToken) is not null)
        {
            return Result<RegisteredAccount, AccountError>.Failure(AccountErrors.EmailTaken);
        }

        if (await accounts.IsDisplayNameTakenAsync(account.DisplayName, cancellationToken))
        {
            return Result<RegisteredAccount, AccountError>.Failure(AccountErrors.DisplayNameTaken);
        }

        return await unitOfWork.ExecuteAsync(
            async token =>
            {
                // The probes above can be stale by the time this runs — two visitors submitting
                // the same address at once both pass them. The unique index is the authority, and
                // the store translates its refusal into the same error the probe would have
                // returned, so the loser is told what it would have been told a moment earlier
                // rather than seeing a database failure leak through as something else.
                var stored = await accounts.CreateAsync(account, password, token);
                if (!stored.IsSuccess)
                {
                    return Result<RegisteredAccount, AccountError>.Failure(stored.Error!);
                }

                // Inside the same unit of work as the account: AC-01's "immediately" cannot
                // half-happen into an account nobody can sign in to.
                var session = await sessions.OpenAsync(stored.Value.Id, token);

                return Result<RegisteredAccount, AccountError>.Success(
                    new RegisteredAccount(stored.Value.Id, stored.Value.DisplayName, session.Id));
            },
            cancellationToken);
    }
}

/// <summary>What registration hands back: enough to show the account itself and nothing more.</summary>
/// <param name="AccountId">The stable identity AC-13 promises.</param>
/// <param name="DisplayName">Shown straight back, so AC-01 needs no second round trip.</param>
/// <param name="SessionId">The reference the session cookie will carry.</param>
public sealed record RegisteredAccount(Guid AccountId, string DisplayName, Guid SessionId);
