using System.Collections.Concurrent;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// AC-01b: at most five registrations from one request source per minute. Anti-spam hygiene on the
/// one endpoint a stranger can reach, not an authorization control — which is why losing the
/// counter on a restart is an accepted cost rather than a hole.
/// </summary>
/// <remarks>
/// <para>
/// The counter is per process and in memory. A restart permits up to five more registrations from
/// one source, and two instances each permit five; both are stated in spec §6.1 as accepted
/// residual risk. A shared store would make the limit exact and would also make the registration
/// path depend on it being available, which is the worse trade for hygiene.
/// </para>
/// <para>
/// A sliding window of individual registration instants, rather than a fixed bucket: a fixed bucket
/// lets ten registrations through across a boundary, and the wait it reports is the time to the
/// next bucket rather than the time until the caller is actually allowed to try.
/// </para>
/// </remarks>
public sealed class RegistrationRateLimit(IClock clock)
{
    /// <summary>AC-01b's figure.</summary>
    public const int PermittedPerWindow = 5;

    /// <summary>AC-01b's "within the past minute".</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _registrations = new();

    private long _lastPrunedAtTicks;

    /// <summary>How many sources currently hold a slot. For tests and for anyone watching memory.</summary>
    public int TrackedSourceCount => _registrations.Count;

    /// <summary>
    /// Holds one of this source's slots for a registration about to be attempted, or says how long
    /// until one frees up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slot is taken <em>before</em> the use case runs, so six concurrent submissions from one
    /// source cannot all pass a check none of them has yet recorded. A registration the use case
    /// refuses hands its slot back through <see cref="Release"/>: AC-01b counts accounts that were
    /// registered, and a visitor correcting a refused submission has registered nothing.
    /// </para>
    /// <para>
    /// A refusal from the limit itself is deliberately <em>not</em> recorded. Counting those would
    /// let a caller who keeps retrying push their own wait further and further out, which punishes
    /// the impatient rather than the abusive.
    /// </para>
    /// </remarks>
    public RateLimitDecision Reserve(string source)
    {
        var now = clock.UtcNow;
        PruneIfDue(now);

        var windowOpenedAt = now - Window;
        var registrations = _registrations.GetOrAdd(source, _ => []);

        lock (registrations)
        {
            if (!_registrations.TryGetValue(source, out var current)
                || !ReferenceEquals(current, registrations))
            {
                // A prune removed this list between the lookup and the lock; a slot taken in it
                // would be lost, so start again on the list that is actually held.
                return Reserve(source);
            }

            registrations.RemoveAll(registered => registered <= windowOpenedAt);

            if (registrations.Count < PermittedPerWindow)
            {
                registrations.Add(now);
                return RateLimitDecision.Permitted(now);
            }

            // The wait until the oldest registration in the window drops out of it, which is the
            // first instant this caller will genuinely be allowed to try again. Rounded up, so a
            // caller that obeys it is never refused a second time for being a fraction early.
            var until = registrations.Min() + Window - now;
            var seconds = Math.Max(1, (int)Math.Ceiling(until.TotalSeconds));

            return RateLimitDecision.Refused(seconds);
        }
    }

    /// <summary>
    /// Hands back the slot <see cref="Reserve"/> took, because the registration it was held for
    /// did not create an account.
    /// </summary>
    public void Release(string source, RateLimitDecision reservation)
    {
        if (!reservation.IsPermitted || !_registrations.TryGetValue(source, out var registrations))
        {
            return;
        }

        lock (registrations)
        {
            registrations.Remove(reservation.ReservedAt);
        }
    }

    /// <summary>
    /// Forgets sources whose window has closed, at most once per window, so a long-running
    /// instance does not keep one entry per address that ever registered: IPv6 address rotation
    /// alone would make that unbounded.
    /// </summary>
    private void PruneIfDue(DateTimeOffset now)
    {
        var last = Interlocked.Read(ref _lastPrunedAtTicks);
        if (now.UtcTicks - last < Window.Ticks
            || Interlocked.CompareExchange(ref _lastPrunedAtTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        var windowOpenedAt = now - Window;

        foreach (var (source, registrations) in _registrations)
        {
            lock (registrations)
            {
                registrations.RemoveAll(registered => registered <= windowOpenedAt);
                if (registrations.Count is 0)
                {
                    // Only this exact list, so a slot reserved concurrently is never thrown away.
                    _registrations.TryRemove(
                        new KeyValuePair<string, List<DateTimeOffset>>(source, registrations));
                }
            }
        }
    }
}

/// <summary>Whether an attempt is allowed, and how long until it would be.</summary>
/// <param name="IsPermitted">Whether the attempt may proceed.</param>
/// <param name="RetryAfterSeconds">
/// Seconds until the caller may try again — a positive number whenever the attempt was refused, so
/// AC-01b's "tells them when they may try again" can always be answered.
/// </param>
/// <param name="ReservedAt">The instant of the slot a permitted attempt holds, so it can be released.</param>
public readonly record struct RateLimitDecision(bool IsPermitted, int RetryAfterSeconds, DateTimeOffset ReservedAt)
{
    public static RateLimitDecision Permitted(DateTimeOffset reservedAt) => new(true, 0, reservedAt);

    public static RateLimitDecision Refused(int retryAfterSeconds) => new(false, retryAfterSeconds, default);
}
