using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// A cap on failed sign-ins, keyed by (request source, normalised email address) with a looser
/// ceiling per source alone across every address — review 2026-09-22-2 N-01 and review 2026-09-23
/// P-01, ADR 0010's amendments of those dates. AC-12's progressive per-account delay only slows a
/// client that waits for its own response; one that hangs up as soon as it has verified whether the
/// account exists pays none of that delay and learns the answer anyway, and several such clients in
/// parallel each pay their own wait independently, so the delay alone does not make guessing futile
/// against a client that behaves this way. This cap does: past 20 failed sign-ins for one (source,
/// address) pair in 15 minutes, the next attempt against that pair is refused before a password is
/// even checked; past 100 failed sign-ins from one source across every address it has tried in the
/// same window, the next attempt from that source is refused regardless of which address it names.
/// </summary>
/// <remarks>
/// <para>
/// Both slots are reserved <em>before</em> <c>SignIn</c> runs — before the password is verified at
/// all — and are released only when the sign-in succeeds. A failure, a refusal, an exception or a
/// client that simply hangs up all leave the slots reserved, because every one of those is exactly
/// the case AC-12's delay alone does not hold back. This is the mirror image of
/// <see cref="RegistrationRateLimit"/>, which releases on failure and keeps the slot on success;
/// the two share <see cref="SlidingWindowLimiter"/> for the bookkeeping and differ only in which
/// outcome they call <see cref="Release"/> for.
/// </para>
/// <para>
/// Applies identically whether the address is registered or not (AC-05b): both keys are built from
/// request source and address alone, never from anything the account store decides, so this adds no
/// second oracle.
/// </para>
/// <para>
/// Keying by (source, address) rather than by source alone (review 2026-09-23 P-01) is what lets a
/// correct password for a different address still succeed from a source that has capped one address
/// under attack — AC-12's "never unusable to its owner", read as "never by anyone who does not share
/// the owner's request source" (spec §5). The per-source ceiling still bounds a guesser who rotates
/// across many addresses from one source to route around the per-address cap. When
/// <see cref="RequestSource.Of"/> resolves to <see cref="RequestSource.Unknown"/> — no
/// <c>RemoteIpAddress</c> to key on — neither cap is applied at all: keying on the literal string
/// "unknown" would give every caller with no resolvable address one shared budget, which is worse
/// than not capping them.
/// </para>
/// <para>
/// Residual risk, recorded in spec §6.1 and ADR 0010's amendment: a guesser who shares the owner's
/// request source and targets the owner's own address, or a source that has reached its own
/// per-source ceiling, can still refuse the owner for up to 15 minutes.
/// </para>
/// </remarks>
public sealed class SignInRateLimit
{
    /// <summary>The per-(source, address) figure named in ADR 0010's amendment and spec §6.</summary>
    public const int PermittedFailuresPerWindow = 20;

    /// <summary>
    /// The looser per-source ceiling across all addresses (review 2026-09-23 P-01). Bounds how far
    /// one source can rotate across addresses to route around the per-address cap above.
    /// </summary>
    public const int PermittedFailuresPerSourceWindow = 100;

    /// <summary>The sliding window both figures above are counted over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    // A control character that cannot appear in a request source (an IP address or
    // RequestSource.Unknown) or survive email normalisation, so a (source, address) compound key
    // can never collide with a different pair across the boundary between the two parts.
    private const char KeySeparator = '␟';

    private readonly SlidingWindowLimiter _perAddress;
    private readonly SlidingWindowLimiter _perSource;
    private readonly UpperInvariantLookupNormalizer _normalizer = new();

    public SignInRateLimit(IClock clock, ILogger<SignInRateLimit> logger)
    {
        // V-02: the per-address limiter filling does not stop the per-source cap from still
        // applying to that source, so its consequence says exactly that rather than the blanket
        // "source_not_rate_limited" both limiters used to share.
        _perAddress = new SlidingWindowLimiter(
            clock,
            PermittedFailuresPerWindow,
            Window,
            logger,
            "sign_in_per_address",
            "pair_uncapped_per_source_cap_still_applies");
        _perSource = new SlidingWindowLimiter(
            clock, PermittedFailuresPerSourceWindow, Window, logger, "sign_in_per_source", "new_sources_uncapped");
    }

    /// <summary>For tests, which have no DI container to source a logger from.</summary>
    public SignInRateLimit(IClock clock)
        : this(clock, NullLogger<SignInRateLimit>.Instance)
    {
    }

    /// <summary>How many sources currently hold a slot. For tests and for anyone watching memory.</summary>
    public int TrackedSourceCount => _perSource.TrackedSourceCount;

    /// <summary>
    /// How many (source, address) keys currently hold a slot. For tests and for anyone watching
    /// memory (review 2026-09-23 (third re-review) S-01).
    /// </summary>
    public int TrackedAddressKeyCount => _perAddress.TrackedSourceCount;

    /// <summary>
    /// Holds this (source, address) pair's slot and this source's own slot for a sign-in attempt
    /// about to be verified, or says how long until one frees up. Called before
    /// <c>SignIn.ExecuteAsync</c>, so a refusal here never touches the password.
    /// </summary>
    /// <param name="source">The request source <see cref="RequestSource.Of"/> resolved.</param>
    /// <param name="email">
    /// The address as submitted; normalised here exactly as the account store normalises it, so one
    /// address typed two ways is one key here just as it is one account there.
    /// </param>
    public SignInReservation Reserve(string source, string email)
    {
        if (source == RequestSource.Unknown)
        {
            // review 2026-09-23 P-01: neither cap is applied when the source cannot be resolved —
            // keying on the literal string "unknown" would make every such caller share one budget.
            return SignInReservation.SkippedCap();
        }

        // review 2026-09-23 (third re-review) S-01: the per-source ceiling is checked first, so a
        // source already refused there never creates a per-address entry at all — a flood of
        // brand-new addresses from a capped source would otherwise grow _perAddress without bound,
        // one hashed key per attempt, however cheap each attempt was to send.
        var sourceDecision = _perSource.Reserve(source);
        if (!sourceDecision.IsPermitted)
        {
            return SignInReservation.Refused(sourceDecision.RetryAfterSeconds, SignInRateLimitCap.PerSource);
        }

        var addressKey = CompoundKey(source, email);
        var addressDecision = _perAddress.Reserve(addressKey);
        if (!addressDecision.IsPermitted)
        {
            // The per-source slot just reserved must not outlive an attempt the per-address cap
            // refuses on its own.
            _perSource.Release(source, sourceDecision);
            return SignInReservation.Refused(addressDecision.RetryAfterSeconds, SignInRateLimitCap.PerAddress);
        }

        return SignInReservation.Permitted(source, addressKey, sourceDecision, addressDecision);
    }

    /// <summary>
    /// Hands back both slots <see cref="Reserve"/> took, because the attempt they were held for
    /// succeeded — a correct password is never counted against either cap, from any source (AC-12).
    /// A no-op for a reservation <see cref="Reserve"/> skipped, since nothing was taken.
    /// </summary>
    public void Release(SignInReservation reservation)
    {
        if (!reservation.IsPermitted || reservation.Source is null)
        {
            return;
        }

        _perAddress.Release(reservation.AddressKey!, reservation.AddressDecision);
        _perSource.Release(reservation.Source, reservation.SourceDecision);
    }

    // review 2026-09-23 (third re-review) S-01: hashed rather than plain, and to a fixed length,
    // exactly as InMemoryUnknownAddressAttempts.Key hashes the same normalised value — a key's
    // length no longer grows with what was submitted, so a huge address is as cheap to hold a slot
    // for as a short one, and no submitted address is ever held in plain text.
    private string CompoundKey(string source, string email)
    {
        var normalized = _normalizer.NormalizeEmail(email.Trim()) ?? string.Empty;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return $"{source}{KeySeparator}{hash}";
    }
}

/// <summary>
/// The outcome of <see cref="SignInRateLimit.Reserve"/>: whether the attempt may proceed, what to
/// say if it may not, and — if it may — what to release back on success.
/// </summary>
/// <param name="IsPermitted">Whether the attempt may proceed.</param>
/// <param name="RetryAfterSeconds">
/// Seconds until the caller may try again — a positive number whenever the attempt was refused.
/// </param>
/// <param name="Skipped">
/// Whether the cap was not applied at all because the request source was unresolved
/// (review 2026-09-23 P-01). Always <c>true</c> here also means <see cref="IsPermitted"/>, but
/// carries no slot to release.
/// </param>
/// <param name="Source">The request source the reservation was made against, or <c>null</c> if refused or skipped.</param>
/// <param name="AddressKey">The compound key the per-address slot was reserved under, or <c>null</c> if refused or skipped.</param>
/// <param name="SourceDecision">The per-source slot to hand back on <see cref="SignInRateLimit.Release"/>.</param>
/// <param name="AddressDecision">The per-address slot to hand back on <see cref="SignInRateLimit.Release"/>.</param>
/// <param name="Cap">
/// Which cap refused the attempt (review 2026-09-23 (third re-review) S-04), or <c>null</c> if the
/// attempt was permitted or the cap was skipped. Logged so sad §7's per-cap breakdown can be built
/// from the logs; never put in the 429 body, which stays silent about which cap fired.
/// </param>
public readonly record struct SignInReservation(
    bool IsPermitted,
    int RetryAfterSeconds,
    bool Skipped,
    string? Source,
    string? AddressKey,
    RateLimitDecision SourceDecision,
    RateLimitDecision AddressDecision,
    SignInRateLimitCap? Cap)
{
    public static SignInReservation Permitted(
        string source, string addressKey, RateLimitDecision sourceDecision, RateLimitDecision addressDecision) =>
        new(true, 0, false, source, addressKey, sourceDecision, addressDecision, null);

    public static SignInReservation Refused(int retryAfterSeconds, SignInRateLimitCap cap) =>
        new(false, retryAfterSeconds, false, null, null, default, default, cap);

    public static SignInReservation SkippedCap() => new(true, 0, true, null, null, default, default, null);
}

/// <summary>
/// Which of <see cref="SignInRateLimit"/>'s two caps refused a sign-in attempt
/// (review 2026-09-23 (third re-review) S-04).
/// </summary>
public enum SignInRateLimitCap
{
    /// <summary>The tighter per-(source, address) pair cap, <see cref="SignInRateLimit.PermittedFailuresPerWindow"/>.</summary>
    PerAddress,

    /// <summary>The looser per-source ceiling across every address, <see cref="SignInRateLimit.PermittedFailuresPerSourceWindow"/>.</summary>
    PerSource,
}
