using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Domain.Tests.Accounts;

/// <summary>
/// T3, corrected by T21 — AC-12's delay curve and the count it is read from. ADR 0010 replaced
/// account lockout with this: the two named floors (at least 2 s at the 6th consecutive failure,
/// at least 30 s at the 10th) are the contract. The curve is indexed by the failure being made —
/// the attempt AC-12 calls "a further attempt" after 5 have already failed is the 6th — and the
/// count it is indexed by starts again at one after 15 quiet minutes.
/// </summary>
public sealed class GuessingDelayTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset JustNow = Now - TimeSpan.FromSeconds(1);

    // ---- The curve, by the number of the failure being made ------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void The_first_five_consecutive_failures_are_not_delayed(int failure)
    {
        // AC-12 starts the curve *after* 5 failures — an owner mistyping their own password a few
        // times must not be punished for it.
        Assert.Equal(TimeSpan.Zero, GuessingDelay.ForFailure(failure));
    }

    [Fact]
    public void The_sixth_consecutive_failure_is_delayed_at_least_two_seconds()
    {
        Assert.True(GuessingDelay.ForFailure(6) >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_tenth_consecutive_failure_is_delayed_at_least_thirty_seconds()
    {
        Assert.True(GuessingDelay.ForFailure(10) >= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void The_curve_never_decreases_as_the_failure_count_grows()
    {
        var previous = TimeSpan.Zero;

        for (var failure = 1; failure <= 40; failure++)
        {
            var delay = GuessingDelay.ForFailure(failure);
            Assert.True(
                delay >= previous,
                $"delay at failure {failure} ({delay}) is shorter than at {failure - 1} ({previous})");
            previous = delay;
        }
    }

    [Fact]
    public void The_delay_is_bounded_so_a_guesser_cannot_hold_a_request_open_indefinitely()
    {
        // Unbounded growth would turn the defence into a way of tying up the server. The ceiling
        // is recorded in spec §6 and ADR 0010 as a deliberate bound on AC-12's growth.
        Assert.Equal(GuessingDelay.Ceiling, GuessingDelay.ForFailure(1_000));
        Assert.Equal(TimeSpan.FromMinutes(5), GuessingDelay.Ceiling);
    }

    // ---- The count a failure leaves behind -----------------------------------------------------

    [Fact]
    public void The_first_failure_an_account_has_ever_had_is_failure_one()
    {
        Assert.Equal(1, GuessingDelay.CountAfterFailure(0, lastFailedAttemptAt: null, Now));
    }

    [Fact]
    public void A_failure_inside_the_window_adds_one_to_the_stored_count()
    {
        Assert.Equal(6, GuessingDelay.CountAfterFailure(5, JustNow, Now));
    }

    [Fact]
    public void A_failure_exactly_fifteen_minutes_after_the_last_starts_the_count_again()
    {
        // "after 15 minutes in which no attempt is made" — the boundary is inclusive.
        Assert.Equal(1, GuessingDelay.CountAfterFailure(20, Now - TimeSpan.FromMinutes(15), Now));
    }

    [Fact]
    public void A_failure_sixteen_minutes_after_the_last_starts_the_count_again()
    {
        Assert.Equal(1, GuessingDelay.CountAfterFailure(20, Now - TimeSpan.FromMinutes(16), Now));
    }

    [Fact]
    public void A_failure_one_second_inside_the_window_still_counts_on()
    {
        Assert.Equal(
            21,
            GuessingDelay.CountAfterFailure(
                20, Now - TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1), Now));
    }

    [Fact]
    public void A_stored_count_with_no_recorded_attempt_instant_is_treated_as_reset()
    {
        // The two columns can only disagree this way through a manual edit or an older row; read
        // it as "no evidence of a recent attempt" rather than counting on from a figure we cannot
        // date.
        Assert.Equal(1, GuessingDelay.CountAfterFailure(20, lastFailedAttemptAt: null, Now));
    }

    [Fact]
    public void A_last_attempt_instant_in_the_future_does_not_reset_the_count()
    {
        // A backwards clock must not become a way of clearing the counter.
        Assert.Equal(11, GuessingDelay.CountAfterFailure(10, Now + TimeSpan.FromMinutes(30), Now));
    }

    [Fact]
    public void Two_quick_typos_after_a_quiet_period_are_both_free()
    {
        // The review 2026-09-22 R-02 case: 20 old failures, a quiet period, then an owner who
        // mistypes twice. The second typo is the 2nd failure of a fresh count, not the 22nd.
        var first = GuessingDelay.CountAfterFailure(20, Now - TimeSpan.FromMinutes(16), Now);
        var second = GuessingDelay.CountAfterFailure(first, Now, Now + TimeSpan.FromSeconds(5));

        Assert.Equal(2, second);
        Assert.Equal(TimeSpan.Zero, GuessingDelay.ForFailure(second));
    }

    [Fact]
    public void The_reset_window_is_the_only_place_fifteen_minutes_appears()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), GuessingDelay.ResetAfter);
    }
}
