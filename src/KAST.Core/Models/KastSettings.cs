using System.ComponentModel.DataAnnotations;

namespace KAST.Core.Models;

/// <summary>
/// Application-level settings persisted in the database.
/// Environment variables override DB values at runtime (not persisted back).
/// </summary>
public class KastSettings
{
    [Key]
    public int Id { get; set; } = 1; // Singleton row

    public string ModsDirectory { get; set; } = "./mods";
    public string ServersDirectory { get; set; } = "./servers";
    public int Arma3ServerAppId { get; set; } = 233780;
    public string ThemeMode { get; set; } = "dark"; // "dark" or "light"
    public int MetricsIntervalSeconds { get; set; } = 5;
    public int ParallelDownloads { get; set; } = DownloadConcurrency.DefaultSteamWorkers;
    public int BulkModDownloadConcurrency { get; set; } = DownloadConcurrency.DefaultBulkModDownloads;
}
