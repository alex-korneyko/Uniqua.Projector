using System.Collections.Concurrent;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// The per-source-sliding-window bookkeeping shared by <see cref="RegistrationRateLimit"/> (AC-01b)
/// and <see cref="SignInRateLimit"/> (AC-12, AC-05b, review 2026-09-22-2 N-01). Both limits reserve
/// a slot for an attempt about to be made, before anything the attempt might do, and hand the slot
/// back through <see cref="Release"/> when the outcome says it should not have counted after all —
/// what counts as "should not have counted" differs between the two callers, which is why the
/// decision to release is theirs and not this type's.
/// </summary>
/// <param name="limiterName">
/// Which limiter this is, carried on the ceiling line as <c>limiter=</c>
/// (<c>registration</c>, <c>sign_in_per_source</c> or <c>sign_in_per_address</c>) — review
/// 2026-09-23 (fourth re-review) V-02: two independently-owned limiters used to share one logger
/// and one ceiling line, so an operator could not tell which table was full.
/// </param>
/// <param name="ceilingConsequence">
/// What not tracking a new key actually means for this limiter, carried on the ceiling line as
/// <c>consequence=</c>. Not a shared constant (V-02): the per-address limiter filling does not
/// leave its pair uncapped in the same sense the per-source or registration limiter filling does
/// — the per-source cap of 100 still applies to that source regardless — so the wording is the
/// owner's to give, not this type's to assume.
/// </param>
/// <remarks>
/// The counter is per process and in memory, pruned lazily rather than on a timer, exactly as
/// <c>RegistrationRateLimit</c> did before this type was extracted (review 2026-09-22-2 N-01,
/// folding R-25's fix into the shared type rather than duplicating it).
/// </remarks>
internal sealed class SlidingWindowLimiter(
    IClock clock,
    int permittedPerWindow,
    TimeSpan window,
    ILogger logger,
    string limiterName,
    string ceilingConsequence)
{
    /// <summary>
    /// The most sources tracked at once, matching the ceiling
    /// <c>InMemoryUnknownAddressAttempts.Capacity</c> accepts for the same reason: an IPv6 address
    /// rotating within its own /64 no longer buys a fresh key (<see cref="RequestSource"/>), but a
    /// flood of distinct sources still must not grow <c>_entries</c> without bound (review
    /// 2026-09-23 P-02). Past it, a new source's slot is not tracked at all — permitted rather than
    /// refused, exactly as <c>InMemoryUnknownAddressAttempts</c> treats an address past its own
    /// capacity — and the gap is logged as an error.
    /// </summary>
    internal const int Capacity = 100_000;

    /// <summary>
    /// How long the forced prune at <see cref="Capacity"/> is throttled to at most once per, so a
    /// limiter kept full costs one full scan per interval rather than one per request (review
    /// 2026-09-23 third re-review T-01). The ceiling log line is throttled separately, on the
    /// limiter's own window.
    /// </summary>
    private static readonly TimeSpan ForcedPruneInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _entries = new();

    private long _lastPrunedAtTicks;
    private long _lastForcedPruneAtTicks;
    private long _lastCeilingLoggedAtTicks;

    /// <summary>
    /// The number of lists in <c>_entries</c>, kept with <see cref="Interlocked"/> on add and
    /// remove rather than read from <see cref="ConcurrentDictionary{TKey,TValue}.Count"/>, which
    /// takes every bucket's lock in turn — a cost this type used to pay on every reserve once it
    /// sat at <see cref="Capacity"/> (review 2026-09-23 third re-review T-01). It is incremented
    /// only by the thread whose <c>TryAdd</c> actually inserted a list, and decremented only by the
    /// one <c>TryRemove</c> of that exact list that succeeds, so it never drifts. It is approximate
    /// only in the sense that a reader can see it a moment before or after a concurrent add or
    /// remove lands; the ceiling check tolerates that the same way it tolerated a stale read of
    /// <c>_entries.Count</c>.
    /// </summary>
    private long _trackedSourceCount;

    /// <summary>How many keys were turned away, uncounted, since the last ceiling line was logged.</summary>
    private long _untrackedSinceLastCeilingLog;

    private long _forcedPruneCount;

    /// <summary>How many sources currently hold a slot. For tests and for anyone watching memory.</summary>
    public int TrackedSourceCount => (int)Interlocked.Read(ref _trackedSourceCount);

    /// <summary>
    /// How many forced reclaims have run at <see cref="Capacity"/>. For tests, so the throttle on
    /// the forced prune is observable rather than inferred from timing (review of T55).
    /// </summary>
    public long ForcedPruneCount => Interlocked.Read(ref _forcedPruneCount);

    /// <summary>
    /// Holds one of this source's slots for an attempt about to be made, or says how long until one
    /// frees up.
    /// </summary>
    /// <remarks>
    /// The slot is taken <em>before</em> the caller does whatever might make it count, so several
    /// concurrent or abandoned attempts from one source cannot all pass a check none of them has yet
    /// recorded. A refusal from the limit itself is never recorded here: counting those would let a
    /// caller who keeps retrying push their own wait further out, which punishes the impatient
    /// rather than the abusive.
    /// </remarks>
    public RateLimitDecision Reserve(string source)
    {
        var now = clock.UtcNow;
        PruneIfDue(now);

        if (!_entries.ContainsKey(source) && TrackedSourceCount >= Capacity)
        {
            // The periodic prune above runs at most once a window; force an immediate reclaim of
            // whatever has already expired before turning a new source away for good. Throttled
            // the same way the periodic prune is (review 2026-09-23 third re-review T-01): a
            // limiter kept full by a flood of new keys must cost one scan per interval, not one
            // per request.
            ForcePruneIfDue(now);
        }

        if (!_entries.ContainsKey(source) && TrackedSourceCount >= Capacity)
        {
            // Not tracked at all rather than refused: refusing would need a slot to refuse *from*,
            // and there is none left to give this source. Permitted and uncounted, exactly as
            // InMemoryUnknownAddressAttempts treats an address past its own capacity. Logged at
            // most once per window, carrying how many keys were turned away since the last
            // line (review 2026-09-23 third re-review T-01) rather than once per refused request.
            LogCeilingReachedIfDue(now);
            return RateLimitDecision.Permitted(now);
        }

        var windowOpenedAt = now - window;
        if (!_entries.TryGetValue(source, out var entries))
        {
            // Counted only when this thread's insert is the one that won: GetOrAdd's factory can
            // run on several racing threads for one absent key while only one list is kept, so
            // counting on the factory's say-so drifted the count upward for good (review of T55).
            var fresh = new List<DateTimeOffset>();
            if (_entries.TryAdd(source, fresh))
            {
                Interlocked.Increment(ref _trackedSourceCount);
                entries = fresh;
            }
            else if (!_entries.TryGetValue(source, out entries))
            {
                // Another thread added the list and a prune removed it again in between.
                return Reserve(source);
            }
        }

        lock (entries)
        {
            if (!_entries.TryGetValue(source, out var current) || !ReferenceEquals(current, entries))
            {
                // A prune removed this list between the lookup and the lock; a slot taken in it
                // would be lost, so start again on the list that is actually held.
                return Reserve(source);
            }

            entries.RemoveAll(recorded => recorded <= windowOpenedAt);

            if (entries.Count < permittedPerWindow)
            {
                entries.Add(now);
                return RateLimitDecision.Permitted(now);
            }

            // The wait until the oldest entry in the window drops out of it, which is the first
            // instant this source will genuinely be allowed to try again. Rounded up, so a caller
            // that obeys it is never refused a second time for being a fraction early.
            var until = entries.Min() + window - now;
            var seconds = Math.Max(1, (int)Math.Ceiling(until.TotalSeconds));

            return RateLimitDecision.Refused(seconds);
        }
    }

    /// <summary>
    /// Hands back the slot <see cref="Reserve"/> took, because the caller's outcome says it should
    /// not have counted after all.
    /// </summary>
    /// <remarks>
    /// review 2026-09-23 (third re-review) S-01: a list this call empties is dropped at once, only
    /// that exact list (as <see cref="PruneExpired"/> does), rather than left in <c>_entries</c>
    /// until the next periodic or forced prune — the same unbounded-growth shape S-01 found, just
    /// reached by releasing instead of by never reserving.
    /// </remarks>
    public void Release(string source, RateLimitDecision reservation)
    {
        if (!reservation.IsPermitted || !_entries.TryGetValue(source, out var entries))
        {
            return;
        }

        lock (entries)
        {
            entries.Remove(reservation.ReservedAt);
            if (entries.Count is 0
                && _entries.TryRemove(new KeyValuePair<string, List<DateTimeOffset>>(source, entries)))
            {
                Interlocked.Decrement(ref _trackedSourceCount);
            }
        }
    }

    /// <summary>
    /// Forgets sources whose window has closed, at most once per window, so a long-running instance
    /// does not keep one entry per address it ever saw: IPv6 address rotation alone would make that
    /// unbounded.
    /// </summary>
    private void PruneIfDue(DateTimeOffset now)
    {
        var last = Interlocked.Read(ref _lastPrunedAtTicks);
        if (IsWithinInterval(now, last, window)
            || Interlocked.CompareExchange(ref _lastPrunedAtTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        PruneExpired(now);
    }

    /// <summary>
    /// Throttles the forced reclaim <see cref="Reserve"/> runs before turning away a new source at
    /// <see cref="Capacity"/> to at most once per <see cref="ForcedPruneInterval"/>, the same
    /// compare-and-set shape as <see cref="PruneIfDue"/> — otherwise a flood of new keys while the
    /// limiter sits at capacity pays for a full scan, locking every bucket, on every single request
    /// (review 2026-09-23 third re-review T-01).
    /// </summary>
    private void ForcePruneIfDue(DateTimeOffset now)
    {
        var last = Interlocked.Read(ref _lastForcedPruneAtTicks);
        if (IsWithinInterval(now, last, ForcedPruneInterval)
            || Interlocked.CompareExchange(ref _lastForcedPruneAtTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        Interlocked.Increment(ref _forcedPruneCount);
        PruneExpired(now);
    }

    /// <summary>
    /// Logs <c>tracked_source_ceiling_reached</c> at most once per the limiter's own window, the
    /// same compare-and-set shape as <see cref="ForcePruneIfDue"/>, carrying how many keys were
    /// turned away uncounted since the previous line rather than writing one line per refused
    /// request (review 2026-09-23 third re-review T-01: "log the ceiling once per window, with a
    /// count"). Deliberately not <see cref="ForcedPruneInterval"/>: that would still write up to 60
    /// lines per registration window and 900 per sign-in window (review of T55).
    /// </summary>
    private void LogCeilingReachedIfDue(DateTimeOffset now)
    {
        Interlocked.Increment(ref _untrackedSinceLastCeilingLog);

        var last = Interlocked.Read(ref _lastCeilingLoggedAtTicks);
        if (IsWithinInterval(now, last, window)
            || Interlocked.CompareExchange(ref _lastCeilingLoggedAtTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        var untrackedSinceLastLine = Interlocked.Exchange(ref _untrackedSinceLastCeilingLog, 0);
        logger.LogError(
            "module=accounts event=tracked_source_ceiling_reached "
            + "limiter={Limiter} consequence={Consequence} ceiling={Ceiling} "
            + "untracked_since_last_line={UntrackedSinceLastLine}",
            limiterName,
            ceilingConsequence,
            Capacity,
            untrackedSinceLastLine);
    }

    /// <summary>
    /// Whether <paramref name="now"/> is still inside <paramref name="interval"/> since
    /// <paramref name="lastTicks"/> — the shared "not due yet" check behind all three throttles
    /// above.
    /// </summary>
    /// <remarks>
    /// review 2026-09-23 (fourth re-review) V-03: a host clock that steps backwards (an NTP
    /// correction, for example, after a Proxmox VM resumes) makes <c>now - last</c> negative. That
    /// used to read as "well within the interval", so a limiter already at capacity would fail
    /// open — new sources turned away uncounted — with the forced reclaim and the ceiling alarm
    /// both silenced for a whole window, until real time caught back up. A negative elapsed time is
    /// treated as due instead: it can only mean the clock moved, never that the interval has not
    /// yet passed.
    /// </remarks>
    private static bool IsWithinInterval(DateTimeOffset now, long lastTicks, TimeSpan interval)
    {
        var elapsedTicks = now.UtcTicks - lastTicks;
        return elapsedTicks >= 0 && elapsedTicks < interval.Ticks;
    }

    /// <summary>
    /// Removes every entry whose window has already closed, right now rather than waiting for the
    /// periodic sweep above — the forced reclaim <see cref="Reserve"/> runs before turning away a
    /// new source at <see cref="Capacity"/>, so a source count only looks exhausted because nothing
    /// has pruned it yet is never mistaken for one that is genuinely full.
    /// </summary>
    private void PruneExpired(DateTimeOffset now)
    {
        var windowOpenedAt = now - window;

        foreach (var (source, entries) in _entries)
        {
            lock (entries)
            {
                entries.RemoveAll(recorded => recorded <= windowOpenedAt);
                if (entries.Count is 0
                    && _entries.TryRemove(new KeyValuePair<string, List<DateTimeOffset>>(source, entries)))
                {
                    // Only this exact list, so a slot reserved concurrently is never thrown away.
                    Interlocked.Decrement(ref _trackedSourceCount);
                }
            }
        }
    }
}

/// <summary>Whether an attempt is allowed, and how long until it would be.</summary>
/// <param name="IsPermitted">Whether the attempt may proceed.</param>
/// <param name="RetryAfterSeconds">
/// Seconds until the caller may try again — a positive number whenever the attempt was refused, so
/// a refusal can always answer "when may I try again".
/// </param>
/// <param name="ReservedAt">The instant of the slot a permitted attempt holds, so it can be released.</param>
public readonly record struct RateLimitDecision(bool IsPermitted, int RetryAfterSeconds, DateTimeOffset ReservedAt)
{
    public static RateLimitDecision Permitted(DateTimeOffset reservedAt) => new(true, 0, reservedAt);

    public static RateLimitDecision Refused(int retryAfterSeconds) => new(false, retryAfterSeconds, default);
}
