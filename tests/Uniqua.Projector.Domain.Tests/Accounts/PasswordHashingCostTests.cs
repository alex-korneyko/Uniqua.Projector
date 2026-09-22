using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Domain.Tests.Accounts;

/// <summary>
/// T7 — spec §6 asks that one password verification cost at least 100 ms. That cost is deliberate:
/// it is what makes offline guessing against a leaked hash expensive. The figure is held here as a
/// named constant so that lowering it is an edit to a test rather than a quiet change to a
/// configuration value nobody reads.
/// </summary>
public sealed class PasswordHashingCostTests
{
    [Fact]
    public void The_configured_iteration_count_is_the_one_both_spec_six_figures_were_reconciled_at()
    {
        // spec §6 asks for at least 100 ms per verification AND a 600 ms p95 to sign in. This
        // count clears the first with room to spare and leaves most of the second for everything
        // else. IdentityAccountStoreTests times a real verification against the floor;
        // LatencyBudgetTests measures the sign-in path against the ceiling. Changing this number
        // means re-running both, which is why it is pinned here rather than left in a
        // configuration file nobody reads.
        Assert.Equal(210_000, PasswordHashingCost.IterationCount);
    }

    [Fact]
    public void The_cost_floor_is_the_one_spec_six_states()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), PasswordHashingCost.MinimumVerificationTime);
    }

    [Fact]
    public void The_iteration_count_is_never_allowed_below_the_owasp_floor_for_this_algorithm()
    {
        // A second, independent floor: even if the reference machine gets faster and the 100 ms
        // measurement stops binding, the count must not fall below current public guidance for
        // PBKDF2-HMAC-SHA512.
        Assert.True(
            PasswordHashingCost.IterationCount >= 210_000,
            $"{PasswordHashingCost.IterationCount} is below the public floor for PBKDF2-HMAC-SHA512");
    }
}
