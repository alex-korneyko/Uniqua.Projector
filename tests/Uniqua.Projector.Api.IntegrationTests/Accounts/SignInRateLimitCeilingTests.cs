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

        // 2) The ceiling is logged at error level, naming the per-source limiter and its own
        //    consequence on the same line (review 2026-09-23 fourth re-review V-02): an operator
        //    reading this line must be able to tell which of the two limiters is full, not just
        //    that one of them is, and what not tracking a new source actually means.
        var ceilingLines = CeilingLines(logger);
        Assert.NotEmpty(ceilingLines);
        Assert.Contains(ceilingLines, line => line.Level == LogLevel.Error);
        Assert.Contains(
            ceilingLines,
            line => line.Message.Contains("limiter=sign_in_per_source", StringComparison.Ordinal)
                && line.Message.Contains("consequence=new_sources_uncapped", StringComparison.Ordinal));

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

        // 4) After the clock passes the window, a new source is tracked again. The periodic prune
        //    is due by then, so this does not isolate the forced reclaim; the test further down
        //    does.
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

        // 2) The ceiling is logged at error level, naming the per-address limiter and its own
        //    consequence (review 2026-09-23 fourth re-review V-02): the per-address table filling
        //    does not stop the per-source cap of 100 from still applying, so the line must never
        //    claim the blanket "source_not_rate_limited" both limiters used to share.
        var addressCeilingLines = CeilingLines(logger);
        Assert.Contains(addressCeilingLines, line => line.Level == LogLevel.Error);
        Assert.Contains(
            addressCeilingLines,
            line => line.Message.Contains("limiter=sign_in_per_address", StringComparison.Ordinal)
                && line.Message.Contains(
                    "consequence=pair_uncapped_per_source_cap_still_applies", StringComparison.Ordinal));
        Assert.DoesNotContain(
            addressCeilingLines,
            line => line.Message.Contains("consequence=source_not_rate_limited", StringComparison.Ordinal));

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

        // 4) After the clock passes the window, a new pair is tracked again. The periodic prune
        //    is due by then, so this does not isolate the forced reclaim; the test further down
        //    does.
        clock.Advance(SignInRateLimit.Window + TimeSpan.FromSeconds(1));
        var afterWindow = limit.Reserve("tracked-again-source", "tracked-again@example.test");

        Assert.True(afterWindow.IsPermitted);
        Assert.True(
            limit.TrackedAddressKeyCount < Capacity,
            $"after the window passed, {limit.TrackedAddressKeyCount} pairs were still tracked "
            + $"against a ceiling of {Capacity}; a new pair must be tracked again once expired "
            + "entries are reclaimed.");
    }

    // ---- Review of T56: the forced reclaim, on its own, frees a slot in both limiters -------------

    [Fact]
    public void At_capacity_the_forced_reclaim_alone_frees_a_slot_in_both_limiters_once_the_fill_has_expired()
    {
        // Review of T56: step (4) of the two tests above cannot tell the forced reclaim from the
        // periodic prune — once the clock passes Window from the fill, the periodic prune is due
        // and reclaims every expired entry before the forced path runs. Here the fill has expired
        // but neither limiter's periodic prune is due yet, so only the forced reclaim can free a
        // slot for the new source and the new pair.
        var clock = new TestClock();
        var limit = new SignInRateLimit(clock);
        var window = SignInRateLimit.Window;

        // T0: stamp both limiters' periodic prune, then hand both slots straight back.
        limit.Release(limit.Reserve("stamp-source", "stamp@example.test"));
        Assert.Equal(0, limit.TrackedSourceCount);
        Assert.Equal(0, limit.TrackedAddressKeyCount);

        // T0 + W/2: fill both limiters to capacity.
        clock.Advance(window / 2);
        FillWithDistinctPairs(limit);
        Assert.Equal(Capacity, limit.TrackedSourceCount);
        Assert.Equal(Capacity, limit.TrackedAddressKeyCount);

        // T0 + W + 1s: both periodic prunes are due, run, find nothing of the fill expired yet, and
        // restamp. The pair ("source-0", "fill-0") is already tracked in both limiters, so this
        // reserve never reaches either forced path.
        clock.Advance((window / 2) + TimeSpan.FromSeconds(1));
        Assert.True(limit.Reserve("source-0", "fill-0@example.test").IsPermitted);
        Assert.Equal(Capacity, limit.TrackedSourceCount);
        Assert.Equal(Capacity, limit.TrackedAddressKeyCount);

        // T0 + 1.5W + 1s: every fill entry but source-0's second one has expired, and neither
        // periodic prune is due for another half window.
        clock.Advance(window / 2);
        Assert.True(limit.Reserve("after-the-fill-expired", "after@example.test").IsPermitted);

        // source-0 / its pair (still inside the window) and the new one: tracked, not waved
        // through uncounted.
        Assert.True(
            limit.TrackedSourceCount == 2,
            $"{limit.TrackedSourceCount} sources were tracked after a new source arrived at "
            + "capacity once the whole fill had expired; the per-source limiter's forced reclaim "
            + "must free the expired entries and track the new source, not fail open.");
        Assert.True(
            limit.TrackedAddressKeyCount == 2,
            $"{limit.TrackedAddressKeyCount} pairs were tracked after a new pair arrived at capacity "
            + "once the whole fill had expired; the per-address limiter's forced reclaim must free "
            + "the expired entries and track the new pair, not fail open.");
    }

    // ---- V-03 (review 2026-09-23 fourth re-review): a backwards clock step must not silence -------
    // ---- the forced reclaim or the ceiling alarm ---------------------------------------------------

    [Fact]
    public void A_backwards_clock_step_still_lets_the_next_new_key_trigger_the_forced_reclaim_and_the_ceiling_line()
    {
        // V-03: if the host clock steps back (an NTP correction after a Proxmox VM resumes, for
        // example), "now - last" goes negative. Before the fix that read as "not due" for a whole
        // window, so a limiter already at capacity would turn new sources away uncounted with
        // nothing logged and no forced reclaim run, until real time caught back up.
        var clock = new TestClock();
        var logger = new RecordingLogger<SignInRateLimit>();
        var limit = new SignInRateLimit(clock, logger);

        FillWithDistinctPairs(limit);
        Assert.Equal(Capacity, limit.TrackedSourceCount);

        // Stamp both throttles once at capacity, then note how many ceiling lines exist so far and
        // how many forced reclaims the per-source limiter has run. This first reserve is itself the
        // one that stamps the forced-prune throttle at T0, exactly as the fill loop above never
        // does (it never sees TrackedSourceCount >= Capacity until the very last insert).
        limit.Reserve("first-over-capacity", "first-over-capacity@example.test");
        var linesBeforeStep = CeilingLines(logger).Count;
        var forcedPrunesBeforeStep = limit.PerSourceForcedPruneCount;
        Assert.True(linesBeforeStep >= 1);
        Assert.Equal(1, forcedPrunesBeforeStep);

        // The clock steps back an hour — well past both the one-second forced-prune interval and
        // the 15-minute window, so if a negative elapsed time were still read as "not due", nothing
        // would fire again for two more real windows.
        clock.Advance(TimeSpan.FromHours(-1));

        var afterStep = limit.Reserve("after-the-backwards-step", "after-the-backwards-step@example.test");

        Assert.True(afterStep.IsPermitted);

        // review of T64 (fourth re-review V-03, follow-up finding on the first fix): the ceiling
        // line alone does not distinguish the forced reclaim from LogCeilingReachedIfDue's own,
        // separately-throttled check — a regression that reverted ForcePruneIfDue's use of
        // IsWithinInterval, but left LogCeilingReachedIfDue's fixed, would still pass an
        // assertion on the ceiling line count alone. Asserting the forced-prune count directly
        // pins the forced reclaim itself, not just its neighbour.
        Assert.True(
            limit.PerSourceForcedPruneCount > forcedPrunesBeforeStep,
            "a backwards clock step silenced the per-source limiter's forced reclaim instead of "
            + "the negative elapsed time being treated as due.");
        Assert.True(
            CeilingLines(logger).Count > linesBeforeStep,
            "a backwards clock step silenced the ceiling alarm instead of the negative elapsed "
            + "time being treated as due.");
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
