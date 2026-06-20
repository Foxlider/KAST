namespace KAST.Core.Interfaces;

public interface IMissionHashService
{
    Task<uint> ComputeHashAsync(string filePath, CancellationToken ct = default);
}
