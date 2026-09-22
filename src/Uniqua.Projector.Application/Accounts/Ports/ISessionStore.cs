using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Application.Accounts.Ports;

/// <summary>
/// Everything that changes a session. Declared in Application and implemented in Infrastructure,
/// so a use case can open or end a session without knowing that EF Core exists.
/// </summary>
public interface ISessionStore
{
    /// <summary>Opens a live session for an account, at the clock's present instant.</summary>
    Task<Session> OpenAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Ends exactly the session it is given. Idempotent: a second call keeps the instant the
    /// session actually ended, so the record of when it stopped staying true.
    /// </summary>
    /// <remarks>
    /// Whose session it is, is not this port's concern — it revokes the id it is handed. AC-08's
    /// "and only that one" is kept by the caller only ever passing the id the request arrived on.
    /// </remarks>
    Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that a request arrived on this session, if enough time has passed to be worth a
    /// write. Returns whether anything was written, which is almost always <c>false</c>: an
    /// ordinary read inside the hour costs nothing at all.
    /// </summary>
    /// <remarks>
    /// It takes the session rather than its id on purpose. The only caller is the recognition
    /// path, which has just read the record — and sad §6 flow 5's postcondition is that
    /// recognition cost <em>one</em> indexed read. An id here would mean a second one on every
    /// authenticated request in the product, against a 30 ms budget.
    /// </remarks>
    Task<bool> StampActivityAsync(Session session, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the rows that can no longer affect any decision — opened past the 90-day ceiling,
    /// or revoked long enough ago that no browser could still be presenting them. Returns how many
    /// went. Finding nothing is a successful sweep.
    /// </summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);
}
