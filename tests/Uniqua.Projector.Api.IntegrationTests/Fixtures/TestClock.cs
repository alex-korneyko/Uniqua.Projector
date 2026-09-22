using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// The clock, under the test's control. Every time-dependent rule in this feature reads
/// <see cref="IClock"/> rather than the system clock precisely so that a 14-day or 90-day boundary
/// can be asserted in milliseconds instead of waited for.
/// </summary>
public sealed class TestClock : IClock
{
    private DateTimeOffset _now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _now;

    /// <summary>Puts the clock at an instant.</summary>
    public void Set(DateTimeOffset now) => _now = now;

    /// <summary>Moves the clock forward.</summary>
    public DateTimeOffset Advance(TimeSpan by) => _now += by;

    /// <summary>Returns the clock to its starting instant, so one test cannot skew the next.</summary>
    public void Reset() => _now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
}
