using System.Diagnostics;
using System.Text.RegularExpressions;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public partial class ProcessManagerService(ILogger<ProcessManagerService> logger, KastDbContext db) : IProcessManagerService
{
    public async Task<int> StartServerProcessAsync(string executablePath, string arguments,
        Action<int, string>? onOutputLine = null, Action<int, int>? onProcessExited = null,
        CancellationToken ct = default)
    {
        using var activity = KastActivitySources.Process.StartActivity(
            "kast.process.start", ActivityKind.Internal);

        logger.LogInformation("Starting server process");

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
            ?? throw new InvalidOperationException("Failed to start server process.");

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

        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Process {Pid} is not running", processId);
            return;
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("Process {Pid} has already exited", processId);
            return;
        }

        logger.LogInformation("Stopping process {Pid}", processId);

        if (!process.HasExited)
        {
            try { process.CloseMainWindow(); } catch { /* best effort */ }

            using var graceCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, graceCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogInformation("Process {Pid} did not exit gracefully, force-killing", processId);
            }
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(ct);
        }
    }

    public async Task<bool> KillProcessAsync(int processId, CancellationToken ct = default)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("KillProcess: PID {Pid} is not running", processId);
            return true;
        }
        catch (InvalidOperationException)
        {
            logger.LogWarning("KillProcess: PID {Pid} has already exited", processId);
            return true;
        }

        if (process.HasExited)
            return true;

        logger.LogInformation("KillProcess: Force-killing process {Pid}", processId);
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(ct);
        return process.HasExited;
    }

    internal static readonly string[] KnownServerProcessNames =
    [
        "arma3server_x64",
        "arma3server",
        "arma3server_x64.exe",
        "arma3server.exe"
    ];

    public bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return false;

            var name = process.ProcessName;
            return KnownServerProcessNames.Any(n =>
                name.StartsWith(n, StringComparison.OrdinalIgnoreCase));
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

    public async Task<IReadOnlyList<RunningProcessInfo>> GetRunningServerProcessesAsync(CancellationToken ct = default)
    {
        var managedPids = await db.ServerInstances
            .Where(s => s.ProcessId != null)
            .Select(s => new { s.Id, s.Name, s.ProcessId })
            .ToDictionaryAsync(s => s.ProcessId!.Value, s => (s.Id, s.Name), ct);

        var result = new List<RunningProcessInfo>();

        foreach (var procName in KnownServerProcessNames)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(procName); }
            catch { continue; }

            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;

                    string? cmdLine = null;
                    try { cmdLine = GetProcessCommandLine(process); }
                    catch { }

                    int? port = null;
                    if (cmdLine != null)
                    {
                        var portMatch = PortRegex().Match(cmdLine);
                        if (portMatch.Success) port = int.Parse(portMatch.Groups[1].Value);
                    }

                    var isManaged = managedPids.TryGetValue(process.Id, out var link);
                    double cpuPercent = 0;
                    try
                    {
                        var metrics = await GetProcessMetricsAsync(process.Id, ct);
                        if (metrics.HasValue) cpuPercent = metrics.Value.CpuPercent;
                    }
                    catch { }

                    result.Add(new RunningProcessInfo(
                        process.Id,
                        process.ProcessName,
                        process.StartTime,
                        cpuPercent,
                        process.WorkingSet64,
                        cmdLine,
                        port,
                        isManaged,
                        isManaged ? link.Id : null,
                        isManaged ? link.Name : null));
                }
                catch
                {
                    // Process may have exited between enumeration and query
                }
            }
        }

        return result;
    }

    private static string? GetProcessCommandLine(Process process)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var cmdlinePath = $"/proc/{process.Id}/cmdline";
                if (File.Exists(cmdlinePath))
                {
                    var bytes = File.ReadAllBytes(cmdlinePath);
                    // /proc/pid/cmdline uses null bytes as separators
                    var args = System.Text.Encoding.UTF8.GetString(bytes)
                        .Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    return string.Join(" ", args);
                }
            }
        }
        catch { }

        return null;
    }

    [GeneratedRegex(@"-port=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PortRegex();
}
