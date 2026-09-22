using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Domain.Tests.Accounts;

/// <summary>
/// T2 — AC-07 ("a session ends after 14 days of inactivity") and AC-07b ("no session outlives 90
/// days however actively it is used"). Both boundaries are asserted from both sides, and every
/// instant is passed in: the entity reads no clock, so these tests cannot become flaky at midnight
/// or drift with the machine's timezone.
/// </summary>
public sealed class SessionTests
{
    private static readonly DateTimeOffset Opened = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Account = Ids.New();

    [Fact]
    public void A_session_opens_live_for_its_account()
    {
        var session = Session.Open(Account, Opened);

        Assert.Equal(Account, session.AccountId);
        Assert.Equal(Opened, session.CreatedAt);
        Assert.Equal(Opened, session.LastSeenAt);
        Assert.Null(session.RevokedAt);
        Assert.False(session.IsRevoked);
        Assert.False(session.IsExpired(Opened));
    }

    [Fact]
    public void A_session_id_is_a_time_ordered_guid_the_application_generated()
    {
        Assert.Equal(7, Session.Open(Account, Opened).Id.Version);
    }

    // ---- AC-07: 14 days of inactivity -------------------------------------------------------

    [Fact]
    public void A_session_idle_one_minute_short_of_14_days_is_still_live()
    {
        var session = Session.Open(Account, Opened);

        Assert.False(session.IsExpired(Opened + TimeSpan.FromDays(14) - TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void A_session_idle_for_exactly_14_days_is_expired()
    {
        var session = Session.Open(Account, Opened);

        Assert.True(session.IsExpired(Opened + TimeSpan.FromDays(14)));
    }

    [Fact]
    public void Activity_moves_the_idle_boundary_along_with_it()
    {
        var session = Session.Open(Account, Opened);
        var thirteenDaysIn = Opened + TimeSpan.FromDays(13);

        session.StampActivity(thirteenDaysIn);

        // 14 days after the *original* open, but only 1 day since the last request.
        Assert.False(session.IsExpired(Opened + TimeSpan.FromDays(14)));
        Assert.True(session.IsExpired(thirteenDaysIn + TimeSpan.FromDays(14)));
    }

    // ---- AC-07b: 90 days absolute, however actively used --------------------------------------

    [Fact]
    public void A_session_used_one_second_ago_but_opened_90_days_ago_is_expired()
    {
        var session = Session.Open(Account, Opened);
        var ninetyDaysIn = Opened + TimeSpan.FromDays(90);

        // Used constantly right up to the ceiling — activity must not save it.
        session.StampActivity(ninetyDaysIn - TimeSpan.FromSeconds(1));

        Assert.True(session.IsExpired(ninetyDaysIn));
    }

    [Fact]
    public void A_steadily_used_session_one_minute_short_of_90_days_is_still_live()
    {
        var session = Session.Open(Account, Opened);
        var almost = Opened + TimeSpan.FromDays(90) - TimeSpan.FromMinutes(1);

        session.StampActivity(almost - TimeSpan.FromHours(1));

        Assert.False(session.IsExpired(almost));
    }

    [Fact]
    public void The_two_expiry_rules_are_or_ed_rather_than_ranked()
    {
        var session = Session.Open(Account, Opened);

        // Idle AND past the ceiling: still just expired, with no precedence to argue about.
        Assert.True(session.IsExpired(Opened + TimeSpan.FromDays(120)));
    }

    [Fact]
    public void A_clock_that_moved_backwards_does_not_expire_a_session()
    {
        var session = Session.Open(Account, Opened);

        Assert.False(session.IsExpired(Opened - TimeSpan.FromDays(30)));
    }

    // ---- Revocation is a separate fact from expiry ---------------------------------------------

    [Fact]
    public void A_revoked_session_is_revoked_regardless_of_expiry()
    {
        var session = Session.Open(Account, Opened);
        var revokedAt = Opened + TimeSpan.FromMinutes(1);

        session.Revoke(revokedAt);

        Assert.True(session.IsRevoked);
        Assert.Equal(revokedAt, session.RevokedAt);
        // Not expired — the caller must check both, so the two facts can never be confused.
        Assert.False(session.IsExpired(revokedAt));
    }

    [Fact]
    public void Revoking_an_already_revoked_session_keeps_the_first_instant()
    {
        var session = Session.Open(Account, Opened);
        var first = Opened + TimeSpan.FromMinutes(1);

        session.Revoke(first);
        session.Revoke(Opened + TimeSpan.FromMinutes(5));

        Assert.Equal(first, session.RevokedAt);
    }

    // ---- The at-most-once-an-hour activity stamp ----------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void Activity_is_stamped_at_most_once_an_hour(int minutesSinceLastSeen, bool expected)
    {
        var session = Session.Open(Account, Opened);

        Assert.Equal(
            expected,
            session.ShouldStampActivity(Opened + TimeSpan.FromMinutes(minutesSinceLastSeen)));
    }

    [Fact]
    public void Stamping_activity_moves_last_seen_at_and_leaves_created_at_alone()
    {
        var session = Session.Open(Account, Opened);
        var later = Opened + TimeSpan.FromHours(2);

        session.StampActivity(later);

        Assert.Equal(later, session.LastSeenAt);
        Assert.Equal(Opened, session.CreatedAt);
    }
}
