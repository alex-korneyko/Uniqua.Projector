namespace Uniqua.Projector.Application.Accounts.Ports;

/// <summary>
/// Runs several port calls as one outcome. AC-01 promises an account is created and its session
/// opened "immediately without asking them to sign in again", which makes those two writes one
/// thing that either happened or did not — an account stored with no session would satisfy neither
/// reading of that sentence.
/// </summary>
/// <remarks>
/// This is a sixth port, where sad §5 names five (ISessionStore, ISessionReader, IAccountStore,
/// IClock, ISessionRevocationNotifier). It is declared here rather than solved by reaching for the
/// DbContext, because Application may not know EF Core exists — but the deviation is real and is
/// raised in the implement handoff for the design stage to either adopt or replace.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> so that either all of its writes stand or none of them do.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken);
}
