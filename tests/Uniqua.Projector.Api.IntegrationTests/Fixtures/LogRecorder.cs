using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// Records every log entry the application writes during a test run, so a test can assert on the
/// line a code path actually chose to log — such as <c>RecordFailureAsync</c>'s
/// <c>contended=true</c> marker (Q-13a) — rather than only on the response it produced.
/// </summary>
public sealed class LogRecorder : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> _entries = new();

    public IReadOnlyCollection<(string Category, LogLevel Level, string Message)> Entries => [.. _entries];

    public void Clear() => _entries.Clear();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(
        string category,
        ConcurrentQueue<(string Category, LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, logLevel, formatter(state, exception)));
    }
}
