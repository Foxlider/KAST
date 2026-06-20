using KAST.Core.Hashing;
using KAST.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class MissionHashService(ILogger<MissionHashService> logger) : IMissionHashService
{
    public async Task<uint> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await Crc32Bzip2.ComputeAsync(stream, ct);
        logger.LogDebug("Computed CRC32/BZIP2 hash {Hash} for {Path}", hash, filePath);
        return hash;
    }
}
