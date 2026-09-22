using Microsoft.Extensions.Logging;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// A logger that keeps what it was told, for the promises that are about what an operator sees —
/// sad §7's alerts are log lines, so the only way to assert one fires is to read the log.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new Entry(logLevel, formatter(state, exception)));
        }
    }

    /// <summary>One line as it would have been written.</summary>
    public sealed record Entry(LogLevel Level, string Message);
}
