namespace Uniqua.Projector.Application.Accounts.Ports;

/// <summary>
/// Announces that a session has ended, so whatever is still listening on the account's behalf can
/// stop (AC-09).
/// </summary>
/// <remarks>
/// This port exists because of the reference direction rather than in spite of it. Signing out has
/// to reach the live-update connections a session holds; those live on the hub, in Api; and sad §2
/// fixes the direction as Api → Application, so a use case may not call the hub. The port is
/// declared here and implemented in Api beside the hub, which is the same shape Infrastructure
/// already uses for the session and account ports — the use case never learns who fulfils it.
/// </remarks>
public interface ISessionRevocationNotifier
{
    Task SessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken);
}
