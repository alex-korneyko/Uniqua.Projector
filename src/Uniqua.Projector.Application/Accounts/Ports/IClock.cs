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

    /// <summary>
    /// Waits. It belongs on this port for the same reason the present does: AC-12's delay reaches
    /// 30 seconds, and a test that proved it by actually waiting would be a test nobody runs.
    /// Asking the port to wait lets a test assert the duration that was requested.
    /// </summary>
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}
