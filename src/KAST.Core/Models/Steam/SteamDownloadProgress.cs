namespace KAST.Core.Models;

public sealed class SteamDownloadProgress
{
    public double? Percent { get; init; }
    public string? Message { get; init; }
    public uint? DepotId { get; init; }
    public long? BytesTransferred { get; init; }
    public long? TotalBytes { get; init; }
}