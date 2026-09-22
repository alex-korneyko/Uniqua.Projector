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
/// A sliding window of individual attempt instants, rather than a fixed bucket: a fixed bucket
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

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _attempts = new();

    /// <summary>
    /// Whether this source may register now, and if not, how long until it may.
    /// </summary>
    /// <remarks>
    /// A refused attempt is deliberately <em>not</em> recorded. Counting refusals would let a
    /// caller who keeps retrying push their own wait further and further out, which punishes the
    /// impatient rather than the abusive.
    /// </remarks>
    public RateLimitDecision Check(string source)
    {
        var now = clock.UtcNow;
        var windowOpenedAt = now - Window;

        var attempts = _attempts.GetOrAdd(source, _ => []);

        lock (attempts)
        {
            attempts.RemoveAll(attempt => attempt <= windowOpenedAt);

            if (attempts.Count < PermittedPerWindow)
            {
                attempts.Add(now);
                return RateLimitDecision.Permitted;
            }

            // The wait until the oldest attempt in the window drops out of it, which is the first
            // instant this caller will genuinely be allowed to try again. Rounded up, so a caller
            // that obeys it is never refused a second time for being a fraction early.
            var until = attempts.Min() + Window - now;
            var seconds = Math.Max(1, (int)Math.Ceiling(until.TotalSeconds));

            return RateLimitDecision.Refused(seconds);
        }
    }

    /// <summary>
    /// Forgets sources whose window has closed, so a long-running instance does not accumulate one
    /// entry per address that ever registered.
    /// </summary>
    public void Prune()
    {
        var windowOpenedAt = clock.UtcNow - Window;

        foreach (var (source, attempts) in _attempts)
        {
            lock (attempts)
            {
                attempts.RemoveAll(attempt => attempt <= windowOpenedAt);
                if (attempts.Count is 0)
                {
                    _attempts.TryRemove(source, out _);
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
public readonly record struct RateLimitDecision(bool IsPermitted, int RetryAfterSeconds)
{
    public static readonly RateLimitDecision Permitted = new(true, 0);

    public static RateLimitDecision Refused(int retryAfterSeconds) => new(false, retryAfterSeconds);
}
