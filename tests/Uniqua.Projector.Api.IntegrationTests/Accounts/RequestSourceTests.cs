using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// review 2026-09-23 P-02 — <c>RequestSource.Of</c> keyed the full IPv6 address, so a client
/// holding a /64 (the block an ISP hands one customer) got a fresh budget for every address in it,
/// and the plain and IPv4-mapped forms of one IPv4 client (<c>203.0.113.7</c> and
/// <c>::ffff:203.0.113.7</c>) were two different keys. Both bypass AC-01b's per-source registration
/// cap and AC-12's per-source sign-in ceiling. This file pins the fix: an IPv4-mapped address maps
/// to its IPv4 form, and any other IPv6 address is keyed by its /64 prefix — for both limits, since
/// both are built on <see cref="RequestSource.Of"/>.
/// </summary>
public sealed class RequestSourceTests
{
    // ---- AC-12, AC-01b: IPv6 addresses in one /64 share a source key -----------------------------

    [Fact]
    public void Two_addresses_in_one_slash64_produce_the_same_source_key()
    {
        var first = SourceOf("2001:db8:1234:5678:aaaa:bbbb:cccc:0001");
        var second = SourceOf("2001:db8:1234:5678:ffff:0000:1111:2222");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Two_different_slash64_prefixes_produce_different_source_keys()
    {
        // The /64 boundary sits in the fourth hextet: ...5678 vs ...5679.
        var first = SourceOf("2001:db8:1234:5678::1");
        var second = SourceOf("2001:db8:1234:5679::1");

        Assert.NotEqual(first, second);
    }

    // ---- P-02: an IPv4-mapped IPv6 address and the plain IPv4 form of the same client -------------

    [Fact]
    public void An_ipv4_mapped_address_and_its_plain_ipv4_form_share_a_source_key()
    {
        var mapped = SourceOf("::ffff:203.0.113.7");
        var plain = SourceOf("203.0.113.7");

        Assert.Equal(plain, mapped);
    }

    // ---- P-02: a hard ceiling on how many sources one limiter tracks at once -----------------------

    [Fact]
    public void The_limiter_stops_growing_once_it_holds_the_named_ceiling_of_tracked_sources()
    {
        // InMemoryUnknownAddressAttempts (review 2026-09-22-2 N-03, spec §6.1) accepts exactly this
        // shape of residual risk at a ceiling of 100,000 tracked entries; P-02 asks for the matching
        // ceiling here, because until now only lazy pruning bounded _entries at all, and IPv6
        // address rotation makes that unbounded within a single window.
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);

        const int flood = 100_010;
        for (var source = 0; source < flood; source++)
        {
            limit.Reserve($"source-{source}");
        }

        Assert.True(
            limit.TrackedSourceCount <= 100_000,
            $"the limiter tracked {limit.TrackedSourceCount} distinct sources in one window, past "
            + "the 100,000 ceiling review 2026-09-23 P-02 asks for — a flood of distinct keys (IPv6 "
            + "rotation among them) must not grow this without bound.");
    }

    // ---- T-01 (review 2026-09-23 third re-review): the forced prune and the ceiling log line ------
    // ---- are both throttled once the limiter sits at capacity -------------------------------------

    [Fact]
    public void At_capacity_a_flood_of_new_keys_within_one_interval_forces_one_prune_and_logs_one_line()
    {
        // T-01: before the fix, every one of these 1,000 new-key attempts at capacity ran its own
        // unthrottled forced PruneExpired over all 100,000 tracked entries and wrote its own
        // tracked_source_ceiling_reached line. The fix claims the forced prune through a
        // compare-and-set like PruneIfDue's, at most once per a short named interval, and logs the
        // ceiling at most once per window — carrying how many keys went untracked since the last
        // line — rather than once per request.
        var clock = new TestClock();
        var logger = new RecordingLogger<RegistrationRateLimit>();
        var limit = new RegistrationRateLimit(clock, logger);

        FillToCapacity(limit);
        Assert.Equal(0, limit.ForcedPruneCount);

        const int flood = 1_000;
        for (var source = 0; source < flood; source++)
        {
            var reservation = limit.Reserve($"flood-{source}");

            // Fail-open at the ceiling is unchanged: a new key past capacity is still permitted,
            // just not tracked.
            Assert.True(reservation.IsPermitted);
        }

        Assert.Equal(Capacity, limit.TrackedSourceCount);

        // Exactly one, not "at most one": zero would mean the forced reclaim no longer runs before
        // a new key is turned away, and more than one would mean it is not throttled.
        Assert.True(
            limit.ForcedPruneCount == 1,
            $"{limit.ForcedPruneCount} forced prunes ran for {flood} new keys inside one interval; "
            + "T-01 asks for exactly one full scan per interval, not one per request.");

        var ceilingLines = CeilingLines(logger);
        Assert.True(
            ceilingLines.Count == 1,
            $"{ceilingLines.Count} tracked_source_ceiling_reached lines were logged for {flood} new "
            + "keys inside one window; T-01 asks for exactly one line per window.");
        Assert.Equal(LogLevel.Error, ceilingLines[0].Level);
        Assert.Equal(1, UntrackedSinceLastLine(ceilingLines[0]));
    }

    [Fact]
    public void At_capacity_the_ceiling_line_is_logged_once_per_window_carrying_the_count_turned_away()
    {
        // Review of T55, finding 3: the ceiling line is throttled on the limiter's own window, not
        // on the one-second forced-prune interval — otherwise a limiter kept full writes up to 60
        // lines per registration window and 900 per sign-in window.
        var clock = new TestClock();
        var logger = new RecordingLogger<RegistrationRateLimit>();
        var limit = new RegistrationRateLimit(clock, logger);

        FillToCapacity(limit);

        const int flood = 1_000;
        for (var source = 0; source < flood; source++)
        {
            limit.Reserve($"flood-{source}");
        }

        Assert.Single(CeilingLines(logger));

        // Well past the forced-prune interval but still inside the window: the forced prune may
        // run again, yet no second ceiling line is written.
        clock.Advance(RegistrationRateLimit.Window / 2);
        limit.Reserve("inside-the-window");

        Assert.True(
            CeilingLines(logger).Count == 1,
            $"{CeilingLines(logger).Count} ceiling lines were logged inside one window; T-01 asks "
            + "for at most one line per window.");

        // Keep the limiter full past the window: every fill key reserves a second, later slot, so
        // its list survives the prune that drops the first.
        for (var source = 0; source < Capacity; source++)
        {
            limit.Reserve($"fill-{source}");
        }

        clock.Advance((RegistrationRateLimit.Window / 2) + TimeSpan.FromSeconds(1));
        limit.Reserve("past-the-window");

        Assert.Equal(Capacity, limit.TrackedSourceCount);

        var ceilingLines = CeilingLines(logger);
        Assert.Equal(2, ceilingLines.Count);
        Assert.Equal(LogLevel.Error, ceilingLines[1].Level);

        // Every key turned away after the first line: the rest of the flood, the one inside the
        // window, and the one that triggered this line.
        Assert.Equal((flood - 1) + 1 + 1, UntrackedSinceLastLine(ceilingLines[1]));
    }

    // ---- Review of T55, finding 1: the tracked count does not drift under racing first reserves ---

    [Fact]
    public void Racing_first_reserves_of_the_same_new_keys_count_each_key_exactly_once()
    {
        // ConcurrentDictionary.GetOrAdd can run its factory on several threads for one absent key
        // and keep only one result; incrementing the count on the factory's say-so therefore
        // counted every lost race as one more tracked source, for good. Several threads walk the
        // same fresh keys in the same order at once, so their first reserves of each key collide.
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);

        const int keys = 10_000;
        var racers = Math.Max(4, Environment.ProcessorCount);
        using var start = new Barrier(racers);

        var threads = Enumerable.Range(0, racers)
            .Select(_ => new Thread(() =>
            {
                start.SignalAndWait();
                for (var key = 0; key < keys; key++)
                {
                    limit.Reserve($"racing-{key}");
                }
            }))
            .ToList();

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        // Nothing was released and the clock never moved, so every one of the keys is still held.
        Assert.True(
            limit.TrackedSourceCount == keys,
            $"TrackedSourceCount is {limit.TrackedSourceCount} after {racers} threads raced first "
            + $"reserves of {keys} distinct keys; it must equal the {keys} keys actually held.");
    }

    // ---- T-04 (review 2026-09-23 third re-review): the old ceiling test asserted only ------------
    // ---- TrackedSourceCount <= 100_000, which would still pass if the limiter refused an ---------
    // ---- already-tracked key just because the limiter as a whole sits at capacity -----------------

    [Fact]
    public void At_capacity_a_key_already_tracked_is_still_refused_once_it_exceeds_its_own_limit()
    {
        // T-04: the ceiling's fail-open path (above) must apply only to a source that was never
        // tracked at all. A source that *is* tracked must still be held to its own
        // PermittedPerWindow, even while the limiter as a whole sits at its 100,000 ceiling —
        // otherwise the ceiling would double as a way to dodge the per-source limit once the
        // limiter happens to be full.
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);

        FillToCapacity(limit);

        // "fill-0" is already tracked, with one reservation already counted against it. Spend
        // the rest of its own per-window allowance.
        for (var attempt = 1; attempt < RegistrationRateLimit.PermittedPerWindow; attempt++)
        {
            var reservation = limit.Reserve("fill-0");
            Assert.True(reservation.IsPermitted);
        }

        var overLimit = limit.Reserve("fill-0");

        Assert.False(
            overLimit.IsPermitted,
            "an already-tracked key was still permitted past its own "
            + $"{RegistrationRateLimit.PermittedPerWindow}-per-window limit while the limiter sat "
            + "at its 100,000-source capacity; the ceiling's fail-open path must apply only to a "
            + "source that isn't tracked yet.");
    }

    [Fact]
    public void A_key_is_tracked_again_once_the_clock_passes_the_window()
    {
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);

        FillToCapacity(limit);
        const int capacity = Capacity;

        var atCeiling = limit.Reserve("still-at-ceiling");
        Assert.True(atCeiling.IsPermitted);
        Assert.Equal(capacity, limit.TrackedSourceCount);

        // The periodic prune is due by now, so this does not isolate the forced reclaim; the test
        // below does.
        clock.Advance(RegistrationRateLimit.Window + TimeSpan.FromSeconds(1));

        var afterWindow = limit.Reserve("tracked-again");
        Assert.True(afterWindow.IsPermitted);

        Assert.True(
            limit.TrackedSourceCount < capacity,
            $"after the window passed, {limit.TrackedSourceCount} sources were still tracked "
            + $"against a ceiling of {capacity}; a new key must be tracked again once expired "
            + "entries are reclaimed.");
    }

    // ---- Review of T56: the forced reclaim, on its own, frees a slot at the ceiling ---------------

    [Fact]
    public void At_capacity_the_forced_reclaim_alone_frees_a_slot_once_the_fill_has_expired()
    {
        // Review of T56: the check above cannot tell the forced reclaim from the periodic prune —
        // once the clock passes Window from the fill, the periodic prune is due and reclaims every
        // expired entry before the forced path runs. Here the fill has expired but the periodic
        // prune is not due yet, so only the forced reclaim can free a slot for the new key.
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);
        var window = RegistrationRateLimit.Window;

        // T0: stamp the periodic prune, then hand the slot straight back so it tracks nothing.
        limit.Release("stamp", limit.Reserve("stamp"));
        Assert.Equal(0, limit.TrackedSourceCount);

        // T0 + W/2: fill to capacity.
        clock.Advance(window / 2);
        FillToCapacity(limit);

        // T0 + W + 1s: the periodic prune is due, runs, finds nothing of the fill expired yet, and
        // restamps. "fill-0" is already tracked, so this reserve never reaches the forced path.
        clock.Advance((window / 2) + TimeSpan.FromSeconds(1));
        Assert.True(limit.Reserve("fill-0").IsPermitted);
        Assert.Equal(Capacity, limit.TrackedSourceCount);

        // T0 + 1.5W + 1s: every fill entry but fill-0's second one has expired, and the periodic
        // prune is not due for another half window.
        clock.Advance(window / 2);
        Assert.True(limit.Reserve("after-the-fill-expired").IsPermitted);

        // fill-0 (still inside its window) and the new key: tracked, not waved through uncounted.
        Assert.True(
            limit.TrackedSourceCount == 2,
            $"{limit.TrackedSourceCount} sources were tracked after a new key arrived at capacity "
            + "once the whole fill had expired; the forced reclaim must free the expired entries "
            + "and track the new key (2 = fill-0 plus the new key), not fail open at the ceiling.");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private const int Capacity = 100_000;

    private static void FillToCapacity(RegistrationRateLimit limit)
    {
        for (var source = 0; source < Capacity; source++)
        {
            limit.Reserve($"fill-{source}");
        }

        Assert.Equal(Capacity, limit.TrackedSourceCount);
    }

    private static List<RecordingLogger<RegistrationRateLimit>.Entry> CeilingLines(
        RecordingLogger<RegistrationRateLimit> logger) =>
        [.. logger.Entries.Where(entry => entry.Message.Contains(
            "tracked_source_ceiling_reached", StringComparison.Ordinal))];

    private static long UntrackedSinceLastLine(RecordingLogger<RegistrationRateLimit>.Entry line)
    {
        var match = Regex.Match(line.Message, @"untracked_since_last_line=(\d+)");
        Assert.True(match.Success, $"the ceiling line carries no untracked_since_last_line: {line.Message}");

        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string SourceOf(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

        return RequestSource.Of(context, NullLogger.Instance);
    }
}
