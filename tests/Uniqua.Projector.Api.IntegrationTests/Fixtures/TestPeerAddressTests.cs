namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// review 2026-09-23 (second re-review) Q-13(c) — <see cref="TestPeerAddress.Fresh"/> must never
/// hand out the same address twice within a run. It previously drew 16 random bits from
/// <see cref="Guid.NewGuid"/>, a 65536-address space in which a birthday-bound collision is likely
/// within a few hundred draws — well inside a full suite run — and <see cref="TestClock.Reset"/>
/// only rewinds the clock, not any per-source counter a rate limiter keeps, so a peer that
/// collided with an already-exhausted one earlier in the run stayed exhausted for every test after
/// it, for no reason that single test could see.
/// </summary>
public sealed class TestPeerAddressTests
{
    [Fact]
    public void Fifty_thousand_fresh_addresses_are_all_distinct()
    {
        var addresses = Enumerable.Range(0, 50_000)
            .Select(_ => TestPeerAddress.Fresh())
            .ToArray();

        Assert.Equal(addresses.Length, addresses.Distinct(StringComparer.Ordinal).Count());
    }
}
