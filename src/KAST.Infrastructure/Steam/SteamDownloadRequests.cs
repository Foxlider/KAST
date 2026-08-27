using System.Diagnostics;
using SteamKit2;

namespace KAST.Infrastructure.Steam;

internal sealed class DepotDownloadRequest
{
    public required uint DepotId { get; init; }
    public byte[]? DepotKey { get; init; }
    public required DepotManifest Manifest { get; init; }
    public required CdnServerPool Pool { get; init; }
    public required string DestinationPath { get; init; }
    public required long TotalSize { get; init; }
    public required long TotalDownloaded { get; init; }
    public IProgress<double>? Progress { get; init; }
    public IProgress<string>? LogProgress { get; init; }
    public required int MaxParallelDownloads { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}

internal sealed class FileDownloadRequest
{
    public required IList<DepotManifest.FileData> Files { get; init; }
    public required uint DepotId { get; init; }
    public byte[]? DepotKey { get; init; }
    public required CdnServerPool Pool { get; init; }
    public required string DestinationPath { get; init; }
    public required ActivityContext ParentSpanContext { get; init; }
    public required long ProgressBase { get; init; }
    public required long ProgressTotal { get; init; }
    public IProgress<double>? Progress { get; init; }
    public IProgress<string>? LogProgress { get; init; }
    public required string LogPrefix { get; init; }
    public required int MaxParallelWorkers { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}

internal sealed record DepotVerificationResult(
    List<DepotManifest.FileData> FilesToDownload,
    int VerifiedCount,
    long VerifiedBytes);