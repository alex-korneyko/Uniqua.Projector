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
    public void The_configured_iteration_count_is_the_one_the_cost_floor_was_measured_at()
    {
        // Measured on the spec §6 reference machine: this count is what put a single PBKDF2
        // verification above the 100 ms floor. IdentityAccountStoreTests times a real
        // verification; this test is what fails if someone lowers the number without re-measuring.
        Assert.Equal(600_000, PasswordHashingCost.IterationCount);
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
