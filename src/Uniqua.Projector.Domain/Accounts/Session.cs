namespace Uniqua.Projector.Domain.Accounts;

/// <summary>
/// A signed-in session: the record that lets an account be recognised again without signing in.
/// </summary>
/// <remarks>
/// This entity owns the whole answer to "is this session still good?" — AC-07's 14 days of
/// inactivity and AC-07b's 90-day ceiling. Nothing else in the codebase re-derives that
/// arithmetic: the authentication handler asks, it does not calculate (sad.md § 5). The entity
/// reads no clock of its own either; every method that needs the present takes it as an argument,
/// so a caller under test can control time and a caller in production cannot disagree with it.
/// </remarks>
public sealed class Session
{
    /// <summary>AC-07. The only place this figure appears as an expiry rule.</summary>
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(14);

    /// <summary>AC-07b. The ceiling activity cannot lift.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// How stale the activity stamp is allowed to get. spec §6 permits expiry to be accurate to
    /// within an hour, and spending that allowance buys one write per session per hour instead of
    /// one per request.
    /// </summary>
    public static readonly TimeSpan ActivityStampInterval = TimeSpan.FromHours(1);

    /// <summary>Only the store materialises a session this way.</summary>
    private Session()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When a request last arrived on this session. The idle rule is measured from here.</summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Opens a live session for an account.</summary>
    public static Session Open(Guid accountId, DateTimeOffset at) => new()
    {
        Id = Ids.New(at),
        AccountId = accountId,
        CreatedAt = at,
        LastSeenAt = at,
    };

    /// <summary>
    /// Revocation is a fact of its own, deliberately not folded into <see cref="IsExpired"/>: a
    /// caller has to check both, so "signed out" and "timed out" can never be confused for each
    /// other in a log or in a refusal.
    /// </summary>
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>
    /// True once either rule has been crossed. The two are or-ed rather than ranked — a session
    /// that is both idle and past its ceiling is simply finished, with no precedence to argue over.
    /// A <paramref name="now"/> earlier than the stored instants yields a negative interval and so
    /// never reads as an expiry, which is what keeps a backwards clock from signing everyone out.
    /// </summary>
    public bool IsExpired(DateTimeOffset now) =>
        now - LastSeenAt >= IdleLifetime || now - CreatedAt >= AbsoluteLifetime;

    /// <summary>Ends the session. Calling it again keeps the first instant.</summary>
    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;

    /// <summary>
    /// Whether this request should cost a write. False for anything under
    /// <see cref="ActivityStampInterval"/>, so an active session is stamped once an hour rather
    /// than on every read.
    /// </summary>
    public bool ShouldStampActivity(DateTimeOffset now) => now - LastSeenAt >= ActivityStampInterval;

    /// <summary>
    /// Records that a request arrived. Never moves the stamp backwards, so an out-of-order or
    /// skewed instant cannot shorten a session's life.
    /// </summary>
    public void StampActivity(DateTimeOffset now)
    {
        if (now > LastSeenAt)
        {
            LastSeenAt = now;
        }
    }
}
