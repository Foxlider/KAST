using KAST.Core.Models;

namespace KAST.Core.Interfaces;

public interface IProcessManagerService
{
    Task<int> StartServerProcessAsync(string executablePath, string arguments,
        Action<int, string>? onOutputLine = null, Action<int, int>? onProcessExited = null,
        CancellationToken ct = default);
    Task StopProcessAsync(int processId, CancellationToken ct = default);
    Task<bool> KillProcessAsync(int processId, CancellationToken ct = default);
    bool IsProcessRunning(int processId);
    Task<(double CpuPercent, long MemoryBytes)?> GetProcessMetricsAsync(int processId, CancellationToken ct = default);
    Task<IReadOnlyList<RunningProcessInfo>> GetRunningServerProcessesAsync(CancellationToken ct = default);
}
