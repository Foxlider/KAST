using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface ISteamDownloadBenchmarkService
{
    /// <summary>
    /// Downloads a sample of chunks from the Arma 3 DS depot at varying parallelism levels
    /// and returns throughput measurements so the user can pick the optimal setting.
    /// </summary>
    Task<IReadOnlyList<BenchmarkResult>> BenchmarkDownloadAsync(IProgress<string>? log = null, CancellationToken ct = default);
}