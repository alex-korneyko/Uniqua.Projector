using Microsoft.Extensions.Logging.Abstractions;
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
/// next bucket rather than the time until the caller is actually allowed to try. The bookkeeping is
/// <see cref="SlidingWindowLimiter"/>, shared with <see cref="SignInRateLimit"/> (review
/// 2026-09-22-2 N-01).
/// </para>
/// </remarks>
public sealed class RegistrationRateLimit
{
    /// <summary>AC-01b's figure.</summary>
    public const int PermittedPerWindow = 5;

    /// <summary>AC-01b's "within the past minute".</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly SlidingWindowLimiter _limiter;

    public RegistrationRateLimit(IClock clock, ILogger<RegistrationRateLimit> logger)
    {
        _limiter = new SlidingWindowLimiter(clock, PermittedPerWindow, Window, logger);
    }

    /// <summary>For tests, which have no DI container to source a logger from.</summary>
    public RegistrationRateLimit(IClock clock)
        : this(clock, NullLogger<RegistrationRateLimit>.Instance)
    {
    }

    /// <summary>How many sources currently hold a slot. For tests and for anyone watching memory.</summary>
    public int TrackedSourceCount => _limiter.TrackedSourceCount;

    /// <summary>
    /// How many forced reclaims the limiter has run at its tracked-source ceiling. For tests, so
    /// the throttle on that full scan is observable (review of T55).
    /// </summary>
    public long ForcedPruneCount => _limiter.ForcedPruneCount;

    /// <summary>
    /// Holds one of this source's slots for a registration about to be attempted, or says how long
    /// until one frees up.
    /// </summary>
    /// <remarks>
    /// The slot is taken <em>before</em> the use case runs, so six concurrent submissions from one
    /// source cannot all pass a check none of them has yet recorded. A registration the use case
    /// refuses hands its slot back through <see cref="Release"/>: AC-01b counts accounts that were
    /// registered, and a visitor correcting a refused submission has registered nothing.
    /// </remarks>
    public RateLimitDecision Reserve(string source) => _limiter.Reserve(source);

    /// <summary>
    /// Hands back the slot <see cref="Reserve"/> took, because the registration it was held for
    /// did not create an account.
    /// </summary>
    public void Release(string source, RateLimitDecision reservation) => _limiter.Release(source, reservation);
}
