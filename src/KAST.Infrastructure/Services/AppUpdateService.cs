using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KAST.Infrastructure.Services;

public partial class AppUpdateService(
    HttpClient httpClient,
    IHostApplicationLifetime applicationLifetime,
    ILogger<AppUpdateService> logger) : IAppUpdateService
{
    private const string StableChannelId = "stable";
    private const string DevChannelId = "dev";
    private const string CasterChannelId = "caster";
    private const string GitHubApiBase = "https://api.github.com";

    private static readonly AppUpdateChannel[] Channels =
    [
        new(CasterChannelId, "Caster", "bluefield-creator", "KAST", AppUpdateReleaseSelection.LatestStable, null, false),
        new(DevChannelId, "Dev", "Foxlider", "KAST", AppUpdateReleaseSelection.Tag, "nightly", true),
        new(StableChannelId, "Stable", "Foxlider", "KAST", AppUpdateReleaseSelection.LatestStable, null, false)
    ];

    public IReadOnlyList<AppUpdateChannel> GetChannels() => Channels;

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(string channelId, CancellationToken ct = default)
    {
        var channel = GetChannel(channelId);
        var rid = GetCurrentRuntimeIdentifier();
        var currentVersion = GetCurrentVersion();
        var isDocker = IsDocker();

        if (rid is null)
        {
            return Unavailable(
                channel,
                currentVersion,
                "This platform is not supported by the native updater.",
                runtimeIdentifier: "unsupported",
                isDocker: isDocker);
        }

        var release = await FetchReleaseAsync(channel, ct);
        if (release is null)
        {
            return Unavailable(
                channel,
                currentVersion,
                $"{channel.Label} does not have an available GitHub release yet.",
                rid,
                isDocker);
        }

        var asset = SelectAsset(release.Assets, rid, channel.IsPrerelease);
        if (asset is null)
        {
            return new AppUpdateCheckResult(
                channel,
                currentVersion,
                rid,
                IsNativeSupported: !isDocker,
                IsDocker: isDocker,
                IsChannelAvailable: false,
                IsUpdateAvailable: false,
                Message: $"No {rid} update asset was found for {channel.Label}.",
                release.TagName,
                release.Name,
                ExtractReleaseVersion(release),
                release.PublishedAt,
                release.HtmlUrl,
                Asset: null);
        }

        var releaseVersion = ExtractReleaseVersion(release);
        var updateAvailable = IsUpdateAvailable(currentVersion, releaseVersion, release.TagName);
        var message = isDocker
            ? "KAST is running in Docker. Download is available, but updates must be applied by changing the container image."
            : updateAvailable
                ? "Update available."
                : "Already on this release.";

        return new AppUpdateCheckResult(
            channel,
            currentVersion,
            rid,
            IsNativeSupported: !isDocker,
            IsDocker: isDocker,
            IsChannelAvailable: true,
            IsUpdateAvailable: updateAvailable,
            message,
            release.TagName,
            release.Name,
            releaseVersion,
            release.PublishedAt,
            release.HtmlUrl,
            ToAsset(asset));
    }

    public async Task<AppUpdateDownloadResult> DownloadUpdateAsync(
        string channelId,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken ct = default)
    {
        var check = await CheckForUpdatesAsync(channelId, ct);
        if (check.IsDocker)
            return new(false, "Docker deployments are notify-only. Update the container image outside KAST.", null, null, false);
        if (!check.IsChannelAvailable || check.Asset is null)
            return new(false, check.Message, null, null, false);

        progress?.Report(new(AppUpdateStage.Preparing, null, "Preparing update download..."));

        var updateId = $"{check.Channel.Id}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var updatesRoot = Path.Combine(AppContext.BaseDirectory, "updates");
        var updateRoot = Path.Combine(updatesRoot, updateId);
        var downloadPath = Path.Combine(updateRoot, check.Asset.Name);
        var extractPath = Path.Combine(updateRoot, "extracted");

        Directory.CreateDirectory(updateRoot);
        Directory.CreateDirectory(extractPath);

        progress?.Report(new(AppUpdateStage.Downloading, 0, $"Downloading {check.Asset.Name}..."));
        await DownloadFileAsync(check.Asset.DownloadUrl, downloadPath, check.Asset.SizeBytes, progress, ct);

        progress?.Report(new(AppUpdateStage.Verifying, null, "Verifying downloaded archive..."));
        var checksumVerified = await TryVerifyChecksumAsync(check.Channel, check.Asset, downloadPath, ct);
        if (!checksumVerified)
        {
            return new(
                false,
                "Update checksum asset was not found. The update was downloaded but will not be staged.",
                null,
                null,
                false);
        }

        progress?.Report(new(AppUpdateStage.Extracting, null, "Extracting update archive..."));
        ExtractArchive(downloadPath, extractPath);

        var markerPath = Path.Combine(updateRoot, "staged.txt");
        await File.WriteAllTextAsync(markerPath, check.Channel.Id, ct);

        progress?.Report(new(AppUpdateStage.Staged, 100, "Update staged."));
        return new(true, checksumVerified ? "Update downloaded and verified." : "Update downloaded and staged.", updateId, extractPath, checksumVerified);
    }

    public Task<AppUpdateApplyResult> ApplyStagedUpdateAsync(string stagedUpdateId, CancellationToken ct = default)
    {
        if (IsDocker())
            return Task.FromResult(new AppUpdateApplyResult(false, "Docker deployments are notify-only."));
        if (string.IsNullOrWhiteSpace(stagedUpdateId) || stagedUpdateId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return Task.FromResult(new AppUpdateApplyResult(false, "Invalid staged update id."));

        var updateRoot = Path.Combine(AppContext.BaseDirectory, "updates", stagedUpdateId);
        var extractPath = Path.Combine(updateRoot, "extracted");
        if (!Directory.Exists(extractPath))
            return Task.FromResult(new AppUpdateApplyResult(false, "Staged update files were not found."));

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return Task.FromResult(new AppUpdateApplyResult(false, "Could not resolve the current KAST executable path."));

        var scriptPath = OperatingSystem.IsWindows()
            ? Path.Combine(updateRoot, "apply-update.cmd")
            : Path.Combine(updateRoot, "apply-update.sh");
        var backupPath = Path.Combine(
            Path.GetTempPath(),
            "kast-update-backups",
            "backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
            File.WriteAllText(scriptPath, BuildWindowsApplyScript(pid, extractPath, AppContext.BaseDirectory, backupPath, processPath), Encoding.ASCII);
        else
            File.WriteAllText(scriptPath, BuildLinuxApplyScript(pid, extractPath, AppContext.BaseDirectory, backupPath, processPath), Encoding.ASCII);

        StartApplyScript(scriptPath);
        logger.LogInformation("KAST update apply script started: {ScriptPath}", scriptPath);
        applicationLifetime.StopApplication();
        return Task.FromResult(new AppUpdateApplyResult(true, "KAST is stopping so the staged update can be applied."));
    }

    public static string? GetCurrentRuntimeIdentifier()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && arch == Architecture.X64)
            return "win-x64";
        if (OperatingSystem.IsLinux() && arch == Architecture.X64)
            return "linux-x64";
        if (OperatingSystem.IsLinux() && arch == Architecture.Arm64)
            return "linux-arm64";
        return null;
    }

    public static GitHubReleaseAsset? SelectAsset(IReadOnlyCollection<GitHubReleaseAsset> assets, string rid, bool isNightly)
    {
        var archiveExtension = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";
        if (isNightly)
        {
            var expected = $"kast-{rid}-nightly{archiveExtension}";
            return assets.FirstOrDefault(a => string.Equals(a.Name, expected, StringComparison.OrdinalIgnoreCase));
        }

        return assets
            .Where(a => a.Name.EndsWith(archiveExtension, StringComparison.OrdinalIgnoreCase))
            .Where(a => a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Name.StartsWith("kast-", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name.Length)
            .FirstOrDefault();
    }

    public static bool IsDocker()
        => string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsUpdateAvailable(string currentVersion, string? releaseVersion, string? releaseTag)
    {
        var current = NormalizeVersion(currentVersion);
        var release = NormalizeVersion(releaseVersion);
        if (string.IsNullOrWhiteSpace(release))
            release = NormalizeVersion(releaseTag);
        if (string.IsNullOrWhiteSpace(release))
            return false;
        return !string.Equals(current, release, StringComparison.OrdinalIgnoreCase);
    }

    public static void ExtractArchive(string archivePath, string destinationPath)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                var targetPath = GetSafeArchiveTargetPath(destinationPath, entry.FullName);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var fileStream = File.OpenRead(archivePath);
            using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                var targetPath = GetSafeArchiveTargetPath(destinationPath, entry.Name);
                if (entry.EntryType is TarEntryType.Directory)
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
            return;
        }

        throw new InvalidOperationException("Unsupported update archive format.");
    }

    private AppUpdateChannel GetChannel(string channelId)
        => Channels.FirstOrDefault(c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase))
           ?? Channels.First(c => c.Id == StableChannelId);

    private async Task<GitHubRelease?> FetchReleaseAsync(AppUpdateChannel channel, CancellationToken ct)
    {
        var path = channel.ReleaseSelection == AppUpdateReleaseSelection.Tag
            ? $"/repos/{channel.Owner}/{channel.Repository}/releases/tags/{channel.TagName}"
            : $"/repos/{channel.Owner}/{channel.Repository}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiBase + path);
        request.Headers.UserAgent.ParseAdd("KAST-Updater");
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<GitHubRelease>(ct);
    }

    private static AppUpdateCheckResult Unavailable(
        AppUpdateChannel channel,
        string currentVersion,
        string message,
        string runtimeIdentifier,
        bool isDocker)
        => new(
            channel,
            currentVersion,
            runtimeIdentifier,
            IsNativeSupported: !isDocker,
            IsDocker: isDocker,
            IsChannelAvailable: false,
            IsUpdateAvailable: false,
            message,
            ReleaseTag: null,
            ReleaseName: null,
            ReleaseVersion: null,
            PublishedAt: null,
            ReleaseUrl: null,
            Asset: null);

    private static AppUpdateAsset ToAsset(GitHubReleaseAsset asset)
        => new(asset.Name, asset.Size, asset.DownloadUrl);

    private static string GetCurrentVersion()
        => Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "dev";

    private static string? ExtractReleaseVersion(GitHubRelease release)
    {
        if (!string.IsNullOrWhiteSpace(release.Name))
        {
            var match = ReleaseVersionRegex().Match(release.Name);
            if (match.Success)
                return match.Groups["version"].Value;
        }

        return release.TagName;
    }

    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "";

        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') && trimmed.Length > 1 && char.IsDigit(trimmed[1]))
            trimmed = trimmed[1..];

        var plusIndex = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
            trimmed = trimmed[..plusIndex];

        return trimmed;
    }

    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        long sizeBytes,
        IProgress<AppUpdateProgress>? progress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("KAST-Updater");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? sizeBytes;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[1024 * 128];
        long downloaded = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (totalBytes > 0)
            {
                var percent = Math.Round(downloaded * 100d / totalBytes, 1);
                progress?.Report(new(AppUpdateStage.Downloading, percent, $"Downloaded {FormatBytes(downloaded)} of {FormatBytes(totalBytes)}"));
            }
        }
    }

    private async Task<bool> TryVerifyChecksumAsync(
        AppUpdateChannel channel,
        AppUpdateAsset asset,
        string archivePath,
        CancellationToken ct)
    {
        var release = await FetchReleaseAsync(channel, ct);
        var checksumAsset = release?.Assets.FirstOrDefault(a =>
            a.Name.Equals(asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(Path.GetFileNameWithoutExtension(asset.Name) + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is null)
        {
            logger.LogWarning("No checksum asset found for update asset {AssetName}", asset.Name);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, checksumAsset.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("KAST-Updater");
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var checksumText = await response.Content.ReadAsStringAsync(ct);
        var expected = checksumText.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(v => v.Length == 64);
        if (expected is null)
            throw new InvalidOperationException("Checksum asset did not contain a SHA-256 hash.");

        await using var file = File.OpenRead(archivePath);
        var hash = await SHA256.HashDataAsync(file, ct);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Downloaded update checksum did not match the release checksum.");

        return true;
    }

    private static string GetSafeArchiveTargetPath(string destinationPath, string entryName)
    {
        var normalizedName = entryName.Replace('/', Path.DirectorySeparatorChar);
        var targetPath = Path.GetFullPath(Path.Combine(destinationPath, normalizedName));
        var root = Path.GetFullPath(destinationPath);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update archive contains a path outside the destination directory.");

        return targetPath;
    }

    private static void StartApplyScript(string scriptPath)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{scriptPath}\"")
            : new ProcessStartInfo("/bin/sh", $"\"{scriptPath}\"");
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        Process.Start(startInfo);
    }

    private static string BuildWindowsApplyScript(int pid, string sourcePath, string destinationPath, string backupPath, string processPath)
        => $$"""
@echo off
setlocal
set PID={{pid}}
set SRC={{sourcePath}}
set DEST={{destinationPath}}
set BACKUP={{backupPath}}
set EXE={{processPath}}
:wait
tasklist /FI "PID eq %PID%" | find "%PID%" >nul
if not errorlevel 1 (
  timeout /t 1 /nobreak >nul
  goto wait
)
mkdir "%BACKUP%" >nul 2>nul
xcopy "%DEST%" "%BACKUP%\" /E /I /Y /H /C >nul
xcopy "%SRC%" "%DEST%\" /E /I /Y /H /C >nul
start "" "%EXE%"
endlocal
""";

    private static string BuildLinuxApplyScript(int pid, string sourcePath, string destinationPath, string backupPath, string processPath)
        => $$"""
#!/bin/sh
PID={{pid}}
SRC='{{sourcePath.Replace("'", "'\"'\"'")}}'
DEST='{{destinationPath.Replace("'", "'\"'\"'")}}'
BACKUP='{{backupPath.Replace("'", "'\"'\"'")}}'
EXE='{{processPath.Replace("'", "'\"'\"'")}}'
while kill -0 "$PID" 2>/dev/null; do
  sleep 1
done
mkdir -p "$BACKUP"
cp -a "$DEST"/. "$BACKUP"/
cp -a "$SRC"/. "$DEST"/
chmod +x "$EXE" 2>/dev/null || true
nohup "$EXE" >/dev/null 2>&1 &
""";

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var suffix = 0;
        while (value >= 1024 && suffix < suffixes.Length - 1)
        {
            value /= 1024;
            suffix++;
        }
        return $"{value:0.0} {suffixes[suffix]}";
    }

    [GeneratedRegex(@"(?<version>\d+\.\d+\.\d+(?:[-.a-zA-Z0-9]+)?)")]
    private static partial Regex ReleaseVersionRegex();
}

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset> Assets);

public sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
