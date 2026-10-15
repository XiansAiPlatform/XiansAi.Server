using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tests.TestUtils;

/// <summary>
/// Records every log entry the host writes, so a test can assert on what reached the logs
/// (for example that a request produced no error-level entry).
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(LogLevel Level, string Category, string Message, Exception? Exception);

    private readonly ConcurrentQueue<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries.ToArray();

    public void Clear() => _entries.Clear();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new Entry(logLevel, category, formatter(state, exception), exception));
    }
}
