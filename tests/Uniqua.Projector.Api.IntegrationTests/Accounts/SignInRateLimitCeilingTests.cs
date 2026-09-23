using Microsoft.Extensions.Logging;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// review 2026-09-23 (third re-review) T-04, S-02 — the old ceiling test
/// (<see cref="RequestSourceTests"/>, before this fix) asserted only that
/// <c>TrackedSourceCount &lt;= 100_000</c>, a bound that would still hold if the limiter refused
/// new keys at the ceiling (failing closed, against spec §6.1), if the forced reclaim were
/// deleted, or if the ceiling were never logged. <see cref="SignInRateLimit"/> is built on two
/// independent <c>SlidingWindowLimiter</c> instances — per (source, address) pair and per source
/// alone — and S-02 named a real gap: no test drove the per-address limiter to its own ceiling at
/// all, so nothing pinned that a source already refused by its own per-source ceiling of 100 stays
/// refused there, rather than slipping through the per-address limiter's fail-open path once that
/// limiter is separately full.
/// </summary>
public sealed class SignInRateLimitCeilingTests
{
    private const int Capacity = 100_000;

    // ---- Both limiters: fail-open for a brand-new key, refusal still holds for a tracked one ------

    [Fact]
    public void At_capacity_the_per_source_limiter_fails_open_for_a_new_source_but_still_refuses_a_tracked_one()
    {
        var clock = new TestClock();
        var logger = new RecordingLogger<SignInRateLimit>();
        var limit = new SignInRateLimit(clock, logger);

        // One reservation per distinct source and address fills both the per-source limiter and
        // the per-address limiter to their shared Capacity of 100,000 tracked keys at once, and
        // leaves every one of the 100,000 sources used here far under its own per-source ceiling
        // of 100.
        FillWithDistinctPairs(limit);
        Assert.Equal(Capacity, limit.TrackedSourceCount);
        Assert.Equal(Capacity, limit.TrackedAddressKeyCount);

        // 1) A brand-new source is still permitted, and left uncounted: the ceiling's fail-open
        //    path, not a new tracked entry.
        var newSourceReservation = limit.Reserve("brand-new-source", "brand-new@example.test");
        Assert.True(newSourceReservation.IsPermitted);
        Assert.Equal(Capacity, limit.TrackedSourceCount);

        // 2) The ceiling is logged at error level.
        var ceilingLines = CeilingLines(logger);
        Assert.NotEmpty(ceilingLines);
        Assert.Contains(ceilingLines, line => line.Level == LogLevel.Error);

        // 3) A source already tracked ("source-0", from the fill above, with one reservation
        //    already counted against it) is still refused once it exceeds its own per-source
        //    ceiling of 100 — the fail-open path proven in (1) must apply only to a source that
        //    was never tracked at all. Every one of these spends a brand-new address too, exactly
        //    the shape the per-address limiter's own fail-open path (also at capacity here) would
        //    otherwise wave through; the per-source ceiling must still catch it first.
        const string trackedSource = "source-0";
        for (var attempt = 1; attempt < SignInRateLimit.PermittedFailuresPerSourceWindow; attempt++)
        {
            var reservation = limit.Reserve(trackedSource, $"spend-{attempt}@example.test");
            Assert.True(reservation.IsPermitted);
        }

        var overLimit = limit.Reserve(trackedSource, "one-too-many@example.test");

        Assert.False(
            overLimit.IsPermitted,
            "a source already tracked at its own per-source ceiling of "
            + $"{SignInRateLimit.PermittedFailuresPerSourceWindow} was still permitted a brand-new "
            + "address once the per-address limiter separately sat at its own tracked-key "
            + "capacity; the per-source ceiling (spec §6.1) must still bound it.");
        Assert.Equal(SignInRateLimitCap.PerSource, overLimit.Cap);

        // 4) After the clock passes the window, a new source is tracked again.
        clock.Advance(SignInRateLimit.Window + TimeSpan.FromSeconds(1));
        var afterWindow = limit.Reserve("tracked-again-source", "tracked-again@example.test");

        Assert.True(afterWindow.IsPermitted);
        Assert.True(
            limit.TrackedSourceCount < Capacity,
            $"after the window passed, {limit.TrackedSourceCount} sources were still tracked "
            + $"against a ceiling of {Capacity}; a new source must be tracked again once expired "
            + "entries are reclaimed.");
    }

    // ---- The per-address limiter specifically, at its own ceiling ----------------------------------

    [Fact]
    public void At_capacity_the_per_address_limiter_fails_open_for_a_new_pair_but_still_refuses_a_tracked_one()
    {
        var clock = new TestClock();
        var logger = new RecordingLogger<SignInRateLimit>();
        var limit = new SignInRateLimit(clock, logger);

        FillWithDistinctPairs(limit);
        Assert.Equal(Capacity, limit.TrackedAddressKeyCount);

        // 1) A brand-new pair, from a brand-new source, is still permitted and left uncounted.
        var newPairReservation = limit.Reserve("brand-new-source", "brand-new@example.test");
        Assert.True(newPairReservation.IsPermitted);
        Assert.Equal(Capacity, limit.TrackedAddressKeyCount);

        // 2) The ceiling is logged at error level.
        Assert.Contains(CeilingLines(logger), line => line.Level == LogLevel.Error);

        // 3) The exact pair reserved first in the fill above ("source-0", "fill-0@example.test")
        //    is already tracked. Spending the rest of its own per-address limit of 20 keeps
        //    source-0 well inside its separate per-source budget of 100, so this exercises the
        //    per-address limit specifically, not the per-source one.
        const string trackedSource = "source-0";
        const string trackedAddress = "fill-0@example.test";
        for (var attempt = 1; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            var reservation = limit.Reserve(trackedSource, trackedAddress);
            Assert.True(reservation.IsPermitted);
        }

        var overLimit = limit.Reserve(trackedSource, trackedAddress);

        Assert.False(
            overLimit.IsPermitted,
            "an already-tracked (source, address) pair was still permitted past its own "
            + $"{SignInRateLimit.PermittedFailuresPerWindow}-per-window limit while the per-address "
            + "limiter sat at its 100,000-pair capacity; the ceiling's fail-open path must apply "
            + "only to a pair that isn't tracked yet.");
        Assert.Equal(SignInRateLimitCap.PerAddress, overLimit.Cap);

        // 4) After the clock passes the window, a new pair is tracked again.
        clock.Advance(SignInRateLimit.Window + TimeSpan.FromSeconds(1));
        var afterWindow = limit.Reserve("tracked-again-source", "tracked-again@example.test");

        Assert.True(afterWindow.IsPermitted);
        Assert.True(
            limit.TrackedAddressKeyCount < Capacity,
            $"after the window passed, {limit.TrackedAddressKeyCount} pairs were still tracked "
            + $"against a ceiling of {Capacity}; a new pair must be tracked again once expired "
            + "entries are reclaimed.");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static void FillWithDistinctPairs(SignInRateLimit limit)
    {
        for (var i = 0; i < Capacity; i++)
        {
            var reservation = limit.Reserve($"source-{i}", $"fill-{i}@example.test");
            Assert.True(reservation.IsPermitted);
        }
    }

    private static List<RecordingLogger<SignInRateLimit>.Entry> CeilingLines(
        RecordingLogger<SignInRateLimit> logger) =>
        [.. logger.Entries.Where(entry => entry.Message.Contains(
            "tracked_source_ceiling_reached", StringComparison.Ordinal))];
}
