using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services.Content;

/// <summary>
/// Installs the Arma 3 dedicated server + Creator DLC depots via SteamKit2.
/// </summary>
public class ServerInstaller(ISteamService steam, IFileSystemService fs, IHttpClientFactory httpClientFactory, ILogger<ServerInstaller> logger, IOutputSanitizer sanitizer) : IContentInstaller
{
    public const uint Arma3ServerAppId = 233780;
    private const string CreatorDlcBranch = "creatordlc";
    private const string DxRedistUrl = "https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe"; // NOSONAR — fixed Microsoft CDN URI for DirectX End-User Runtimes June 2010

    public static readonly (Func<ServerInstance, bool> Enabled, uint DepotId, string? Branch, string Name, string Folder)[] DlcTable =
    [
        (i => i.ContactDlc,  0u,      "contact",  "Contact",              "contact"),
        (i => i.GmDlc,       233792u, null,        "Global Mobilization",  "gm"),
        (i => i.PfDlc,       233794u, null,        "S.O.G. Prairie Fire",  "vn"),
        (i => i.CslaDlc,     233793u, null,        "CSLA Iron Curtain",    "csla"),
        (i => i.WsDlc,       233795u, null,        "Western Sahara",       "ws"),
        (i => i.SpeDlc,      233788u, null,        "Spearhead 1944",       "spe"),
        (i => i.RfDlc,       233799u, null,        "Reaction Forces",      "rf"),
        (i => i.EfDlc,       233798u, null,        "Expeditionary Forces", "ef"),
    ];

    public ContentType Type => ContentType.Server;

    public IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request)
    {
        var steps = new List<ContentStep>
        {
            new() { Name = "Arma 3 Dedicated Server", Detail = $"branch: public → {sanitizer.ToDisplayPath(request.DestinationPath)}" }
        };

        if (request.Instance is null) return steps;

        foreach (var dlc in DlcTable)
        {
            if (!dlc.Enabled(request.Instance)) continue;

            string detail = dlc.Branch is not null ? $"branch: {dlc.Branch}"
                : dlc.DepotId == 0 ? "no server depot"
                : $"depot {dlc.DepotId}";

            steps.Add(new ContentStep { Name = dlc.Name, Detail = detail });
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            steps.Add(new ContentStep { Name = "DirectX", Detail = "DXSETUP.exe /silent" });

        return steps;
    }

    public async Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
    {
        var instance = request.Instance
            ?? throw new InvalidOperationException("ServerInstaller requires an Instance on the request.");
        int maxPar = request.MaxParallelDownloads;

        using var activity = KastActivitySources.Content.StartActivity(
            "kast.server.install", ActivityKind.Internal);
        activity?.SetTag("instance.name",       instance.Name);
        activity?.SetTag("instance.id",         request.ServerInstanceId);
        activity?.SetTag("parallel_downloads",  maxPar);

        try
        {
            if (!steam.IsConnected)
            {
                state.AddLog("Connecting to Steam (anonymous)...");
                logger.LogInformation("Server install [{Instance}]: connecting to Steam anonymously", instance.Name);
                await steam.LoginAnonymousAsync(ct);
            }
            if (!steam.IsConnected)
                throw new InvalidOperationException("Failed to connect to Steam.");

            state.AddLog(steam.IsAuthenticated ? $"Signed in as {steam.CurrentUsername}." : "Connected anonymously.");
            state.AddLog($"Install target: {sanitizer.ToDisplayPath(request.DestinationPath)}.");
            state.AddLog($"Parallel downloads: {maxPar}.");
            logger.LogInformation("Server install [{Instance}]: steam ready, {Auth}, {Par} workers",
                instance.Name,
                steam.IsAuthenticated ? $"authenticated as {steam.CurrentUsername}" : "anonymous",
                maxPar);

        int stepIdx = 0;

        // ── Step 0: Base server (public branch) ──────────────────────────────
        state.BeginStep(stepIdx);
        state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Downloading Arma 3 Dedicated Server (AppId {Arma3ServerAppId}, branch: public)...");
        logger.LogInformation("Server install [{Instance}]: step 1/{Total} — base server (AppId {AppId})",
            instance.Name, state.Steps.Count, Arma3ServerAppId);

        var currentStep = stepIdx;
        var pctProgress = new Progress<double>(pct => state.SetStepProgress(currentStep, pct));
        var logProgress = new Progress<string>(state.AddLog);

        await steam.DownloadAppAsync(Arma3ServerAppId, request.DestinationPath,
            pctProgress, logProgress,
            ignorePlatformFilter: false, branch: "public",
            maxParallelDownloads: maxPar, ct: ct);

        var exe = Path.Combine(request.DestinationPath,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "arma3server_x64.exe" : "arma3server_x64");
        fs.SetExecutable(exe);

        state.CompleteStep(stepIdx);
        state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Arma 3 Dedicated Server complete.");
        logger.LogInformation("Server install [{Instance}]: step 1 complete — base server done", instance.Name);

        // ── Steps 1..N: Creator DLC depots ───────────────────────────────────
        foreach (var dlc in DlcTable)
        {
            if (!dlc.Enabled(instance)) continue;

            stepIdx++;
            string branch = dlc.Branch ?? CreatorDlcBranch;

            if (dlc is { DepotId: 0, Branch: null })
            {
                state.SkipStep(stepIdx, "no server depot available");
                state.AddLog($"⚠ {dlc.Name}: no server depot available — skipping.");
                logger.LogWarning("Server install [{Instance}]: skipping {Dlc} — no server depot", instance.Name, dlc.Name);
                continue;
            }

            state.BeginStep(stepIdx);
            int idx = stepIdx;
            var pct = new Progress<double>(p => state.SetStepProgress(idx, p));
            var log = new Progress<string>(state.AddLog);

            if (dlc.Branch is not null)
            {
                state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Downloading {dlc.Name} (branch: {branch}, all depots)...");
                logger.LogInformation("Server install [{Instance}]: step {Step}/{Total} — {Dlc} (branch: {Branch})",
                    instance.Name, stepIdx + 1, state.Steps.Count, dlc.Name, branch);
                await steam.DownloadAppAsync(Arma3ServerAppId, request.DestinationPath,
                    pct, log, ignorePlatformFilter: false, branch: branch,
                    maxParallelDownloads: maxPar, ct: ct);
            }
            else
            {
                state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Downloading {dlc.Name} (depot {dlc.DepotId}, branch: {branch})...");
                logger.LogInformation("Server install [{Instance}]: step {Step}/{Total} — {Dlc} (depot {DepotId}, branch: {Branch})",
                    instance.Name, stepIdx + 1, state.Steps.Count, dlc.Name, dlc.DepotId, branch);
                await steam.DownloadAppAsync(Arma3ServerAppId, request.DestinationPath,
                    pct, log, ignorePlatformFilter: true, branch: branch,
                    depotFilter: [dlc.DepotId], maxParallelDownloads: maxPar, ct: ct);
            }

            state.CompleteStep(stepIdx);
            state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] {dlc.Name} complete.");
            logger.LogInformation("Server install [{Instance}]: step {Step}/{Total} complete — {Dlc}",
                instance.Name, stepIdx + 1, state.Steps.Count, dlc.Name);
        }

        // ── DirectX (Windows only) ────────────────────────────────────────────────
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            await InstallDirectXAsync(request, state, stepIdx + 1, ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, sanitizer.Sanitize(ex.Message));
            throw;
        }
    }

    public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
    {
        var results = new List<ContentValidationResult>();
        var installPath = request.DestinationPath;

        bool dirExists = Directory.Exists(installPath);
        var displayPath = sanitizer.ToDisplayPath(installPath);
        results.Add(new("Install directory", dirExists, dirExists ? displayPath : $"Not found: {displayPath}"));

        if (!dirExists) return results;

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "arma3server_x64.exe" : "arma3server_x64";
        var exePath = Path.Combine(installPath, exeName);
        results.Add(new("Server executable", File.Exists(exePath), sanitizer.ToDisplayPath(exePath)));

        results.AddRange(new[] { "addons", "dta", "keys" }.Select(dir =>
            new ContentValidationResult(dir, Directory.Exists(Path.Combine(installPath, dir)), sanitizer.ToDisplayPath(Path.Combine(installPath, dir)))));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(exePath))
        {
            var mode = File.GetUnixFileMode(exePath);
            results.Add(new("Executable permission (+x)", mode.HasFlag(UnixFileMode.UserExecute)));
        }

        if (request.Instance is not null)
        {
            results.AddRange(DlcTable
                .Where(d => d.Enabled(request.Instance))
                .Select(dlc => new ContentValidationResult(
                    $"{dlc.Name} ({dlc.Folder}/)",
                    Directory.Exists(Path.Combine(installPath, dlc.Folder)),
                    sanitizer.ToDisplayPath(Path.Combine(installPath, dlc.Folder)))));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string sys   = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string sys86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            string? found = new[]
            {
                Path.Combine(sys,   "D3DX9_43.dll"),
                Path.Combine(sys86, "D3DX9_43.dll"),
                Path.Combine(sys,   "XINPUT1_3.dll"),
            }.FirstOrDefault(File.Exists);
            results.Add(new("DirectX", found is not null,
                found is not null ? "Installed" : "Required DirectX files were not found"));
        }

        return results;
    }

    [SupportedOSPlatform("windows")]
    private async Task InstallDirectXAsync(ContentInstallRequest request, ContentInstallState state, int stepIdx, CancellationToken ct)
    {
        var instance = request.Instance!;
        state.BeginStep(stepIdx);
        state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Checking DirectX...");
        logger.LogInformation("Server install [{Instance}]: step {Step}/{Total} — DirectX",
            instance.Name, stepIdx + 1, state.Steps.Count);

        if (IsDirectXInstalled())
        {
            state.SkipStep(stepIdx, "already installed");
            state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] DirectX already installed — skipping.");
            logger.LogInformation("Server install [{Instance}]: DirectX already installed, skipping", instance.Name);
            return;
        }

        var tempRedist  = Path.Combine(Path.GetTempPath(), "kast_directx_Jun2010_redist.exe");
        var tempExtract = Path.Combine(Path.GetTempPath(), "kast_dxredist");
        try
        {
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                // KAST runs as a web application — there is no interactive desktop session
                // for a UAC prompt to appear on. Fail early with a clear remediation message
                // rather than hanging on a dialog the remote user can never see.
                const string msg = "DirectX setup requires administrator privileges. "
                                 + "Restart KAST as an administrator and re-run the server install.";
                state.FailStep(stepIdx, msg);
                state.AddLog($"⚠ {msg}");
                logger.LogError("Server install [{Instance}]: DirectX setup aborted — process is not elevated", instance.Name);
                return;
            }

            // ── Download ──────────────────────────────────────────────────────────
            state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Downloading DirectX End-User Runtimes (June 2010)...");
            logger.LogInformation("Server install [{Instance}]: downloading DirectX June 2010 redistributable", instance.Name);

            using var http = httpClientFactory.CreateClient();
            await DownloadFileWithProgressAsync(http, DxRedistUrl, tempRedist,
                pct => state.SetStepProgress(stepIdx, pct), ct);
            // file is closed inside helper before returning

            state.SetStepIndeterminate(stepIdx, true);

            // ── Extract ───────────────────────────────────────────────────────────
            // directx_Jun2010_redist.exe is an IExpress self-extractor.
            // /T sets the extraction directory; /C extracts without launching setup.
            state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Extracting DirectX setup files...");
            logger.LogInformation("Server install [{Instance}]: extracting DirectX redistributable", instance.Name);

            Directory.CreateDirectory(tempExtract);
            using (var extract = Process.Start(new ProcessStartInfo(tempRedist, $"/Q /C /T:\"{tempExtract}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Failed to start DirectX self-extractor."))
                await extract.WaitForExitAsync(ct);

            // ── Install ───────────────────────────────────────────────────────────
            var dxSetup = Path.Combine(tempExtract, "DXSETUP.exe");
            if (!File.Exists(dxSetup))
                throw new FileNotFoundException("DXSETUP.exe not found after extraction.", dxSetup);

            state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Running DXSETUP.exe /silent...");
            logger.LogInformation("Server install [{Instance}]: running DXSETUP.exe /silent", instance.Name);

            using var proc = Process.Start(new ProcessStartInfo(dxSetup, "/silent")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Failed to start DXSETUP.exe.");
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode == 0)
            {
                if (IsDirectXInstalled())
                {
                    state.CompleteStep(stepIdx);
                    state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] DirectX complete.");
                    logger.LogInformation("Server install [{Instance}]: DirectX setup complete", instance.Name);
                }
                else
                {
                    state.FailStep(stepIdx, "DXSETUP.exe reported success but required DirectX files were not found.");
                    state.AddLog("⚠ Required DirectX files were not found after DXSETUP.exe.");
                    logger.LogError("Server install [{Instance}]: DXSETUP.exe exited 0 but required DirectX files are absent", instance.Name);
                }
            }
            else
            {
                state.FailStep(stepIdx, $"DXSETUP.exe exited with code {proc.ExitCode}");
                state.AddLog($"⚠ DirectX setup failed (exit code {proc.ExitCode}).");
                logger.LogError("Server install [{Instance}]: DXSETUP.exe failed with exit code {ExitCode}",
                    instance.Name, proc.ExitCode);
            }
        }
        finally
        {
            if (File.Exists(tempRedist))           File.Delete(tempRedist);
            if (Directory.Exists(tempExtract))     Directory.Delete(tempExtract, recursive: true);
        }
    }

    public static bool IsDirectXInstalled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return true;

        // Use SpecialFolder.System (= System32 on 64-bit processes) and
        // SpecialFolder.SystemX86 (= SysWOW64) rather than constructing paths
        // from the Windows directory to correctly handle WOW64 redirection.
        string sys   = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string sys86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        return File.Exists(Path.Combine(sys,   "D3DX9_43.dll"))
            || File.Exists(Path.Combine(sys86, "D3DX9_43.dll"))
            || File.Exists(Path.Combine(sys,   "XINPUT1_3.dll"));
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destPath"/> and reports
    /// percentage progress via <paramref name="onProgress"/>.
    /// The destination file is fully closed before this method returns.
    /// </summary>
    private static async Task DownloadFileWithProgressAsync(
        HttpClient http, string url, string destPath,
        Action<double> onProgress, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength;

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(destPath);

        if (totalBytes is > 0)
        {
            var buf = new byte[81920]; // 80 KB chunks
            long downloaded = 0;
            int read;
            while ((read = await responseStream.ReadAsync(buf, ct)) > 0)
            {
                await fileStream.WriteAsync(buf.AsMemory(0, read), ct);
                downloaded += read;
                onProgress(downloaded * 100.0 / totalBytes.Value);
            }
        }
        else
        {
            await responseStream.CopyToAsync(fileStream, ct);
        }
    } // responseStream + fileStream disposed here — destPath fully released
}
