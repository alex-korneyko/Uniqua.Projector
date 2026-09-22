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

    private readonly List<TimeSpan> _delays = [];

    public DateTimeOffset UtcNow => _now;

    /// <summary>
    /// Every delay the application asked for, in order. The wait is recorded rather than served,
    /// so a test can assert that the 10th consecutive failure was held for 30 seconds without
    /// spending 30 seconds finding out.
    /// </summary>
    public IReadOnlyList<TimeSpan> RequestedDelays => _delays;

    public TimeSpan LongestRequestedDelay => _delays.Count is 0 ? TimeSpan.Zero : _delays.Max();

    /// <summary>
    /// When set, every non-zero delay behaves as a request the client abandoned mid-wait: it is
    /// recorded, then cancelled — which is what a real <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
    /// on the request-abort token does when a guesser hangs up.
    /// </summary>
    public bool AbandonDelays { get; set; }

    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        _delays.Add(duration);
        return AbandonDelays && duration > TimeSpan.Zero
            ? Task.FromCanceled(new CancellationToken(canceled: true))
            : Task.CompletedTask;
    }

    public void ClearRequestedDelays() => _delays.Clear();

    /// <summary>Puts the clock at an instant.</summary>
    public void Set(DateTimeOffset now) => _now = now;

    /// <summary>Moves the clock forward.</summary>
    public DateTimeOffset Advance(TimeSpan by) => _now += by;

    /// <summary>Returns the clock to its starting instant, so one test cannot skew the next.</summary>
    public void Reset()
    {
        _delays.Clear();
        AbandonDelays = false;
        _now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    }

}
