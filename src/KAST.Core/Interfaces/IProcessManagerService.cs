namespace KAST.Core.Interfaces;

public interface IProcessManagerService
{
    Task<int> StartServerProcessAsync(string executablePath, string arguments, CancellationToken ct = default);
    Task StopProcessAsync(int processId, CancellationToken ct = default);
    bool IsProcessRunning(int processId);
    Task<(double CpuPercent, long MemoryBytes)?> GetProcessMetricsAsync(int processId, CancellationToken ct = default);
}
