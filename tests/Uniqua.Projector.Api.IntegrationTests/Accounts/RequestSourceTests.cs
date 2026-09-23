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

    // ---- Helpers -------------------------------------------------------------------------------

    private static string SourceOf(string remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

        return RequestSource.Of(context, NullLogger.Instance);
    }
}
