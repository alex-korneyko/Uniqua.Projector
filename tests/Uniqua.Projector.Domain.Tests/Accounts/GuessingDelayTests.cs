using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Domain.Tests.Accounts;

/// <summary>
/// T3 — AC-12's delay curve. ADR 0010 replaced account lockout with this: the two named floors
/// (at least 2 s at the 6th consecutive failure, at least 30 s at the 10th) are the contract, and
/// the reset is derived from the stored last-attempt instant on read rather than kept alive by a
/// timer, so a restart cannot lose it.
/// </summary>
public sealed class GuessingDelayTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset JustNow = Now - TimeSpan.FromSeconds(1);

    [Fact]
    public void An_account_that_has_never_failed_is_not_delayed()
    {
        Assert.Equal(TimeSpan.Zero, GuessingDelay.For(0, lastFailedAttemptAt: null, Now));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void The_first_five_consecutive_failures_are_not_delayed(int failures)
    {
        // AC-12 starts the curve *after* 5 failures — an owner mistyping their own password a few
        // times must not be punished for it.
        Assert.Equal(TimeSpan.Zero, GuessingDelay.For(failures, JustNow, Now));
    }

    [Fact]
    public void The_sixth_consecutive_failure_is_delayed_at_least_two_seconds()
    {
        Assert.True(GuessingDelay.For(6, JustNow, Now) >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_tenth_consecutive_failure_is_delayed_at_least_thirty_seconds()
    {
        Assert.True(GuessingDelay.For(10, JustNow, Now) >= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void The_curve_never_decreases_as_the_failure_count_grows()
    {
        var previous = TimeSpan.Zero;

        for (var failures = 0; failures <= 40; failures++)
        {
            var delay = GuessingDelay.For(failures, JustNow, Now);
            Assert.True(
                delay >= previous,
                $"delay at {failures} failures ({delay}) is shorter than at {failures - 1} ({previous})");
            previous = delay;
        }
    }

    [Fact]
    public void The_delay_is_bounded_so_a_guesser_cannot_hold_a_request_open_indefinitely()
    {
        // Unbounded growth would turn the defence into a way of tying up the server. AC-12 is
        // satisfied by the two floors; the ceiling is this task's engineering choice.
        Assert.Equal(GuessingDelay.Ceiling, GuessingDelay.For(1_000, JustNow, Now));
        Assert.True(GuessingDelay.Ceiling <= TimeSpan.FromMinutes(5));
    }

    // ---- The 15-minute reset, derived on read --------------------------------------------------

    [Fact]
    public void A_count_whose_last_attempt_was_exactly_fifteen_minutes_ago_is_reset()
    {
        // "after 15 minutes in which no attempt is made" — the boundary is inclusive.
        Assert.Equal(
            TimeSpan.Zero,
            GuessingDelay.For(20, Now - TimeSpan.FromMinutes(15), Now));
    }

    [Fact]
    public void A_count_whose_last_attempt_was_sixteen_minutes_ago_is_reset()
    {
        Assert.Equal(
            TimeSpan.Zero,
            GuessingDelay.For(20, Now - TimeSpan.FromMinutes(16), Now));
    }

    [Fact]
    public void A_count_whose_last_attempt_was_one_second_inside_the_window_still_delays()
    {
        var delay = GuessingDelay.For(
            20, Now - TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1), Now);

        Assert.True(delay > TimeSpan.Zero);
    }

    [Fact]
    public void A_stored_count_with_no_recorded_attempt_instant_is_treated_as_reset()
    {
        // The two columns can only disagree this way through a manual edit or an older row; read
        // it as "no evidence of a recent attempt" rather than delaying on a count we cannot date.
        Assert.Equal(TimeSpan.Zero, GuessingDelay.For(20, lastFailedAttemptAt: null, Now));
    }

    [Fact]
    public void A_last_attempt_instant_in_the_future_does_not_reset_the_count()
    {
        // A backwards clock must not become a way of clearing the counter.
        Assert.True(GuessingDelay.For(10, Now + TimeSpan.FromMinutes(30), Now) > TimeSpan.Zero);
    }

    [Fact]
    public void The_reset_window_is_the_only_place_fifteen_minutes_appears()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), GuessingDelay.ResetAfter);
    }
}
