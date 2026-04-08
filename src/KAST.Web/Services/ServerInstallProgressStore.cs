using System.Collections.Concurrent;

namespace KAST.Web.Services;

/// <summary>Holds live download state for a single server instance.</summary>
public class ServerInstallState
{
    private readonly List<string> _log = [];
    private double _progress;

    public IReadOnlyList<string> Log => _log;
    public double Progress => _progress;
    public bool IsDownloading { get; set; }
    public bool IsComplete { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Fires on every log line and on meaningful progress increments.</summary>
    public event Action? Changed;

    public void AddLog(string line)
    {
        _log.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
        Changed?.Invoke();
    }

    public void SetProgress(double pct)
    {
        _progress = pct;
        // Only raise Changed at ~1% increments to avoid flooding the Blazor renderer
        if (pct % 1.0 < 0.5)
            Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void Reset()
    {
        _log.Clear();
        _progress = 0;
        IsDownloading = false;
        IsComplete = false;
        ErrorMessage = null;
    }
}

/// <summary>Singleton store mapping server instance IDs to their install state.</summary>
public class ServerInstallProgressStore
{
    private readonly ConcurrentDictionary<int, ServerInstallState> _states = new();

    public ServerInstallState GetOrCreate(int instanceId)
        => _states.GetOrAdd(instanceId, _ => new ServerInstallState());

    public ServerInstallState? Get(int instanceId)
        => _states.TryGetValue(instanceId, out var s) ? s : null;
}
