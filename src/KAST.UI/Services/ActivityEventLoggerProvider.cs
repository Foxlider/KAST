using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KAST.UI.Services;

/// <summary>
/// Attaches ILogger calls as span events on <see cref="Activity.Current"/> so
/// that log messages are visible inside traces in Jaeger.
/// Only wires up when there is an active span; has no effect otherwise.
/// </summary>
public sealed class ActivityEventLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ActivityEventLogger(categoryName);
    public void Dispose()
    {
        // No unmanaged resources to release.
    }
}

file sealed class ActivityEventLogger(string categoryName) : ILogger
{
    // No span = no overhead; never block callers with actual I/O in this path.
    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Debug && Activity.Current is not null;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var activity = Activity.Current;
        if (activity is null || logLevel < LogLevel.Debug) return;

        var tags = new ActivityTagsCollection
        {
            ["log.severity"] = logLevel.ToString(),
            ["log.message"] = formatter(state, exception),
            ["log.category"] = categoryName
        };

        if (exception is not null)
        {
            tags["exception.type"] = exception.GetType().Name;
            tags["exception.message"] = exception.Message;
        }

        activity.AddEvent(new ActivityEvent("log", tags: tags));
    }
}
