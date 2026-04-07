using System.Diagnostics;
using KAST.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class ProcessManagerService(ILogger<ProcessManagerService> logger) : IProcessManagerService
{
    public async Task<int> StartServerProcessAsync(string executablePath, string arguments, CancellationToken ct = default)
    {
        logger.LogInformation("Starting process: {Executable} {Args}", executablePath, arguments);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {executablePath}");

        logger.LogInformation("Process started with PID {Pid}", process.Id);

        // Don't await the process — it runs in background
        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync(ct);
            logger.LogInformation("Process {Pid} exited with code {Code}", process.Id, process.ExitCode);
        }, ct);

        return await Task.FromResult(process.Id);
    }

    public async Task StopProcessAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            logger.LogInformation("Stopping process {Pid}", processId);

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Process {Pid} is not running", processId);
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("Process {Pid} has already exited", processId);
        }
    }

    public bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(double CpuPercent, long MemoryBytes)?> GetProcessMetricsAsync(int processId, CancellationToken ct = default)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return null;

            var startCpu = process.TotalProcessorTime;
            var startTime = DateTime.UtcNow;

            await Task.Delay(500, ct);

            var endCpu = process.TotalProcessorTime;
            var endTime = DateTime.UtcNow;

            var cpuUsed = (endCpu - startCpu).TotalMilliseconds;
            var elapsed = (endTime - startTime).TotalMilliseconds;
            var cpuPercent = cpuUsed / (Environment.ProcessorCount * elapsed) * 100;

            return (cpuPercent, process.WorkingSet64);
        }
        catch
        {
            return null;
        }
    }
}
