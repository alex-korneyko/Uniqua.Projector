using System.Reflection;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// review 2026-09-23 (third re-review) S-01 — <c>SignInRateLimit</c>'s per-address key must be
/// bounded and unrevealing whatever was submitted, the per-source ceiling must be checked before a
/// per-address slot is ever taken, and a released, now-empty per-address list must be dropped at
/// once rather than left for the next periodic prune. These are pure bookkeeping properties of
/// <see cref="SignInRateLimit"/> and <see cref="SlidingWindowLimiter"/>, so — like
/// <see cref="RequestSourceTests"/> — this file drives them directly with a <see cref="TestClock"/>
/// rather than through <c>ApiFactory</c> and a database.
/// </summary>
public sealed class SignInRateLimitTests
{
    // ---- S-01: the per-source ceiling is checked before a per-address slot is ever reserved ------

    [Fact]
    public void A_flood_of_new_addresses_from_a_source_already_at_its_ceiling_leaves_the_per_address_count_unchanged()
    {
        var clock = new TestClock();
        var limit = new SignInRateLimit(clock);
        const string source = "203.0.113.50";

        // Spend the source's own ceiling first, one distinct address per attempt so nothing here
        // touches the per-address cap of 20.
        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerSourceWindow; attempt++)
        {
            var reservation = limit.Reserve(source, $"spend-{attempt}@example.test");
            Assert.True(reservation.IsPermitted);
        }

        var trackedBeforeFlood = limit.TrackedAddressKeyCount;

        // The source is now capped. A flood of brand-new addresses it has never named before —
        // including one absurdly long, cheap for an attacker to send and expensive to key on in
        // plain text — must be refused before any per-address slot is taken for them.
        var hugeAddress = new string('a', 10_000) + "@example.test";
        var floodedAddresses = new[] { hugeAddress }
            .Concat(Enumerable.Range(0, 50).Select(index => $"flood-{index}@example.test"));

        foreach (var email in floodedAddresses)
        {
            var reservation = limit.Reserve(source, email);

            Assert.False(
                reservation.IsPermitted,
                $"a source already at its per-source ceiling of "
                + $"{SignInRateLimit.PermittedFailuresPerSourceWindow} was still permitted a new "
                + "address; the per-source check must run before any per-address slot is reserved.");
        }

        Assert.Equal(
            trackedBeforeFlood,
            limit.TrackedAddressKeyCount);
    }

    // ---- S-01: SlidingWindowLimiter.Release drops a key's list once it is empty -------------------

    [Fact]
    public void Releasing_the_only_reservation_for_an_address_drops_its_tracked_entry_at_once()
    {
        var clock = new TestClock();
        var limit = new SignInRateLimit(clock);

        var reservation = limit.Reserve("203.0.113.7", "owner@example.test");
        Assert.True(reservation.IsPermitted);
        Assert.Equal(1, limit.TrackedAddressKeyCount);

        // A correct password releases both slots (AC-12). The per-address list this reservation
        // was the only entry in must be dropped immediately — not left in `_entries` until the
        // next periodic or forced prune, which is exactly the unbounded growth S-01 found.
        limit.Release(reservation);

        Assert.Equal(0, limit.TrackedAddressKeyCount);
    }

    // ---- S-01: the per-address key's length does not depend on what was submitted -----------------

    [Fact]
    public void The_per_address_key_length_does_not_grow_with_the_submitted_addresses_length()
    {
        var clock = new TestClock();
        var limit = new SignInRateLimit(clock);

        var shortKey = CompoundKeyFor(limit, "203.0.113.7", "a@b.co");
        var longKey = CompoundKeyFor(limit, "203.0.113.7", new string('x', 10_000) + "@example.test");

        Assert.Equal(
            shortKey.Length,
            longKey.Length);
    }

    [Fact]
    public void The_per_address_key_never_carries_the_submitted_address_in_plain_text()
    {
        var clock = new TestClock();
        var limit = new SignInRateLimit(clock);

        var key = CompoundKeyFor(limit, "203.0.113.7", "someone-guessable@example.test");

        Assert.DoesNotContain("someone-guessable", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.test", key, StringComparison.OrdinalIgnoreCase);
    }

    // ---- V-05: a per-address refusal gives back the per-source slot it took before checking -------

    [Fact]
    public void A_per_address_refusal_gives_back_its_per_source_slot()
    {
        var clock = new TestClock();
        var limit = new SignInRateLimit(clock);
        const string source = "203.0.113.9";
        const string cappedAddress = "owner@example.test";

        // Cap the (source, address) pair at its own ceiling of 20.
        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            Assert.True(limit.Reserve(source, cappedAddress).IsPermitted);
        }

        // Every further attempt at the SAME pair is refused by the per-address cap alone. Reserve
        // takes the per-source slot first (S-01) and must give it back on a per-address refusal
        // (V-05) — otherwise these refusals, which never touched a password, would still spend the
        // source's own ceiling.
        const int refusalsToSend = 100;
        for (var attempt = 0; attempt < refusalsToSend; attempt++)
        {
            var reservation = limit.Reserve(source, cappedAddress);
            Assert.False(reservation.IsPermitted);
            Assert.Equal(SignInRateLimitCap.PerAddress, reservation.Cap);
        }

        // Those 100 refusals must not have counted against the source's own ceiling of 100 — only
        // the 20 permitted attempts against the capped pair should. Spend exactly the remaining 80
        // on distinct addresses; every one must still be permitted.
        var remaining = SignInRateLimit.PermittedFailuresPerSourceWindow - SignInRateLimit.PermittedFailuresPerWindow;
        for (var index = 0; index < remaining; index++)
        {
            var reservation = limit.Reserve(source, $"other-{index}@example.test");
            Assert.True(
                reservation.IsPermitted,
                "a per-address refusal did not give back its per-source slot: the source's own "
                + "ceiling was exhausted by refusals that should never have counted against it.");
        }

        // ...and the one past that must now be refused by the per-source cap, proving the count is
        // exactly the 20 + 80 = 100 permitted attempts, not 20 + 100 refused-and-uncounted ones.
        var overLimit = limit.Reserve(source, "final-straw@example.test");
        Assert.False(overLimit.IsPermitted);
        Assert.Equal(SignInRateLimitCap.PerSource, overLimit.Cap);
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// Reaches <c>SignInRateLimit.CompoundKey</c> directly — the one place S-01's key shape (hashed,
    /// fixed-length, source-prefixed) can be observed without allocating the gigabytes a black-box
    /// flood test would need to prove the same thing.
    /// </summary>
    private static string CompoundKeyFor(SignInRateLimit limit, string source, string email)
    {
        var method = typeof(SignInRateLimit).GetMethod(
            "CompoundKey", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "SignInRateLimit no longer declares a private CompoundKey(string, string) method.");

        return (string)method.Invoke(limit, [source, email])!;
    }
}
