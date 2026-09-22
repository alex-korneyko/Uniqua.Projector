using Microsoft.Extensions.Logging;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Application.Accounts;

/// <summary>
/// AC-08: ending the session the request arrived on, and only that one — a sign-out on the phone
/// must not sign the laptop out.
/// </summary>
public sealed class SignOut(
    ISessionStore sessions,
    ISessionRevocationNotifier notifier,
    ILogger<SignOut> logger)
{
    /// <summary>
    /// Ends a session and announces it.
    /// </summary>
    /// <remarks>
    /// Which session is not decided here: the caller passes the id the request arrived on, which
    /// is how AC-08's "and only that one" is kept without this use case needing to know what other
    /// sessions the account holds.
    /// </remarks>
    public async Task ExecuteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        // Written first, announced second. Announcing a revocation that is not yet a fact would
        // let a listener act on something that could still fail.
        await sessions.RevokeAsync(sessionId, cancellationToken);

        try
        {
            await notifier.SessionRevokedAsync(sessionId, cancellationToken);
        }
        catch (Exception failure)
        {
            // The revocation already stands. Turning a completed sign-out into an error because
            // the announcement failed would leave the caller believing they are still signed in
            // while their session is in fact over — the worse of the two outcomes by far.
            // No session reference in the message: sad §8 keeps those out of the log.
            logger.LogWarning(
                failure, "module=accounts event=session_revocation_announcement_failed");
        }
    }
}

/// <summary>
/// The default announcement: none. It exists so <see cref="SignOut"/> is wired and green before
/// the hub does, and it is replaced by the Api-side implementation that sits beside the hub. It is
/// deliberately not silent about being a no-op, so a deployment missing the real one is visible in
/// the log rather than merely quiet.
/// </summary>
public sealed class NoSessionRevocationNotifier(ILogger<NoSessionRevocationNotifier> logger)
    : ISessionRevocationNotifier
{
    public Task SessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "module=accounts event=session_revoked notifier=none "
            + "reason=no_live_update_channel_yet");

        return Task.CompletedTask;
    }
}
