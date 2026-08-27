using KAST.Core.Interfaces;

namespace KAST.UI.Services;

/// <summary>
/// Writes application logs at Information level and above to a daily local file.
/// </summary>
public sealed class KastFileLoggerProvider(string logDirectory, IOutputSanitizer sanitizer) : ILoggerProvider
{
    private readonly object _sync = new();

    public ILogger CreateLogger(string categoryName) =>
        new KastFileLogger(logDirectory, sanitizer, _sync, categoryName);

    public void Dispose()
    {
        // Each entry is appended and closed immediately so logs remain available
        // after an unexpected process exit.
    }
}

internal sealed class KastFileLogger(
    string logDirectory,
    IOutputSanitizer sanitizer,
    object sync,
    string category) : ILogger
{
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        try
        {
            var timestamp = DateTimeOffset.Now;
            var message = sanitizer.Sanitize(formatter(state, exception));
            if (exception is not null)
                message += $"{Environment.NewLine}{sanitizer.Sanitize(exception.ToString())}";

            var entry = $"{timestamp:O} [{logLevel}] {category}: {message}{Environment.NewLine}";
            var logPath = Path.Combine(logDirectory, $"kast-{timestamp:yyyy-MM-dd}.log");
            lock (sync)
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(logPath, entry);
            }
        }
        catch
        {
            // Logging must not interfere with the application when disk I/O fails.
        }
    }
}
