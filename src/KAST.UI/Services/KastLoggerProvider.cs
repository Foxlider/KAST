using KAST.Core.Interfaces;

namespace KAST.UI.Services;

[ProviderAlias("KastInMemory")]
public sealed class KastLoggerProvider(KastLogStore store, IOutputSanitizer sanitizer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new KastLogger(store, sanitizer, categoryName);

    public void Dispose()
    {
        // No unmanaged resources to release.
    }
}

internal sealed class KastLogger(KastLogStore store, IOutputSanitizer sanitizer, string category) : ILogger
{
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Warning 
            || category.StartsWith("KAST.", StringComparison.OrdinalIgnoreCase);
    }

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

        var message = sanitizer.Sanitize(formatter(state, exception));
        if (exception != null)
            message += $"\n{sanitizer.SanitizeException(exception)}";

        store.Add(new AppLogEntry(DateTime.Now, logLevel, category, message));
    }
}
