using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public sealed class WindowsHostServiceManager(ILogger<WindowsHostServiceManager> logger) : IHostServiceManager
{
    public const string ServiceName = "KAST";
    private const string DisplayName = "KAST Panel";
    private const string Description = "Keelah Arma Server Tool web panel";
    private const int DefaultRestartDelaySeconds = 60;
    private const int DefaultResetFailureDays = 1;

    public async Task<HostServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return Unsupported("KAST service management is only supported on Windows from inside the app.");

        var isAdmin = IsAdministrator();
        var query = await RunScAsync(["query", ServiceName], ct);
        if (query.ExitCode != 0)
        {
            return new HostServiceStatus(
                true,
                isAdmin,
                ServiceName,
                DisplayName,
                HostServiceRunState.NotInstalled,
                HostServiceStartupMode.Manual,
                new HostServiceRecoveryOptions(false, DefaultRestartDelaySeconds, DefaultResetFailureDays),
                ResolveServiceExecutablePath(),
                "KAST is not installed as a Windows service.");
        }

        var qc = await RunScAsync(["qc", ServiceName], ct);
        var failure = await RunScAsync(["qfailure", ServiceName], ct);
        return new HostServiceStatus(
            true,
            isAdmin,
            ServiceName,
            DisplayName,
            ParseState(query.Output),
            ParseStartupMode(qc.Output),
            ParseRecovery(failure.Output),
            ResolveServiceExecutablePath(),
            isAdmin
                ? null
                : "Service changes require running KAST as Administrator.");
    }

    public async Task<HostServiceOperationResult> InstallAsync(HostServiceConfigureRequest request, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "KAST service install is only supported on Windows.", await GetStatusAsync(ct));
        if (!IsAdministrator())
            return new(false, "Run KAST as Administrator to install the Windows service.", await GetStatusAsync(ct));

        var status = await GetStatusAsync(ct);
        if (status.State != HostServiceRunState.NotInstalled)
        {
            var configured = await ConfigureAsync(request, ct);
            return configured with { Message = configured.Success ? "KAST service is already installed. Settings updated." : configured.Message };
        }

        var binPath = BuildServiceBinPath();
        var startArg = ToScStartupMode(request.StartupMode);
        var create = await RunScAsync([
            "create",
            ServiceName,
            "binPath=", binPath,
            "start=", startArg,
            "DisplayName=", DisplayName
        ], ct);
        if (create.ExitCode != 0)
            return new(false, $"Failed to create service: {create.Output}", await GetStatusAsync(ct));

        await RunScAsync(["description", ServiceName, Description], ct);
        var configuredResult = await ApplyRecoveryAsync(request.Recovery, ct);
        if (!configuredResult.Success)
            return configuredResult;

        logger.LogInformation("Installed KAST Windows service at {BinPath}", binPath);
        return new(true, "KAST Windows service installed.", await GetStatusAsync(ct));
    }

    public async Task<HostServiceOperationResult> ConfigureAsync(HostServiceConfigureRequest request, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "KAST service configuration is only supported on Windows.", await GetStatusAsync(ct));
        if (!IsAdministrator())
            return new(false, "Run KAST as Administrator to configure the Windows service.", await GetStatusAsync(ct));

        var status = await GetStatusAsync(ct);
        if (status.State == HostServiceRunState.NotInstalled)
            return new(false, "KAST is not installed as a Windows service.", status);

        var config = await RunScAsync(["config", ServiceName, "start=", ToScStartupMode(request.StartupMode)], ct);
        if (config.ExitCode != 0)
            return new(false, $"Failed to configure startup mode: {config.Output}", await GetStatusAsync(ct));

        return await ApplyRecoveryAsync(request.Recovery, ct);
    }

    public async Task<HostServiceOperationResult> UninstallAsync(CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "KAST service uninstall is only supported on Windows.", await GetStatusAsync(ct));
        if (!IsAdministrator())
            return new(false, "Run KAST as Administrator to uninstall the Windows service.", await GetStatusAsync(ct));

        var status = await GetStatusAsync(ct);
        if (status.State == HostServiceRunState.NotInstalled)
            return new(true, "KAST service is not installed.", status);

        if (status.State is HostServiceRunState.Running or HostServiceRunState.StartPending)
            await RunScAsync(["stop", ServiceName], ct);

        var delete = await RunScAsync(["delete", ServiceName], ct);
        return delete.ExitCode == 0
            ? new HostServiceOperationResult(true, "KAST Windows service removed.", await GetStatusAsync(ct))
            : new HostServiceOperationResult(false, $"Failed to remove service: {delete.Output}", await GetStatusAsync(ct));
    }

    public async Task<HostServiceOperationResult> StartAsync(CancellationToken ct = default)
        => await RunSimpleServiceCommandAsync("start", "KAST Windows service started.", ct);

    public async Task<HostServiceOperationResult> StopAsync(CancellationToken ct = default)
        => await RunSimpleServiceCommandAsync("stop", "KAST Windows service stop requested.", ct);

    private async Task<HostServiceOperationResult> RunSimpleServiceCommandAsync(string command, string successMessage, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "KAST service control is only supported on Windows.", await GetStatusAsync(ct));
        if (!IsAdministrator())
            return new(false, "Run KAST as Administrator to control the Windows service.", await GetStatusAsync(ct));

        var status = await GetStatusAsync(ct);
        if (status.State == HostServiceRunState.NotInstalled)
            return new(false, "KAST is not installed as a Windows service.", status);

        var result = await RunScAsync([command, ServiceName], ct);
        return result.ExitCode == 0
            ? new HostServiceOperationResult(true, successMessage, await GetStatusAsync(ct))
            : new HostServiceOperationResult(false, result.Output, await GetStatusAsync(ct));
    }

    private async Task<HostServiceOperationResult> ApplyRecoveryAsync(HostServiceRecoveryOptions recovery, CancellationToken ct)
    {
        var delayMs = Math.Clamp(recovery.RestartDelaySeconds, 5, 3600) * 1000;
        var resetSeconds = Math.Clamp(recovery.ResetFailureCountAfterDays, 0, 365) * 86400;
        var actions = recovery.RestartOnCrash
            ? $"restart/{delayMs}/restart/{delayMs}/restart/{delayMs}"
            : "";

        var failure = await RunScAsync([
            "failure",
            ServiceName,
            "reset=", resetSeconds.ToString(),
            "actions=", actions
        ], ct);
        if (failure.ExitCode != 0)
            return new(false, $"Failed to configure crash recovery: {failure.Output}", await GetStatusAsync(ct));

        var flag = await RunScAsync(["failureflag", ServiceName, recovery.RestartOnCrash ? "1" : "0"], ct);
        if (flag.ExitCode != 0)
            logger.LogWarning("Failed to configure service failure flag: {Output}", flag.Output);

        return new(true, "KAST Windows service configuration updated.", await GetStatusAsync(ct));
    }

    private static HostServiceStatus Unsupported(string message)
        => new(
            false,
            false,
            ServiceName,
            DisplayName,
            HostServiceRunState.Unsupported,
            HostServiceStartupMode.Manual,
            new HostServiceRecoveryOptions(false, DefaultRestartDelaySeconds, DefaultResetFailureDays),
            ResolveServiceExecutablePath(),
            message);

    private static HostServiceRunState ParseState(string output)
    {
        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return HostServiceRunState.Running;
        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            return HostServiceRunState.Stopped;
        if (output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase))
            return HostServiceRunState.StartPending;
        if (output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase))
            return HostServiceRunState.StopPending;
        if (output.Contains("PAUSED", StringComparison.OrdinalIgnoreCase))
            return HostServiceRunState.Paused;
        return HostServiceRunState.Unknown;
    }

    private static HostServiceStartupMode ParseStartupMode(string output)
    {
        if (output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase))
            return HostServiceStartupMode.Disabled;
        if (output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase))
            return HostServiceStartupMode.Automatic;
        return HostServiceStartupMode.Manual;
    }

    private static HostServiceRecoveryOptions ParseRecovery(string output)
    {
        var restart = output.Contains("RESTART", StringComparison.OrdinalIgnoreCase);
        var delaySeconds = DefaultRestartDelaySeconds;
        var resetDays = DefaultResetFailureDays;

        var delayLine = output.SplitLines()
            .FirstOrDefault(line => line.Contains("RESTART", StringComparison.OrdinalIgnoreCase) &&
                                    line.Contains("Delay", StringComparison.OrdinalIgnoreCase));
        if (delayLine is not null)
        {
            var number = new string(delayLine.Where(char.IsDigit).ToArray());
            if (int.TryParse(number, out var delayMs) && delayMs > 0)
                delaySeconds = Math.Clamp(delayMs / 1000, 5, 3600);
        }

        var resetLine = output.SplitLines()
            .FirstOrDefault(line => line.Contains("RESET_PERIOD", StringComparison.OrdinalIgnoreCase));
        if (resetLine is not null)
        {
            var number = new string(resetLine.Where(char.IsDigit).ToArray());
            if (int.TryParse(number, out var resetSeconds) && resetSeconds >= 0)
                resetDays = Math.Clamp(resetSeconds / 86400, 0, 365);
        }

        return new HostServiceRecoveryOptions(restart, delaySeconds, resetDays);
    }

    private static string ToScStartupMode(HostServiceStartupMode mode)
        => mode switch
        {
            HostServiceStartupMode.Automatic => "auto",
            HostServiceStartupMode.Disabled => "disabled",
            _ => "demand"
        };

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string BuildServiceBinPath()
    {
        var processPath = Environment.ProcessPath;
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("Could not resolve current KAST executable path.");

        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(entryAssembly))
        {
            return $"\"{processPath}\" \"{entryAssembly}\" --service";
        }

        return $"\"{processPath}\" --service";
    }

    private static string? ResolveServiceExecutablePath()
        => string.IsNullOrWhiteSpace(Environment.ProcessPath) ? null : Environment.ProcessPath;

    private static async Task<CommandResult> RunScAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sc.exe.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var output = string.Join(
            Environment.NewLine,
            new[] { await stdout, await stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new CommandResult(process.ExitCode, output.Trim());
    }

    private sealed record CommandResult(int ExitCode, string Output);
}

file static class StringLineExtensions
{
    public static string[] SplitLines(this string value)
        => value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
}
