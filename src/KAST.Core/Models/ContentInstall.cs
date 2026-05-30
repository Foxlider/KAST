using System.Collections.Immutable;
using KAST.Core.Enums;

namespace KAST.Core.Models;

/// <summary>
/// Describes a single step inside a content install operation.
/// Mutable — the orchestrator updates <see cref="Status"/>, <see cref="Progress"/>, and <see cref="Error"/>.
/// </summary>
public class ContentStep
{
    public required string Name { get; init; }
    public string? Detail { get; init; }
    public ContentStepStatus Status { get; set; } = ContentStepStatus.Pending;
    public double Progress { get; set; }
    public string? Error { get; set; }
    /// <summary>When true the UI renders an indeterminate (spinning) bar instead of a percentage.</summary>
    public bool IsIndeterminate { get; set; }
}

/// <summary>
/// Immutable descriptor handed to an <see cref="KAST.Core.Interfaces.IContentInstaller"/>
/// so it knows what to install and where.
/// </summary>
public record ContentInstallRequest
{
    public required ContentType Type { get; init; }

    /// <summary>Final destination path (instance dir for servers, staging dir for mods).</summary>
    public required string DestinationPath { get; init; }
    public int ModId { get; init; }

    // ── Local mod specifics ──
    /// <summary>Path to the ZIP archive or existing folder (local mods only).</summary>
    public string? SourcePath { get; init; }

    // ── Steam mod specifics ──
    public long WorkshopId { get; init; }
    public long ExpectedSizeBytes { get; init; }

    // ── Server specifics ──
    public uint AppId { get; init; }
    public int ServerInstanceId { get; init; }

    /// <summary>Snapshot of DLC flags so the installer can plan steps without DB access.</summary>
    public ServerInstance? Instance { get; init; }

    /// <summary>Maximum parallel chunk downloads (from user settings).</summary>
    public int MaxParallelDownloads { get; init; } = 4;
}

/// <summary>
/// Live state of a content install operation, including its ordered steps.
/// </summary>
public class ContentInstallState
{
    private ImmutableArray<string> _log = [];
    private readonly object _lock = new();

    public required string Key { get; init; }
    public required ContentType Type { get; init; }
    public required string Label { get; init; }
    public List<ContentStep> Steps { get; init; } = [];

    public bool IsDownloading { get; set; }
    public bool IsComplete { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>For workshop mod installs: the manifest ID used during the download. Set by SteamModInstaller.</summary>
    public ulong InstalledManifestId { get; set; }

    public int CurrentStepIndex { get; set; } = -1;
    public ContentStep? CurrentStep => CurrentStepIndex >= 0 && CurrentStepIndex < Steps.Count
        ? Steps[CurrentStepIndex] : null;

    public IReadOnlyList<string> Log
    {
        get { lock (_lock) return _log; }
    }

    public double OverallProgress
    {
        get
        {
            if (Steps.Count == 0) return 0;
            return Steps.Sum(s => s.Status == ContentStepStatus.Completed || s.Status == ContentStepStatus.Skipped ? 100.0 : s.Progress) / Steps.Count;
        }
    }

    public event Action? Changed;

    public void AddLog(string line)
    {
        lock (_lock) _log = _log.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void BeginStep(int index)
    {
        if (index < 0 || index >= Steps.Count) return;
        CurrentStepIndex = index;
        Steps[index].Status = ContentStepStatus.InProgress;
        Steps[index].Progress = 0;
        Changed?.Invoke();
    }

    public void CompleteStep(int index)
    {
        if (index < 0 || index >= Steps.Count) return;
        Steps[index].Status = ContentStepStatus.Completed;
        Steps[index].Progress = 100;
        Steps[index].IsIndeterminate = false;
        Changed?.Invoke();
    }

    public void FailStep(int index, string error)
    {
        if (index < 0 || index >= Steps.Count) return;
        Steps[index].Status = ContentStepStatus.Failed;
        Steps[index].Error = error;
        Steps[index].IsIndeterminate = false;
        Changed?.Invoke();
    }

    public void SkipStep(int index, string reason)
    {
        if (index < 0 || index >= Steps.Count) return;
        Steps[index].Status = ContentStepStatus.Skipped;
        Steps[index].Error = reason;
        Changed?.Invoke();
    }

    public void SetStepIndeterminate(int index, bool indeterminate)
    {
        if (index < 0 || index >= Steps.Count) return;
        Steps[index].IsIndeterminate = indeterminate;
        Changed?.Invoke();
    }

    public void SetStepProgress(int index, double pct)
    {
        if (index < 0 || index >= Steps.Count) return;
        // Throttle — only fire Changed at ~1% increments
        var prev = Steps[index].Progress;
        Steps[index].Progress = pct;
        if (Math.Abs(pct - prev) >= 1.0 || pct >= 100)
            Changed?.Invoke();
    }

    public void Reset()
    {
        lock (_lock) _log = [];
        CurrentStepIndex = -1;
        IsDownloading = false;
        IsComplete = false;
        ErrorMessage = null;
        foreach (var s in Steps)
        {
            s.Status = ContentStepStatus.Pending;
            s.Progress = 0;
            s.Error = null;
            s.IsIndeterminate = false;
        }
    }
}
