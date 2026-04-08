using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace KAST.Web.Services;

/// <summary>
/// Captures log entries from the ASP.NET Core logging pipeline and exposes them for the UI.
/// </summary>
public sealed class KastLogStore
{
    private const int MaxEntries = 2000;
    private readonly ConcurrentQueue<AppLogEntry> _entries = new();

    public event Action? OnNewEntry;

    public void Add(AppLogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > MaxEntries)
            _entries.TryDequeue(out _);

        OnNewEntry?.Invoke();
    }

    public IReadOnlyList<AppLogEntry> GetAll() => _entries.ToArray();
}

public record AppLogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message);
