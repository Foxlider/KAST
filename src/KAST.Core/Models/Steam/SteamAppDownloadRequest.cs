namespace KAST.Core.Models;

/// <summary>
/// Specifies the content and depot selection for an application download from Steam.
/// </summary>
public sealed class SteamAppDownloadRequest
{
    public required uint AppId { get; init; }
    public required string DestinationPath { get; init; }
    public bool IgnorePlatformFilter { get; init; }
    public string Branch { get; init; } = "public";
    public IReadOnlyCollection<uint>? DepotFilter { get; init; }
    public int MaxParallelDownloads { get; init; } = DownloadConcurrency.DefaultSteamWorkers;
}