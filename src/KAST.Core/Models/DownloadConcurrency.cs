namespace KAST.Core.Models;

/// <summary>
/// Shared defaults and supported ranges for download concurrency settings.
/// </summary>
public static class DownloadConcurrency
{
    public const int MinimumSteamWorkers = 1;
    public const int DefaultSteamWorkers = 8;
    public const int MaximumSteamWorkers = 128;

    public const int MinimumBulkModDownloads = 1;
    public const int DefaultBulkModDownloads = 4;
    public const int MaximumBulkModDownloads = 16;

    public static IReadOnlyList<int> BenchmarkLevels { get; } =
        [1, 2, 4, 8, 16, 32, 64, MaximumSteamWorkers];
}
