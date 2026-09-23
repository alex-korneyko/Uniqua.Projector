namespace Uniqua.Projector.Domain.Accounts;

/// <summary>
/// AC-12's answer to "how long must this attempt be held back?". ADR 0010 chose this over account
/// lockout: guessing becomes futile while the owner's correct password is still accepted
/// immediately, because nothing here is ever consulted on success.
/// </summary>
/// <remarks>
/// The whole rule is a pure function of two stored columns and the present instant, so the reset
/// is <em>derived when the next failure is recorded</em> rather than kept alive by a timer or a
/// scheduled job — a restart cannot lose it, and there is no background state to get out of step
/// with the counter.
/// </remarks>
public static class GuessingDelay
{
    /// <summary>
    /// How many consecutive failures are free. An owner mistyping their own password a few times
    /// must not be made to wait for it.
    /// </summary>
    public const int FreeAttempts = 5;

    /// <summary>
    /// AC-12's "after 15 minutes in which no failed attempt reached verification" (review
    /// 2026-09-23 (third re-review) S-03). The only place this figure appears.
    /// </summary>
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The longest an attempt is ever held. AC-12 says the delay "grows with each additional
    /// failure"; spec §6 and ADR 0010 record this bound on that growth as a deliberate decision,
    /// because a delay that grew without limit would turn the defence into a way of tying up
    /// request threads. The curve reaches it at the 14th consecutive failure.
    /// </summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The consecutive-failure count once a new failure has been added to what is stored — the
    /// number the failure being made will carry.
    /// </summary>
    /// <remarks>
    /// This is where AC-12's "returns to zero ... after 15 minutes in which no failed attempt
    /// reached verification" lives: a failure after the quiet period is the first of a fresh count,
    /// however high the stored figure was. Keeping the rule here, rather than in the store's SQL,
    /// is what lets it be tested without a database and changed in one place.
    /// </remarks>
    /// <param name="storedFailures">The counter as the store holds it.</param>
    /// <param name="lastFailedAttemptAt">
    /// When the most recent failure happened, or <c>null</c> if none is recorded — in which case
    /// the count cannot be dated and is read as "no evidence of a recent attempt".
    /// </param>
    /// <param name="now">The present, supplied by the caller's clock port.</param>
    public static int CountAfterFailure(
        int storedFailures,
        DateTimeOffset? lastFailedAttemptAt,
        DateTimeOffset now)
    {
        if (lastFailedAttemptAt is null)
        {
            return 1;
        }

        // The window is measured forwards only. A recorded instant in the future means the clock
        // moved, not that 15 quiet minutes passed, so it must not clear the counter.
        var sinceLastAttempt = now - lastFailedAttemptAt.Value;

        return sinceLastAttempt >= ResetAfter ? 1 : storedFailures + 1;
    }

    /// <summary>
    /// The delay owed by the attempt that has just become this consecutive failure. The attempt
    /// AC-12 describes — "a further attempt" once 5 have already failed — is failure number 6.
    /// </summary>
    /// <param name="consecutiveFailure">
    /// The number of the failure being made, as <see cref="CountAfterFailure"/> returns it.
    /// </param>
    public static TimeSpan ForFailure(int consecutiveFailure)
    {
        if (consecutiveFailure <= FreeAttempts)
        {
            return TimeSpan.Zero;
        }

        // Doubling from the first non-free failure: 2 s at the 6th, 4 s at the 7th, 32 s at the
        // 10th. The two floors AC-12 names are the contract; this is the simplest shape that
        // clears both and never decreases.
        var doublings = Math.Min(consecutiveFailure - FreeAttempts, 20);
        var seconds = Math.Pow(2, doublings);

        return seconds >= Ceiling.TotalSeconds
            ? Ceiling
            : TimeSpan.FromSeconds(seconds);
    }
}
