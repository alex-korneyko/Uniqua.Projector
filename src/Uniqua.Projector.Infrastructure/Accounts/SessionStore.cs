using Microsoft.EntityFrameworkCore;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Infrastructure.Accounts;

/// <summary>
/// The session ports over EF Core. It satisfies both <see cref="ISessionReader"/> and
/// <see cref="ISessionStore"/> because they are two views of one table, while staying two
/// interfaces so the recognition path cannot quietly grow the needs of the write path.
/// </summary>
internal sealed class SessionStore(AppDbContext context, IClock clock)
    : ISessionReader, ISessionStore
{
    /// <summary>
    /// How long a revoked row is kept before the sweep removes it. It is the idle lifetime because
    /// that is the longest a browser could still be presenting a cookie whose session ended — once
    /// that has passed, the row can no longer change any answer.
    /// </summary>
    private static readonly TimeSpan RevokedRecordRetention = Session.IdleLifetime;

    /// <summary>
    /// A primary-key lookup, and deliberately nothing else: no <c>Include</c>, no join, no
    /// projection of the account. This is the statement spec §6's 30 ms budget is really about,
    /// and SessionStoreTests asserts its shape rather than only its answer.
    /// </summary>
    public Task<Session?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        context.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task<Session> OpenAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var session = Session.Open(accountId, clock.UtcNow);

        context.Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);

        return session;
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await context.Sessions
            .SingleOrDefaultAsync(candidate => candidate.Id == sessionId, cancellationToken);

        if (session is null || session.IsRevoked)
        {
            // Nothing to do, and nothing to complain about: revoking an id that was never issued,
            // or one that already ended, leaves the world in the state the caller asked for.
            return;
        }

        session.Revoke(clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> StampActivityAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var session = await context.Sessions
            .SingleOrDefaultAsync(candidate => candidate.Id == sessionId, cancellationToken);

        // The entity decides whether this is worth a write, not this method — the one-hour
        // allowance is part of the session's own rules (spec §6 permits an hour of slack on expiry
        // accuracy, and spending it buys one write per session per hour instead of one per
        // request).
        if (session is null || !session.ShouldStampActivity(now))
        {
            return false;
        }

        session.StampActivity(now);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var openedBefore = now - Session.AbsoluteLifetime;
        var revokedBefore = now - RevokedRecordRetention;

        // One statement, served by IX_Sessions_CreatedAt and the filtered IX_Sessions_RevokedAt.
        // Nothing is materialised: the rows are only being removed.
        return context.Sessions
            .Where(session =>
                session.CreatedAt <= openedBefore
                || (session.RevokedAt != null && session.RevokedAt <= revokedBefore))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
