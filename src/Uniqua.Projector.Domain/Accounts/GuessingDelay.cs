namespace Uniqua.Projector.Domain.Accounts;

/// <summary>
/// AC-12's answer to "how long must this attempt be held back?". ADR 0010 chose this over account
/// lockout: guessing becomes futile while the owner's correct password is still accepted
/// immediately, because nothing here is ever consulted on success.
/// </summary>
/// <remarks>
/// The whole rule is a pure function of two stored columns and the present instant, so the reset
/// is <em>derived on read</em> rather than kept alive by a timer or a scheduled job — a restart
/// cannot lose it, and there is no background state to get out of step with the counter.
/// </remarks>
public static class GuessingDelay
{
    /// <summary>
    /// How many consecutive failures are free. An owner mistyping their own password a few times
    /// must not be made to wait for it.
    /// </summary>
    public const int FreeAttempts = 5;

    /// <summary>
    /// AC-12's "after 15 minutes in which no attempt is made at all". The only place this figure
    /// appears.
    /// </summary>
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The longest an attempt is ever held. AC-12 is satisfied by the two floors below; the
    /// ceiling is an engineering choice, because a delay that grew without bound would turn this
    /// defence into a way of tying up request threads.
    /// </summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The delay owed before answering an attempt against this account.
    /// </summary>
    /// <param name="consecutiveFailures">The stored failure counter.</param>
    /// <param name="lastFailedAttemptAt">
    /// When the most recent failure happened, or <c>null</c> if none is recorded — in which case
    /// the count cannot be dated and is read as "no evidence of a recent attempt".
    /// </param>
    /// <param name="now">The present, supplied by the caller's clock port.</param>
    public static TimeSpan For(
        int consecutiveFailures,
        DateTimeOffset? lastFailedAttemptAt,
        DateTimeOffset now)
    {
        if (consecutiveFailures <= FreeAttempts || lastFailedAttemptAt is null)
        {
            return TimeSpan.Zero;
        }

        // The window is measured forwards only. A recorded instant in the future means the clock
        // moved, not that 15 quiet minutes passed, so it must not clear the counter.
        var sinceLastAttempt = now - lastFailedAttemptAt.Value;
        if (sinceLastAttempt >= ResetAfter)
        {
            return TimeSpan.Zero;
        }

        // Doubling from the first non-free failure: 2 s at the 6th, 4 s at the 7th, 32 s at the
        // 10th. The two floors AC-12 names are the contract; this is the simplest shape that
        // clears both and never decreases.
        var doublings = Math.Min(consecutiveFailures - FreeAttempts, 20);
        var seconds = Math.Pow(2, doublings);

        return seconds >= Ceiling.TotalSeconds
            ? Ceiling
            : TimeSpan.FromSeconds(seconds);
    }
}
