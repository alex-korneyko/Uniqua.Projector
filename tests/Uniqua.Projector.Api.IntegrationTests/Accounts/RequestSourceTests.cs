using System.Net;
using Microsoft.AspNetCore.Http;
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
    public void At_capacity_a_flood_of_new_keys_within_one_interval_logs_the_ceiling_at_most_once()
    {
        // T-01: before the fix, every one of these 1,000 new-key attempts at capacity ran its own
        // unthrottled forced PruneExpired over all 100,000 tracked entries and wrote its own
        // tracked_source_ceiling_reached line. The fix claims the forced prune through a
        // compare-and-set like PruneIfDue's, at most once per a short named interval, so the
        // ceiling is logged at most once per window — carrying how many keys went untracked since
        // the last line — rather than once per request.
        var clock = new TestClock();
        var logger = new RecordingLogger<RegistrationRateLimit>();
        var limit = new RegistrationRateLimit(clock, logger);

        const int capacity = 100_000;
        for (var source = 0; source < capacity; source++)
        {
            limit.Reserve($"fill-{source}");
        }

        Assert.Equal(capacity, limit.TrackedSourceCount);

        const int flood = 1_000;
        for (var source = 0; source < flood; source++)
        {
            var reservation = limit.Reserve($"flood-{source}");

            // Fail-open at the ceiling is unchanged: a new key past capacity is still permitted,
            // just not tracked.
            Assert.True(reservation.IsPermitted);
        }

        var ceilingLines = logger.Entries
            .Count(entry => entry.Message.Contains(
                "tracked_source_ceiling_reached", StringComparison.Ordinal));

        Assert.True(
            ceilingLines <= 1,
            $"{ceilingLines} separate tracked_source_ceiling_reached lines were logged for {flood} "
            + "new keys inside one window; T-01 asks for at most one line per window, carrying how "
            + "many keys went untracked since the last one, not one line per refused request.");
    }

    [Fact]
    public void A_key_is_tracked_again_once_the_clock_passes_the_window()
    {
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);

        const int capacity = 100_000;
        for (var source = 0; source < capacity; source++)
        {
            limit.Reserve($"fill-{source}");
        }

        Assert.Equal(capacity, limit.TrackedSourceCount);

        var atCeiling = limit.Reserve("still-at-ceiling");
        Assert.True(atCeiling.IsPermitted);
        Assert.Equal(capacity, limit.TrackedSourceCount);

        clock.Advance(RegistrationRateLimit.Window + TimeSpan.FromSeconds(1));

        var afterWindow = limit.Reserve("tracked-again");
        Assert.True(afterWindow.IsPermitted);

        Assert.True(
            limit.TrackedSourceCount < capacity,
            $"after the window passed, {limit.TrackedSourceCount} sources were still tracked "
            + $"against a ceiling of {capacity}; a new key must be tracked again once expired "
            + "entries are reclaimed.");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static string SourceOf(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

        return RequestSource.Of(context, NullLogger.Instance);
    }
}
