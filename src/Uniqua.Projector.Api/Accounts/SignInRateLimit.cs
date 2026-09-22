using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// A cap on failed sign-ins per request source — review 2026-09-22-2 N-01, ADR 0010's amendment of
/// the same date. AC-12's progressive per-account delay only slows a client that waits for its
/// response; one that hangs up as soon as it has verified whether the account exists pays none of
/// that delay and learns the answer anyway, and several such clients in parallel each pay their own
/// wait independently, so the delay alone does not make guessing futile against a client that
/// behaves this way. This cap does: past 20 failed sign-ins from one source in 15 minutes, the next
/// attempt is refused before a password is even checked.
/// </summary>
/// <remarks>
/// <para>
/// The slot is reserved <em>before</em> <c>SignIn</c> runs — before the password is verified at
/// all — and is released only when the sign-in succeeds. A failure, a refusal, an exception or a
/// client that simply hangs up all leave the slot reserved, because every one of those is exactly
/// the case AC-12's delay alone does not hold back. This is the mirror image of
/// <see cref="RegistrationRateLimit"/>, which releases on failure and keeps the slot on success;
/// the two share <see cref="SlidingWindowLimiter"/> for the bookkeeping and differ only in which
/// outcome they call <see cref="Release"/> for.
/// </para>
/// <para>
/// Applies identically whether the address is registered or not (AC-05b): the reservation is keyed
/// by request source, never by account or by address, so it adds no second oracle.
/// </para>
/// <para>
/// Residual risk, recorded in spec §6.1 and ADR 0010's amendment: one account attacked from many
/// request sources is not slowed by this cap, which is a per-source counter rather than a
/// per-account one.
/// </para>
/// </remarks>
public sealed class SignInRateLimit
{
    /// <summary>The figure named in ADR 0010's amendment and spec §6.</summary>
    public const int PermittedFailuresPerWindow = 20;

    /// <summary>The sliding window the figure above is counted over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly SlidingWindowLimiter _limiter;

    public SignInRateLimit(IClock clock)
    {
        _limiter = new SlidingWindowLimiter(clock, PermittedFailuresPerWindow, Window);
    }

    /// <summary>How many sources currently hold a slot. For tests and for anyone watching memory.</summary>
    public int TrackedSourceCount => _limiter.TrackedSourceCount;

    /// <summary>
    /// Holds one of this source's slots for a sign-in attempt about to be verified, or says how
    /// long until one frees up. Called before <c>SignIn.ExecuteAsync</c>, so a refusal here never
    /// touches the password.
    /// </summary>
    public RateLimitDecision Reserve(string source) => _limiter.Reserve(source);

    /// <summary>
    /// Hands back the slot <see cref="Reserve"/> took, because the attempt it was held for
    /// succeeded — a correct password is never counted against the cap, from any source (AC-12).
    /// </summary>
    public void Release(string source, RateLimitDecision reservation) => _limiter.Release(source, reservation);
}
