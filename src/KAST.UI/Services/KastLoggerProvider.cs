using Microsoft.Extensions.Logging;

namespace KAST.UI.Services;

[ProviderAlias("KastInMemory")]
public sealed class KastLoggerProvider(KastLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new KastLogger(store, categoryName);

    public void Dispose() { }
}

internal sealed class KastLogger(KastLogStore store, string category) : ILogger
{
    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel >= LogLevel.Warning) return true;
        if (category.StartsWith("KAST.", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception != null) message += $"\n{exception}";

        store.Add(new AppLogEntry(DateTime.Now, logLevel, category, message));
    }
}
