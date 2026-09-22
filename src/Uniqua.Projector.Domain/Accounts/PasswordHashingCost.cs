namespace Uniqua.Projector.Domain.Accounts;

/// <summary>
/// How expensive a password verification is allowed to be cheap. spec §6 asks for at least 100 ms
/// per attempt, and that slowness is the feature, not a cost to be optimised away: it is what
/// makes offline guessing against a leaked hash expensive, and what makes the online delay curve
/// of <see cref="GuessingDelay"/> worth having at all.
/// </summary>
/// <remarks>
/// The figure lives in Domain rather than in the Infrastructure configuration that consumes it,
/// because it is a stated property of the product rather than a tuning knob. Infrastructure reads
/// it; a unit test pins it; lowering it means editing a test and re-measuring, not quietly
/// changing a number in a service registration.
/// </remarks>
public static class PasswordHashingCost
{
    /// <summary>
    /// PBKDF2-HMAC-SHA512 iterations, measured on the spec §6 reference machine as the count that
    /// puts one verification above <see cref="MinimumVerificationTime"/>.
    /// </summary>
    public const int IterationCount = 600_000;

    /// <summary>The floor spec §6 states.</summary>
    public static readonly TimeSpan MinimumVerificationTime = TimeSpan.FromMilliseconds(100);
}
