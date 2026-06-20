namespace KAST.Core.Interfaces;

public interface IMissionHttpDownloadService
{
    Task<MissionDownloadResult?> GetDownloadAsync(int instanceId, string fileName, CancellationToken ct = default);
}

public sealed class MissionDownloadResult
{
    public required string PhysicalPath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required uint Hash { get; init; }
    public required DateTimeOffset LastModified { get; init; }
    public required string ETag { get; init; }
}
