using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Application.Accounts.Ports;

/// <summary>
/// Reading one session by the reference its cookie carries. One port with one method, kept
/// separate from <see cref="ISessionStore"/> on purpose: this is the path every authenticated
/// request in the product takes, spec §6 budgets 30 ms for it, and a narrow port is what keeps
/// that path from acquiring an <c>Include</c> later on because something else needed one.
/// </summary>
public interface ISessionReader
{
    /// <summary>
    /// The session, or <c>null</c> if no such id was ever issued.
    /// </summary>
    /// <remarks>
    /// A revoked session is returned, not hidden. AC-10 refuses "regardless of what their browser
    /// still holds", and that refusal has to rest on a record saying the session ended — if the
    /// row were withheld, a revoked session and a forged id would be indistinguishable, and the
    /// sweep removing rows 14 days later would silently change what a request means.
    /// </remarks>
    Task<Session?> FindAsync(Guid sessionId, CancellationToken cancellationToken);
}
