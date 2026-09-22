namespace Uniqua.Projector.Application.Accounts.Ports;

/// <summary>
/// The present, as a dependency. Every rule in this feature that depends on time — the two session
/// expiry rules, the activity stamp, the guessing delay and its 15-minute reset — reads it from
/// here rather than from the system clock, so a test can put the clock where it needs it instead
/// of waiting 14 days to find out whether the rule works.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
