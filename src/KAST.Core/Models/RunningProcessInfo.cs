namespace KAST.Core.Models;

public sealed record RunningProcessInfo(
    int Pid,
    string ProcessName,
    DateTime StartTime,
    double CpuPercent,
    long MemoryBytes,
    string? CommandLine,
    int? Port,
    bool IsManaged,
    int? LinkedInstanceId,
    string? LinkedInstanceName
);
