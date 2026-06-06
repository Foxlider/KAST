namespace KAST.Core.Models;

public enum StorageCandidateKind
{
    Server,
    Mod
}

public enum StorageCandidateStatus
{
    New,
    IdenticalDuplicate,
    SameNameDifferentData
}

public enum StorageResolutionAction
{
    Skip,
    KeepCurrent,
    UseDiscovered,
    AdoptDiscovered
}

public sealed record StorageScanRequest(string ModsDirectory, string ServersDirectory);

public sealed record StorageApplyRequest(
    string ModsDirectory,
    string ServersDirectory,
    IReadOnlyList<StorageResolutionSelection> Resolutions);

public sealed record StorageMigrationRequest(string ModsDirectory, string ServersDirectory);

public sealed record StorageResolutionSelection(
    StorageCandidateKind Kind,
    string DiscoveredPath,
    StorageResolutionAction Action,
    int? ExistingId = null);

public sealed record StorageScanResult(
    string CurrentModsDirectory,
    string CurrentServersDirectory,
    string ProposedModsDirectory,
    string ProposedServersDirectory,
    IReadOnlyList<StorageScanCandidate> Servers,
    IReadOnlyList<StorageScanCandidate> Mods)
{
    public bool HasFindings => Servers.Count > 0 || Mods.Count > 0;
    public bool HasConflicts => Servers.Any(c => c.Status != StorageCandidateStatus.New) ||
                                Mods.Any(c => c.Status != StorageCandidateStatus.New);
}

public sealed record StorageScanCandidate
{
    public required StorageCandidateKind Kind { get; init; }
    public required StorageCandidateStatus Status { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Fingerprint { get; init; }
    public string? Identity { get; init; }
    public long SizeBytes { get; init; }
    public long WorkshopId { get; init; }
    public int? ExistingId { get; init; }
    public string? ExistingPath { get; init; }
    public string? Detail { get; init; }
}

public sealed record StorageApplyResult(
    int ServersAdopted,
    int ServersSwitched,
    int ModsAdopted,
    int ModsSwitched,
    bool SettingsSaved);

public sealed record StorageMigrationResult(
    int ServersMigrated,
    int ModsMigrated,
    IReadOnlyList<string> Skipped,
    string ModsDirectory,
    string ServersDirectory);
