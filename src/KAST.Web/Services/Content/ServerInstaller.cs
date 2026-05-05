using System.Runtime.InteropServices;
using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Web.Services.Content;

/// <summary>
/// Installs Arma 3 dedicated server files: base game on public branch,
/// then each enabled Creator DLC on the creatordlc branch (or its own branch).
/// Replaces the old ServerInstallService.
/// </summary>
public class ServerInstaller(ISteamService steam, IFileSystemService fs) : IContentInstaller
{
    public const uint Arma3ServerAppId = 233780;
    private const string CreatorDlcBranch = "creatordlc";

    /// <summary>
    /// DLC metadata. Most DLCs live on the <c>creatordlc</c> branch filtered by depot ID.
    /// Contact is special: it uses its own <c>contact</c> branch (base depots carry the content).
    /// DepotId 0 + no Branch override means the DLC has no server depot yet.
    /// </summary>
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
            new() { Name = "Arma 3 Dedicated Server", Detail = "branch: public" }
        };

        if (request.Instance is null) return steps;

        foreach (var dlc in DlcTable)
        {
            if (!dlc.Enabled(request.Instance)) continue;

            string detail;
            if (dlc.Branch is not null)
                detail = $"branch: {dlc.Branch}";
            else if (dlc.DepotId == 0)
                detail = "no server depot";
            else
                detail = $"depot {dlc.DepotId}";

            steps.Add(new ContentStep { Name = dlc.Name, Detail = detail });
        }

        return steps;
    }

    public async Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
    {
        var instance = request.Instance
            ?? throw new InvalidOperationException("ServerInstaller requires an Instance on the request.");
        int maxPar = request.MaxParallelDownloads;

        // Ensure Steam connection
        if (!steam.IsConnected)
        {
            state.AddLog("Connecting to Steam (anonymous)...");
            await steam.LoginAnonymousAsync(ct);
        }
        if (!steam.IsConnected)
            throw new InvalidOperationException("Failed to connect to Steam.");

        state.AddLog(steam.IsAuthenticated
            ? $"Signed in as {steam.CurrentUsername}."
            : "Connected anonymously.");
        state.AddLog($"Parallel downloads: {maxPar}.");

        int stepIdx = 0;

        // ── Step 0: Base dedicated server (public branch) ────────────────────
        state.BeginStep(stepIdx);
        state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Downloading Arma 3 Dedicated Server (AppId {Arma3ServerAppId}, branch: public)...");

        var percentProgress = new Progress<double>(pct => state.SetStepProgress(stepIdx, pct));
        var logProgress     = new Progress<string>(line => state.AddLog(line));

        await steam.DownloadAppAsync(Arma3ServerAppId, request.DestinationPath,
            percentProgress, logProgress,
            ignorePlatformFilter: false, branch: "public",
            maxParallelDownloads: maxPar, ct: ct);

        // Post-download: set executable bit
        var exe = Path.Combine(request.DestinationPath,
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "arma3server_x64.exe" : "arma3server_x64");
        fs.SetExecutable(exe);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(exe))
            state.AddLog("Set executable permissions on arma3server_x64.");

        state.CompleteStep(stepIdx);
        state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Arma 3 Dedicated Server complete.");

        // ── Steps 1..N: Creator DLC depots ───────────────────────────────────
        foreach (var dlc in DlcTable)
        {
            if (!dlc.Enabled(instance)) continue;

            stepIdx++;
            string branch = dlc.Branch ?? CreatorDlcBranch;

            // Skip DLCs with no depot and no branch override
            if (dlc.DepotId == 0 && dlc.Branch is null)
            {
                state.SkipStep(stepIdx, "no server depot available");
                state.AddLog($"⚠ {dlc.Name}: no server depot available — skipping.");
                continue;
            }

            state.BeginStep(stepIdx);

            // Capture stepIdx for the closures
            int idx = stepIdx;
            var pct = new Progress<double>(p => state.SetStepProgress(idx, p));
            var log = new Progress<string>(line => state.AddLog(line));

            if (dlc.Branch is not null)
            {
                // DLC has its own branch — download all base depots from that branch
                state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Downloading {dlc.Name} (branch: {branch}, all depots)...");
                await steam.DownloadAppAsync(Arma3ServerAppId, request.DestinationPath,
                    pct, log, ignorePlatformFilter: false, branch: branch,
                    maxParallelDownloads: maxPar, ct: ct);
            }
            else
            {
                // Standard depot filter on creatordlc branch
                state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] Downloading {dlc.Name} (depot {dlc.DepotId}, branch: {branch})...");
                await steam.DownloadAppAsync(Arma3ServerAppId, request.DestinationPath,
                    pct, log, ignorePlatformFilter: true, branch: branch,
                    depotFilter: [dlc.DepotId], maxParallelDownloads: maxPar, ct: ct);
            }

            state.CompleteStep(stepIdx);
            state.AddLog($"[Step {stepIdx + 1}/{state.Steps.Count}] {dlc.Name} complete.");
        }
    }

    public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
    {
        var results = new List<ContentValidationResult>();
        var installPath = request.DestinationPath;

        bool dirExists = Directory.Exists(installPath);
        results.Add(new("Install directory", dirExists, dirExists ? installPath : "Not found"));

        if (!dirExists) return results;

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "arma3server_x64.exe" : "arma3server_x64";
        var exePath = Path.Combine(installPath, exeName);
        results.Add(new("Server executable", File.Exists(exePath), exeName));

        foreach (var dir in new[] { "addons", "dta", "keys" })
            results.Add(new(dir, Directory.Exists(Path.Combine(installPath, dir))));

        // Executable bit on Linux
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(exePath))
        {
            var mode = File.GetUnixFileMode(exePath);
            results.Add(new("Executable permission (+x)", mode.HasFlag(UnixFileMode.UserExecute)));
        }

        // DLC subfolder checks
        if (request.Instance is not null)
        {
            foreach (var dlc in DlcTable.Where(d => d.Enabled(request.Instance)))
                results.Add(new($"{dlc.Name} ({dlc.Folder}/)", Directory.Exists(Path.Combine(installPath, dlc.Folder))));
        }

        return results;
    }
}
