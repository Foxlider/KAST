using System.Diagnostics;
using KAST.Core.Interfaces;
using KAST.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public class ProcessManagerService(ILogger<ProcessManagerService> logger) : IProcessManagerService
{
    public async Task<int> StartServerProcessAsync(string executablePath, string arguments,
        Action<int, string>? onOutputLine = null, Action<int, int>? onProcessExited = null,
        CancellationToken ct = default)
    {
        using var activity = KastActivitySources.Process.StartActivity(
            "kast.process.start", ActivityKind.Internal);
        activity?.SetTag("process.executable", executablePath);

        logger.LogInformation("Starting process: {Executable} {Args}", executablePath, arguments);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {executablePath}");

        logger.LogInformation("Process started with PID {Pid}", process.Id);
        activity?.SetTag("process.pid", process.Id);

        var pid = process.Id;

        // Stream stdout/stderr lines to the callback
        if (onOutputLine is not null)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) onOutputLine(pid, e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) onOutputLine(pid, e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        // Monitor for exit and invoke callback
        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync(CancellationToken.None);
            logger.LogInformation("Process {Pid} exited with code {Code}", pid, process.ExitCode);
            onProcessExited?.Invoke(pid, process.ExitCode);
        }, CancellationToken.None);

        return await Task.FromResult(pid);
    }

    public async Task StopProcessAsync(int processId, CancellationToken ct = default)
    {
        using var activity = KastActivitySources.Process.StartActivity(
            "kast.process.stop", ActivityKind.Internal);
        activity?.SetTag("process.pid", processId);

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
