using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Application.Accounts;

/// <summary>
/// AC-04: an account that comes back gets a session. The harder half is AC-05 and AC-05b — every
/// way of failing has to look and cost the same, so that the sign-in form cannot be used to
/// discover which addresses are registered.
/// </summary>
public sealed class SignIn(
    IAccountStore accounts,
    ISessionStore sessions,
    IUnknownAddressAttempts unknownAddresses,
    IClock clock)
{
    /// <summary>
    /// Opens a session, or refuses with the one refusal this use case has.
    /// </summary>
    /// <remarks>
    /// There is exactly one failure path and one error, deliberately. An unknown address, a wrong
    /// password, a malformed submission and a delayed attempt all arrive at the same
    /// <see cref="AccountErrors.CredentialsInvalid"/>, because a second refusal — however
    /// carefully worded — would be a second observable outcome, and the difference between two
    /// outcomes is the information AC-05b exists to withhold.
    /// </remarks>
    public async Task<Result<SignedIn, AccountError>> ExecuteAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindByEmailAsync(email ?? string.Empty, cancellationToken);

        if (account is null)
        {
            // AC-05b. The verification is not a formality: it is what makes the refusal take as
            // long as a real one. And once the curve would hold a registered address back, this
            // one is held back by the same curve — counted in memory, since there is no account
            // row to count against and writing one would store the guess itself.
            await accounts.VerifyDummyPasswordAsync(password ?? string.Empty, cancellationToken);
            var guess = unknownAddresses.RecordFailure(email ?? string.Empty, clock.UtcNow);
            await clock.DelayAsync(GuessingDelay.ForFailure(guess), cancellationToken);

            return Refused();
        }

        var verified = await accounts.VerifyPasswordAsync(
            account.Id, password ?? string.Empty, cancellationToken);

        if (!verified)
        {
            // Recorded first, and on a token no client can cancel. A guesser who hangs up once the
            // verification has cost what it costs already knows the answer; if the count were
            // written after the wait, hanging up would skip it and the curve would never grow.
            var failure = await accounts.RecordFailureAsync(
                account.Id,
                account.ConsecutiveFailures,
                account.LastFailedAttemptAt,
                CancellationToken.None);

            // The delay is served only on failure. A correct password is never held, which is what
            // keeps the account usable to its owner no matter how hard anyone else is guessing
            // (AC-12, ADR 0010).
            await clock.DelayAsync(GuessingDelay.ForFailure(failure), cancellationToken);

            return Refused();
        }

        await accounts.ResetFailuresAsync(account.Id, cancellationToken);
        var session = await sessions.OpenAsync(account.Id, cancellationToken);

        return Result<SignedIn, AccountError>.Success(
            new SignedIn(account.Id, account.Email, account.DisplayName, session.Id));
    }

    private static Result<SignedIn, AccountError> Refused() =>
        Result<SignedIn, AccountError>.Failure(AccountErrors.CredentialsInvalid);
}

/// <summary>What a sign-in hands back — the same shape registration returns, for the same reason.</summary>
/// <param name="AccountId">The stable identity AC-13 promises.</param>
/// <param name="Email">
/// The address as the account holds it — not as it was typed at sign-in, which may differ in case
/// and spacing and would show one account two ways.
/// </param>
/// <param name="DisplayName">Shown straight back (AC-11), so nothing has to display the address.</param>
/// <param name="SessionId">The reference the session cookie will carry.</param>
public sealed record SignedIn(Guid AccountId, string Email, string DisplayName, Guid SessionId);
