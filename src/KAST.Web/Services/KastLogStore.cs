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

    /// <summary>
    /// Clears log entries. If <paramref name="filter"/> is null, clears everything.
    /// If <paramref name="filter"/> is <see cref="LogLevel.Error"/>, also clears <see cref="LogLevel.Critical"/>.
    /// </summary>
    public void Clear(LogLevel? filter = null)
    {
        if (filter is null)
        {
            while (_entries.TryDequeue(out _)) { }
        }
        else
        {
            // Rebuild the queue keeping only entries that don't match the filter.
            var kept = _entries
                .Where(e => !MatchesFilter(e.Level, filter.Value))
                .ToArray();
            while (_entries.TryDequeue(out _)) { }
            foreach (var e in kept)
                _entries.Enqueue(e);
        }

        OnNewEntry?.Invoke();
    }

    private static bool MatchesFilter(LogLevel entryLevel, LogLevel filter)
    {
        // Error filter also matches Critical, matching the UI semantics.
        if (filter == LogLevel.Error)
            return entryLevel is LogLevel.Error or LogLevel.Critical;
        return entryLevel == filter;
    }
}

public record AppLogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message);
