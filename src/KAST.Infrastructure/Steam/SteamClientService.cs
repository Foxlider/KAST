using KAST.Core.Interfaces;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;
using SteamKit2.Internal;
using System.Text.Json;

namespace KAST.Infrastructure.Steam;

public class SteamClientService : ISteamService, IDisposable
{
    private readonly ILogger<SteamClientService> _logger;
    private readonly SteamClient _steamClient;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private readonly SteamApps _steamApps;
    private readonly SteamContent _steamContent;
    private readonly SteamFriends _steamFriends;
    private readonly SteamUnifiedMessages _steamUnifiedMessages;
    private readonly Client _cdnClient;

    private TaskCompletionSource<bool>? _loginTcs;
    private bool _isRunning;
    private CancellationTokenSource? _callbackCts;
    private bool _isReconnecting;
    private readonly SemaphoreSlim _qrAuthLock = new(1, 1);
    private readonly SemaphoreSlim _anonLoginLock = new(1, 1);
    private Task<bool>? _anonymousLoginTask;

    // Temporary credentials for the login callback flow
    private string? _pendingAccessToken;
    private string? _currentRefreshToken;

    // QR auth state
    private QrAuthSession? _activeQrSession;

    // CDN state — a persistent pool that discards faulty servers and auto-refills
    private CdnServerPool? _cdnPool;

    // Token cache
    private static readonly string TokenCachePath = Path.Combine(
        AppContext.BaseDirectory, "steam-token-cache.json");

    // Connection + profile state
    private bool _isConnected;                 // true when Steam network is reachable (anon or real)
    private SteamUserProfile? _profile;

    public bool IsAuthenticated => _isConnected && CurrentUsername != null;
    public bool IsConnected => _isConnected;
    public string? CurrentUsername { get; private set; }
    public SteamUserProfile? Profile => _profile;
    public event Action? AuthStateChanged;

    public SteamClientService(ILogger<SteamClientService> logger)
    {
        _logger = logger;
        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamApps = _steamClient.GetHandler<SteamApps>()!;
        _steamContent = _steamClient.GetHandler<SteamContent>()!;
        _steamFriends = _steamClient.GetHandler<SteamFriends>()!;
        _steamUnifiedMessages = _steamClient.GetHandler<SteamUnifiedMessages>()!;
        _cdnClient = new Client(_steamClient);

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        _callbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);

        // Suppress SteamKit2's own debug output (it can write to Debug.Write)
        DebugLog.Enabled = false;
    }

    // ───── Token Cache ─────

    private record TokenCache(
        string Username,
        string RefreshToken,
        ulong SteamId,
        string PersonaName,
        string? AvatarUrl);

    private void SaveTokenCache(string username, string refreshToken, SteamUserProfile? profile = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(new TokenCache(
                username,
                refreshToken,
                profile?.SteamId ?? 0,
                profile?.PersonaName ?? username,
                profile?.AvatarUrl));
            File.WriteAllText(TokenCachePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save Steam token cache");
        }
    }

    private TokenCache? LoadTokenCache()
    {
        try
        {
            if (!File.Exists(TokenCachePath)) return null;
            var json = File.ReadAllText(TokenCachePath);
            return JsonSerializer.Deserialize<TokenCache>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Steam token cache");
            return null;
        }
    }

    public static (string Username, string RefreshToken)? PeekTokenCache()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "steam-token-cache.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var cache = JsonSerializer.Deserialize<TokenCache>(json);
            return cache is null ? null : (cache.Username, cache.RefreshToken);
        }
        catch { return null; }
    }

    private void ClearTokenCache()
    {
        try { if (File.Exists(TokenCachePath)) File.Delete(TokenCachePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to clear Steam token cache"); }
    }

    // ───── Authentication ─────

    public async Task<bool> LoginAnonymousAsync(CancellationToken ct = default)
    {
        // Fast path: already logged in anonymously
        if (_isConnected && CurrentUsername == null)
            return true;

        await _anonLoginLock.WaitAsync(ct);
        Task<bool> loginTask;
        try
        {
            // Re-check once we own the lock
            if (_isConnected && CurrentUsername == null)
                return true;

            // Share a single in-flight anonymous login attempt across callers
            if (_anonymousLoginTask is { IsCompleted: false })
            {
                loginTask = _anonymousLoginTask;
            }
            else
            {
                _loginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                CurrentUsername = null;
                _pendingAccessToken = null;
                _currentRefreshToken = null;
                StartCallbackLoop();

                if (_steamClient.IsConnected)
                {
                    // Already transport-connected — request anonymous logon.
                    _steamUser.LogOnAnonymous();
                }
                else
                {
                    _steamClient.Connect();
                }

                _anonymousLoginTask = _loginTcs.Task;
                loginTask = _anonymousLoginTask;
            }
        }
        finally
        {
            _anonLoginLock.Release();
        }

        using var reg = ct.Register(() => _loginTcs?.TrySetResult(false));
        var result = await loginTask;

        // Clear finished task so a future reconnect can start a new attempt.
        if (loginTask.IsCompleted)
            _anonymousLoginTask = null;

        return result;
    }

    public async Task<bool> LoginWithTokenAsync(string username, string refreshToken, CancellationToken ct = default)
    {
        // Pre-populate profile from cache so the header shows something immediately
        var cached = LoadTokenCache();
        if (cached?.Username == username && !string.IsNullOrEmpty(cached.PersonaName))
        {
            _profile = new SteamUserProfile
            {
                SteamId = cached.SteamId,
                PersonaName = cached.PersonaName,
                AvatarUrl = cached.AvatarUrl
            };
        }

        _loginTcs = new TaskCompletionSource<bool>();
        CurrentUsername = username;
        _pendingAccessToken = refreshToken;
        _currentRefreshToken = refreshToken;
        _isReconnecting = _steamClient.IsConnected;
        StartCallbackLoop();
        _steamClient.Connect();

        using var reg = ct.Register(() => _loginTcs.TrySetResult(false));
        return await _loginTcs.Task;
    }

    public async Task LogoutAsync()
    {
        _activeQrSession = null;
        CurrentUsername = null;
        _profile = null;
        _pendingAccessToken = null;
        _currentRefreshToken = null;
        ClearTokenCache();
        _isConnected = false;
        AuthStateChanged?.Invoke();

        // Disconnect — auto-reconnect will fire and log on anonymously
        _loginTcs = new TaskCompletionSource<bool>();
        _isReconnecting = true;
        _steamClient.Disconnect();

        try
        {
            await _loginTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Anonymous reconnect after logout timed out");
        }
    }

    // ───── QR Code Authentication ─────

    public async Task<SteamQrAuthSession> BeginQrLoginAsync(CancellationToken ct = default)
    {
        await _qrAuthLock.WaitAsync(ct);
        try
        {
            // Ensure we're connected (may not be if logout's auto-reconnect failed)
            if (!_isConnected)
            {
                _logger.LogWarning("Not connected to Steam — reconnecting before QR auth");
                var ok = await LoginAnonymousAsync(ct);
                if (!ok || !_isConnected)
                    return new SteamQrAuthSession { ErrorMessage = "Failed to connect to Steam" };
            }

            var qrSession = await _steamClient.Authentication.BeginAuthSessionViaQRAsync(
                new AuthSessionDetails());

            _activeQrSession = qrSession;

            var session = new SteamQrAuthSession
            {
                ChallengeUrl = qrSession.ChallengeURL
            };

            // Steam periodically refreshes the challenge URL — relay it to the UI
            qrSession.ChallengeURLChanged = () =>
            {
                _logger.LogDebug("QR challenge URL refreshed");
                session.ChallengeUrl = qrSession.ChallengeURL;
                session.ChallengeUrlChanged?.Invoke(qrSession.ChallengeURL);
            };

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin QR auth session");
            return new SteamQrAuthSession { ErrorMessage = ex.Message };
        }
        finally
        {
            _qrAuthLock.Release();
        }
    }

    public async Task<bool> PollQrLoginAsync(SteamQrAuthSession session, CancellationToken ct = default)
    {
        if (_activeQrSession is not { } qrSession)
            return false;

        try
        {
            // Block until the user scans the QR code and confirms in the Steam app
            var result = await qrSession.PollingWaitForResultAsync(ct);

            _logger.LogInformation("QR auth succeeded for {Account}", result.AccountName);

            // Prepare credentials for the reconnect
            CurrentUsername = result.AccountName;
            _pendingAccessToken = result.RefreshToken;
            _currentRefreshToken = result.RefreshToken;
            SaveTokenCache(result.AccountName, result.RefreshToken);

            // Transition from anonymous → real account:
            // Disconnect (auto-reconnect will fire → OnConnected → LogOn with token)
            _loginTcs = new TaskCompletionSource<bool>();
            _isReconnecting = true;
            _steamClient.Disconnect();

            using var reg = ct.Register(() => _loginTcs.TrySetResult(false));
            var loggedIn = await _loginTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            _activeQrSession = null;
            return loggedIn;
        }
        catch (OperationCanceledException)
        {
            _activeQrSession = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QR auth polling failed");
            _activeQrSession = null;
            return false;
        }
    }

    // ───── Workshop Metadata (via SteamKit2 Unified Messages) ─────

    public async Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(long workshopId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching workshop item info for {Id} via Unified Messages", workshopId);

        if (!_isConnected)
            throw new InvalidOperationException("Not connected to Steam");

        var publishedFileService = _steamUnifiedMessages.CreateService<PublishedFile>();
        var request = new CPublishedFile_GetDetails_Request();
        request.publishedfileids.Add((ulong)workshopId);

        var response = await publishedFileService.GetDetails(request).ToTask().WaitAsync(ct);

        if (response.Result != EResult.OK)
        {
            _logger.LogWarning("GetDetails returned {Result} for {Id}", response.Result, workshopId);
            return null;
        }

        var details = response.Body.publishedfiledetails.FirstOrDefault();
        if (details == null) return null;

        return new WorkshopItemInfo
        {
            WorkshopId = (long)details.publishedfileid,
            Name = details.title ?? $"Workshop Item {workshopId}",
            Description = details.file_description,
            ThumbnailUrl = details.preview_url,
            Author = details.creator.ToString(),
            SizeBytes = (long)details.file_size,
            LastUpdated = DateTimeOffset.FromUnixTimeSeconds((long)details.time_updated).UtcDateTime,
            Subscriptions = (int)details.subscriptions,
            ConsumerAppId = details.consumer_appid,
            ManifestId = details.hcontent_file,
            Tags = details.tags?.Select(t => t.tag).ToList() ?? []
        };
    }

    public Task<IReadOnlyList<WorkshopItemInfo>> SearchWorkshopAsync(string query, int count = 20, CancellationToken ct = default)
    {
        _logger.LogWarning("Workshop search not yet implemented");
        return Task.FromResult<IReadOnlyList<WorkshopItemInfo>>([]);
    }

    // ───── Workshop Download (via SteamKit2 CDN.Client) ─────

    // Arma 3 AppID — used as both appId and depotId for workshop items (same as FASTER & BytexDigital)
    private const uint Arma3AppId = 107410;

    public async Task DownloadWorkshopItemAsync(
        long workshopId, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Not connected to Steam");
        if (!IsAuthenticated)
            throw new InvalidOperationException("Must be signed in with a Steam account to download mods");

        _logger.LogInformation("Starting CDN download of workshop item {Id}", workshopId);
        progress?.Report(0);

        // 1. Get workshop item details via Unified Messages for the manifest ID
        var publishedFileService = _steamUnifiedMessages.CreateService<PublishedFile>();
        var detailsReq = new CPublishedFile_GetDetails_Request();
        detailsReq.publishedfileids.Add((ulong)workshopId);
        var detailsResp = await publishedFileService.GetDetails(detailsReq).ToTask().WaitAsync(ct);

        if (detailsResp.Result != EResult.OK)
            throw new InvalidOperationException($"Failed to fetch workshop item {workshopId}: {detailsResp.Result}");

        var details = detailsResp.Body.publishedfiledetails.FirstOrDefault()
            ?? throw new InvalidOperationException($"Workshop item {workshopId} not found");

        if (details.hcontent_file == 0)
            throw new InvalidOperationException($"Workshop item {workshopId} has no downloadable content (hcontent_file=0)");

        uint appId = Arma3AppId;
        uint depotId = Arma3AppId;
        ulong manifestId = details.hcontent_file;

        _logger.LogInformation(
            "Workshop item: {Name}, AppId={AppId}, DepotId={DepotId}, ManifestId={ManifestId}",
            details.title, appId, depotId, manifestId);

        // 2. Get a CDN server from the pool
        var pool = await EnsureCdnPoolAsync(ct);
        var server = pool.GetServer(ct);
        _logger.LogDebug("Using CDN server: {Host} ({Type})", server.Host, server.Type);

        // 3. Get depot decryption key
        var depotKeyResult = await _steamApps.GetDepotDecryptionKey(depotId, appId);
        byte[]? depotKey = null;
        if (depotKeyResult.Result == EResult.OK)
        {
            depotKey = depotKeyResult.DepotKey;
            _logger.LogDebug("Obtained depot key for depot {DepotId}", depotId);
        }
        else
        {
            _logger.LogWarning("Depot key request returned {Result} for depot {DepotId}", depotKeyResult.Result, depotId);
        }

        // 4. Get manifest request code
        var manifestRequestCode = await _steamContent.GetManifestRequestCode(depotId, appId, manifestId);
        _logger.LogDebug("Manifest request code: {Code}", manifestRequestCode);

        // 5. Download manifest via CDN.Client
        DepotManifest manifest;
        bool manifestServerFaulty = false;
        try
        {
            manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { pool.ReturnServer(server, false); throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manifest download failed on {Host} — trying next server", server.Host);
            manifestServerFaulty = true;
            pool.ReturnServer(server, true); // discard faulty server
            server = pool.GetServer(ct);     // get a fresh one
            manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
        }
        if (!manifestServerFaulty) pool.ReturnServer(server, false);

        if (manifest.FilenamesEncrypted && depotKey != null)
            manifest.DecryptFilenames(depotKey);

        var files = manifest.Files?
            .Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory))
            .ToList() ?? [];

        if (files.Count == 0)
            throw new InvalidOperationException($"Manifest for workshop item {workshopId} contains no files");

        _logger.LogInformation("Manifest contains {FileCount} files, total {Size} bytes",
            files.Count, manifest.TotalUncompressedSize);

        // 6. Download all file chunks via CDN.Client
        long totalSize = files.Sum(f => (long)f.TotalSize);
        long downloadedSize = 0;

        Directory.CreateDirectory(destinationPath);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar);
            var filePath = Path.Combine(destinationPath, relativePath);
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null) Directory.CreateDirectory(dir);

            await using var fs = File.Create(filePath);
            if (file.TotalSize > 0)
                fs.SetLength((long)file.TotalSize);

            foreach (var chunk in file.Chunks.OrderBy(c => c.Offset))
            {
                ct.ThrowIfCancellationRequested();

                var chunkBuffer = new byte[chunk.UncompressedLength];
                var written = await DownloadChunkWithRetryAsync(depotId, chunk, pool, chunkBuffer, depotKey, ct);

                fs.Position = (long)chunk.Offset;
                await fs.WriteAsync(chunkBuffer.AsMemory(0, written), ct);

                downloadedSize += chunk.UncompressedLength;
                if (totalSize > 0)
                    progress?.Report((double)downloadedSize / totalSize * 100.0);
            }

            _logger.LogDebug("Downloaded: {FileName} ({Size} bytes)", file.FileName, file.TotalSize);
        }

        _logger.LogInformation("Workshop item {Id} download complete → {Path}", workshopId, destinationPath);
        progress?.Report(100);
    }

    private static List<(uint DepotId, ulong ManifestId)> ExtractDepotManifests(
        KeyValue depots, string currentOs, ILogger logger, bool ignorePlatformFilter = false, string branch = "public", uint[]? depotFilter = null)
    {
        var depotManifests = new List<(uint DepotId, ulong ManifestId)>();
        var filterSet = depotFilter is { Length: > 0 } ? new HashSet<uint>(depotFilter) : null;

        foreach (var depot in depots.Children)
        {
            if (!uint.TryParse(depot.Name, out var depotId))
                continue;

            // If a depot filter is supplied, only include explicitly listed depots
            if (filterSet != null && !filterSet.Contains(depotId))
            {
                logger.LogDebug("Skipping depot {DepotId} (not in depot filter)", depotId);
                continue;
            }

            // Filter: only download depots for the current OS.
            // ignorePlatformFilter=true bypasses this for Creator DLC apps whose depots are
            // marked Windows-only even though their PBO content runs on Linux servers.
            var config = depot["config"];
            if (!ignorePlatformFilter && config != KeyValue.Invalid)
            {
                var oslist = config["oslist"].AsString();
                if (!string.IsNullOrEmpty(oslist) && !oslist.Contains(currentOs, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Skipping depot {DepotId} (OS filter: {OsList})", depotId, oslist);
                    continue;
                }
            }

            // Get manifest ID from the requested branch (fall back to "public" if not found)
            var depotManifestsKv = depot["manifests"];
            if (depotManifestsKv == KeyValue.Invalid) continue;

            var branchManifest = depotManifestsKv[branch];
            if (branchManifest == KeyValue.Invalid)
            {
                // If the requested branch has no manifest for this depot, skip it
                // (don't fall back to public — we only want depots that exist on the target branch)
                if (branch != "public")
                {
                    logger.LogDebug("Skipping depot {DepotId} (no manifest on branch '{Branch}')", depotId, branch);
                    continue;
                }
                continue;
            }

            var manifestIdStr = branchManifest["gid"].AsString() ?? branchManifest.AsString();
            if (string.IsNullOrEmpty(manifestIdStr) || !ulong.TryParse(manifestIdStr, out var manifestId))
                continue;

            depotManifests.Add((depotId, manifestId));
            logger.LogInformation("Depot {DepotId} (branch '{Branch}'): ManifestId={ManifestId}", depotId, branch, manifestId);
        }

        return depotManifests;
    }

    private async Task<(long TotalDownloaded, int Verified, int Downloaded)> DownloadDepotFilesAsync(
        uint depotId, byte[]? depotKey, DepotManifest manifest, CdnServerPool pool,
        string destinationPath, long totalSize, long totalDownloaded,
        IProgress<double>? progress, IProgress<string>? logProgress, int maxParallelDownloads, CancellationToken ct)
    {
        int maxParallelHash = Math.Max(1, Environment.ProcessorCount);

        var files = manifest.Files?
            .Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory))
            .ToList() ?? [];

        int verifiedCount = 0, downloadedCount = 0;
        long depotTotalBytes = (long)manifest.TotalUncompressedSize;

        // ── Phase 1: Quick scan — existence + size only (no hashing) ─────────
        var depotMb = depotTotalBytes / 1_048_576.0;
        _logger.LogInformation("Depot {DepotId}: scanning {Count} files ({Size:F0} MB)", depotId, files.Count, depotMb);
        logProgress?.Report($"  Depot {depotId}: scanning {files.Count} files ({depotMb:F0} MB)...");

        var toDownload = new List<DepotManifest.FileData>();
        var toVerify   = new List<DepotManifest.FileData>();

        foreach (var file in files)
        {
            var filePath = Path.Combine(destinationPath, file.FileName.Replace('\\', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null) Directory.CreateDirectory(dir);

            if (!File.Exists(filePath) || new FileInfo(filePath).Length != (long)file.TotalSize)
                toDownload.Add(file);
            else
                toVerify.Add(file);
        }

        _logger.LogInformation("Depot {DepotId}: {ToDownload} missing/changed, {ToVerify} to hash-check",
            depotId, toDownload.Count, toVerify.Count);
        if (toDownload.Count > 0)
            logProgress?.Report($"  {toDownload.Count} file(s) missing or wrong size — will download.");
        if (toVerify.Count > 0)
            logProgress?.Report($"  {toVerify.Count} file(s) size-matched — hash-verifying...");

        // ── Phase 2: Parallel hash verification of size-matched files ────────
        if (toVerify.Count > 0)
        {
            int hashDone = 0;
            var hashResults = new System.Collections.Concurrent.ConcurrentBag<(DepotManifest.FileData File, bool Match)>();

            await Parallel.ForEachAsync(
                toVerify,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelHash, CancellationToken = ct },
                async (file, hashCt) =>
                {
                    try
                    {
                        if (file.FileHash is not { Length: > 0 })
                        {
                            hashResults.Add((file, Match: true));
                            return;
                        }

                        var path = Path.Combine(destinationPath, file.FileName.Replace('\\', Path.DirectorySeparatorChar));
                        var match = await Task.Run(() =>
                        {
                            using var sha1 = System.Security.Cryptography.SHA1.Create();
                            using var fStream = File.OpenRead(path);
                            return sha1.ComputeHash(fStream).SequenceEqual(file.FileHash);
                        }, hashCt);

                        var done = Interlocked.Increment(ref hashDone);
                        if (done % 100 == 0 || done == toVerify.Count)
                            logProgress?.Report($"  Verifying: {done}/{toVerify.Count} files checked...");

                        hashResults.Add((file, match));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                });

            int hashFailed = 0;
            foreach (var (file, match) in hashResults)
            {
                if (match)
                {
                    verifiedCount++;
                    totalDownloaded += (long)file.TotalSize;
                }
                else
                {
                    toDownload.Add(file);
                    hashFailed++;
                }
            }

            _logger.LogInformation("Depot {DepotId}: hash check complete — {Ok} OK, {Bad} corrupted/changed",
                depotId, toVerify.Count - hashFailed, hashFailed);
            if (hashFailed > 0)
                logProgress?.Report($"  {hashFailed} file(s) failed hash check — queued for re-download.");
            else
                logProgress?.Report($"  All {toVerify.Count} existing file(s) verified OK.");
        }

        if (totalSize > 0)
            progress?.Report((double)totalDownloaded / totalSize * 100.0);

        // ── Phase 3: Parallel chunk download ─────────────────────────────────
        if (toDownload.Count > 0)
        {
            long downloadBytes = toDownload.Sum(f => (long)f.TotalSize);
            _logger.LogInformation("Depot {DepotId}: downloading {Count} files ({Size:F1} MB) with {Par} parallel workers",
                depotId, toDownload.Count, downloadBytes / 1_048_576.0, maxParallelDownloads);
            logProgress?.Report($"  Downloading {toDownload.Count} file(s) ({downloadBytes / 1_048_576.0:F1} MB) — {maxParallelDownloads} parallel worker(s)...");

            long bytesDownloaded = 0;
            int  filesDone = 0;
            var  sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportBytes = 0;
            var  lastReportTime  = sw.Elapsed;

            // Parallel.ForEachAsync only ever keeps MaxDegreeOfParallelism items in-flight.
            // Unlike Select().ToArray() + Task.WhenAll, it never queues thousands of async
            // tasks up-front, so cancellation is instant (only active files need to abort).
            // When any body throws, Parallel.ForEachAsync cancels the token passed to all
            // other in-progress bodies, so the "first error aborts the rest" invariant is
            // preserved without a manual CancellationTokenSource.
            await Parallel.ForEachAsync(
                toDownload,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, maxParallelDownloads),
                    CancellationToken = ct
                },
                async (file, fileCt) =>
                {
                    try
                    {
                        var relativePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar);
                        var filePath     = Path.Combine(destinationPath, relativePath);
                        var dir          = Path.GetDirectoryName(filePath);
                        if (dir != null) Directory.CreateDirectory(dir);

                        var fileSizeMb = file.TotalSize / 1_048_576.0;
                        _logger.LogDebug("Depot {DepotId}: downloading {File} ({Size:F2} MB)", depotId, relativePath, fileSizeMb);

                        await using var fs = File.Create(filePath);
                        if (file.TotalSize > 0)
                            fs.SetLength((long)file.TotalSize);

                        foreach (var chunk in file.Chunks.OrderBy(c => c.Offset))
                        {
                            fileCt.ThrowIfCancellationRequested();
                            var buf = new byte[chunk.UncompressedLength];
                            var written = await DownloadChunkWithRetryAsync(depotId, chunk, pool, buf, depotKey, fileCt);
                            fs.Position = (long)chunk.Offset;
                            await fs.WriteAsync(buf.AsMemory(0, written), fileCt);

                            var newBytes = Interlocked.Add(ref bytesDownloaded, chunk.UncompressedLength);
                            if (totalSize > 0)
                                progress?.Report((double)(totalDownloaded + newBytes) / totalSize * 100.0);
                        }

                        var done = Interlocked.Increment(ref filesDone);
                        _logger.LogDebug("Depot {DepotId}: [{Done}/{Total}] {File}", depotId, done, toDownload.Count, relativePath);

                        // Speed + progress report: every 10 files, for large files (≥ 50 MB), last file, or every 5 s
                        var nowBytes = Interlocked.Read(ref bytesDownloaded);
                        var elapsed  = sw.Elapsed;
                        var secSinceReport = (elapsed - lastReportTime).TotalSeconds;

                        if (done % 10 == 0 || fileSizeMb >= 50 || done == toDownload.Count || secSinceReport >= 5)
                        {
                            var deltaBytes  = nowBytes - lastReportBytes;
                            var mbps        = secSinceReport > 0 ? (deltaBytes / 1_048_576.0) / secSinceReport : 0;
                            var totalMbDone = nowBytes / 1_048_576.0;
                            var totalMbAll  = downloadBytes / 1_048_576.0;

                            logProgress?.Report(
                                $"  [{done}/{toDownload.Count}] {relativePath}  —  {totalMbDone:F0}/{totalMbAll:F0} MB  ({mbps:F1} MB/s)");

                            Interlocked.Exchange(ref lastReportBytes, nowBytes);
                            lastReportTime = elapsed;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Catching here (before re-throwing) ensures the debugger's Just My Code
                        // mode does not flag this as "unhandled in user code": the exception IS
                        // handled here in user code; we simply propagate it so Parallel.ForEachAsync
                        // can stop the remaining workers correctly.
                        throw;
                    }
                });

            var swElapsed = sw.Elapsed;
            var totalMbDownloaded = Interlocked.Read(ref bytesDownloaded) / 1_048_576.0;
            var avgMbps  = swElapsed.TotalSeconds > 0 ? totalMbDownloaded / swElapsed.TotalSeconds : 0;

            totalDownloaded += Interlocked.Read(ref bytesDownloaded);
            downloadedCount  = toDownload.Count;

            _logger.LogInformation("Depot {DepotId}: download complete — {MB:F1} MB in {Sec:F1}s ({Mbps:F1} MB/s avg)",
                depotId, totalMbDownloaded, swElapsed.TotalSeconds, avgMbps);
            logProgress?.Report(
                $"  Depot {depotId}: {downloadedCount} file(s) downloaded ({totalMbDownloaded:F1} MB in {swElapsed.TotalSeconds:F0}s, avg {avgMbps:F1} MB/s).");
        }

        if (totalSize > 0)
            progress?.Report((double)totalDownloaded / totalSize * 100.0);

        _logger.LogInformation("Depot {DepotId}: {Verified} verified, {Downloaded} downloaded ({FileCount} total)",
            depotId, verifiedCount, downloadedCount, files.Count);
        logProgress?.Report($"  Depot {depotId}: {verifiedCount} up-to-date, {downloadedCount} updated.");

        return (totalDownloaded, verifiedCount, downloadedCount);
    }

    /// <summary>
    /// Downloads a single depot chunk with up to <c>MaxChunkRetries</c> attempts, rotating CDN
    /// servers on each failure.  Only re-throws on user cancellation or exhausted retries.
    /// </summary>
    private const int MaxChunkRetries = 3;

    private async Task<int> DownloadChunkWithRetryAsync(
        uint depotId, DepotManifest.ChunkData chunk, CdnServerPool pool,
        byte[] buf, byte[]? depotKey, CancellationToken ct)
    {
        Exception? lastEx = null;

        for (int attempt = 0; attempt < MaxChunkRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var server = pool.GetServer(ct);
            try
            {
                var written = await _cdnClient.DownloadDepotChunkAsync(depotId, chunk, server, buf, depotKey);
                pool.ReturnServer(server, false); // proven good — reuse it
                return written;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                pool.ReturnServer(server, false);
                throw;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _logger.LogWarning(ex,
                    "Chunk download failed on {Server} (attempt {Attempt}/{Max}) — discarding and retrying",
                    server.Host, attempt + 1, MaxChunkRetries);

                pool.ReturnServer(server, true); // faulty — permanently discard it

                if (attempt < MaxChunkRetries - 1)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct); // 1s, 2s back-off
            }
        }

        throw new IOException(
            $"Failed to download chunk after {MaxChunkRetries} attempts — no healthy CDN server responded.",
            lastEx);
    }

    public async Task DownloadAppAsync(
        uint appId, string destinationPath,
        IProgress<double>? progress = null, IProgress<string>? logProgress = null,
        bool ignorePlatformFilter = false, string branch = "public", uint[]? depotFilter = null, int maxParallelDownloads = 4, CancellationToken ct = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Not connected to Steam");
        // Anonymous login is sufficient for free dedicated server tools (e.g. AppId 233780)
        if (!IsAuthenticated)
            _logger.LogWarning("Downloading AppId {AppId} without a real account — may fail for paid content", appId);

        _logger.LogInformation("Starting app download for AppId {AppId} → {Path}", appId, destinationPath);
        logProgress?.Report($"Starting download for AppId {appId}...");
        progress?.Report(0);

        // 1. Get product info to discover depots and their manifests
        logProgress?.Report("Fetching product info from Steam...");
        var picsRequest = new SteamApps.PICSRequest(appId);
        var productInfo = await _steamApps.PICSGetProductInfo(new[] { picsRequest }, Enumerable.Empty<SteamApps.PICSRequest>());
        if (productInfo.Failed || !productInfo.Results?.Any() == true)
            throw new InvalidOperationException($"Failed to get product info for AppId {appId}");

        var appInfo = productInfo.Results!
            .SelectMany(r => r.Apps)
            .FirstOrDefault(a => a.Key == appId).Value;

        if (appInfo == null)
            throw new InvalidOperationException($"AppId {appId} not found in PICS response");

        var depots = appInfo.KeyValues["depots"];
        if (depots == KeyValue.Invalid)
            throw new InvalidOperationException($"No depots found for AppId {appId}");

        // 2. Collect all relevant depots (numeric keys only, skip branches/etc.)
        var currentOs = OperatingSystem.IsWindows() ? "windows" : "linux";
        var depotManifests = ExtractDepotManifests(depots, currentOs, _logger, ignorePlatformFilter, branch, depotFilter);

        if (depotManifests.Count == 0)
            throw new InvalidOperationException($"No downloadable depots found for AppId {appId} (OS: {currentOs})");

        logProgress?.Report($"Found {depotManifests.Count} depot(s) for {currentOs}.");

        // 3. Connect to the CDN pool
        logProgress?.Report("Connecting to CDN servers...");
        var pool = await EnsureCdnPoolAsync(ct);
        Directory.CreateDirectory(destinationPath);

        long totalDownloaded = 0;
        long totalSize = 0;

        // First pass: fetch all manifests and collect total size
        var manifestCache    = LoadManifestCache(destinationPath);
        var newManifestCache = new Dictionary<uint, ulong>(manifestCache);
        var manifests = new List<(uint DepotId, ulong ManifestId, byte[]? DepotKey, DepotManifest Manifest)>();

        foreach (var (depotId, manifestId) in depotManifests)
        {
            ct.ThrowIfCancellationRequested();
            logProgress?.Report($"Fetching manifest for depot {depotId}...");

            // Get depot key
            byte[]? depotKey = null;
            var depotKeyResult = await _steamApps.GetDepotDecryptionKey(depotId, appId);
            if (depotKeyResult.Result == EResult.OK)
                depotKey = depotKeyResult.DepotKey;
            else
                _logger.LogWarning("Could not get depot key for {DepotId}: {Result}", depotId, depotKeyResult.Result);

            // Get manifest request code and download manifest
            var manifestRequestCode = await _steamContent.GetManifestRequestCode(depotId, appId, manifestId);
            var mServer = pool.GetServer(ct);

            DepotManifest manifest;
            bool mFaulty = false;
            try
            {
                manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, mServer, depotKey);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { pool.ReturnServer(mServer, false); throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CDN manifest failed on {Host} for depot {DepotId} — trying next server", mServer.Host, depotId);
                logProgress?.Report($"Retrying depot {depotId} manifest on fallback server...");
                pool.ReturnServer(mServer, true); // discard faulty server
                mFaulty = true;
                mServer = pool.GetServer(ct);
                manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, mServer, depotKey);
            }
            if (!mFaulty) pool.ReturnServer(mServer, false);

            if (manifest.FilenamesEncrypted && depotKey != null)
                manifest.DecryptFilenames(depotKey);

            if (manifest.FilenamesEncrypted)
            {
                _logger.LogWarning("Depot {DepotId}: filenames are encrypted and no valid depot key — skipping", depotId);
                logProgress?.Report($"Depot {depotId}: skipped (encrypted filenames, no depot key). Try logging in with a Steam account that owns the game.");
                continue;
            }

            manifests.Add((depotId, manifestId, depotKey, manifest));
            totalSize += (long)(manifest.TotalUncompressedSize);
            var sizeMb = manifest.TotalUncompressedSize / 1_048_576.0;
            logProgress?.Report($"Depot {depotId}: {manifest.Files?.Count ?? 0} files, {sizeMb:F0} MB");
        }

        var totalMb = totalSize / 1_048_576.0;
        _logger.LogInformation("Total size: {Size} bytes across {Count} depots", totalSize, manifests.Count);
        logProgress?.Report($"Total: {totalMb:F0} MB across {manifests.Count} depot(s). Checking for changes...");

        // Second pass: skip unchanged depots (manifest ID cache hit), verify + repair the rest
        int grandVerified = 0, grandDownloaded = 0, skippedDepots = 0;

        foreach (var (depotId, manifestId, depotKey, manifest) in manifests)
        {
            ct.ThrowIfCancellationRequested();

            if (manifestCache.TryGetValue(depotId, out var cachedManifestId) && cachedManifestId == manifestId)
            {
                // Manifest ID unchanged — all files in this depot are guaranteed current
                skippedDepots++;
                totalDownloaded += (long)manifest.TotalUncompressedSize;
                if (totalSize > 0) progress?.Report((double)totalDownloaded / totalSize * 100.0);
                logProgress?.Report($"Depot {depotId}: no changes (manifest {manifestId}) — skipped.");
                newManifestCache[depotId] = manifestId;
                continue;
            }

            var (newTotal, verified, downloaded) = await DownloadDepotFilesAsync(
                depotId, depotKey, manifest, pool, destinationPath,
                totalSize, totalDownloaded, progress, logProgress, maxParallelDownloads, ct);

            totalDownloaded = newTotal;
            grandVerified  += verified;
            grandDownloaded += downloaded;
            newManifestCache[depotId] = manifestId;
        }

        // Persist updated manifest IDs so subsequent runs can skip unchanged depots
        SaveManifestCache(destinationPath, newManifestCache);

        var summary = (grandDownloaded == 0 && skippedDepots > 0)
            ? $"Already up-to-date — {skippedDepots} depot(s) unchanged, {grandVerified} file(s) verified."
            : $"Complete — {grandVerified} file(s) already up-to-date, {grandDownloaded} file(s) updated, {skippedDepots} depot(s) skipped.";

        _logger.LogInformation("App {AppId} complete → {Path}", appId, destinationPath);
        logProgress?.Report(summary);
        progress?.Report(100);
    }

    // ───── Manifest Cache & File Verification ─────

    private static string ManifestCachePath(string installPath) =>
        Path.Combine(installPath, ".kast_manifests.json");

    private static Dictionary<uint, ulong> LoadManifestCache(string installPath)
    {
        var path = ManifestCachePath(installPath);
        if (!File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<uint, ulong>>(json) ?? [];
        }
        catch { return []; }
    }

    private static void SaveManifestCache(string installPath, Dictionary<uint, ulong> cache)
    {
        try { File.WriteAllText(ManifestCachePath(installPath), JsonSerializer.Serialize(cache)); }
        catch { /* non-fatal — next run will re-verify */ }
    }

    /// <summary>
    /// Returns <c>true</c> if the local file exists, has the correct size, and
    /// (when the manifest provides one) a matching SHA-1 hash.
    /// The size check is done first to avoid hashing files that are clearly wrong.
    /// </summary>
    private static bool FileMatchesManifest(string filePath, DepotManifest.FileData file)
    {
        if (!File.Exists(filePath)) return false;
        if (new FileInfo(filePath).Length != (long)file.TotalSize) return false;
        if (file.FileHash is not { Length: > 0 }) return true; // no hash in manifest — size match is sufficient

        using var sha1 = System.Security.Cryptography.SHA1.Create();
        using var fs   = File.OpenRead(filePath);
        return sha1.ComputeHash(fs).SequenceEqual(file.FileHash);
    }

    // ───── CDN Server Pool ─────

    private Task<CdnServerPool> EnsureCdnPoolAsync(CancellationToken ct = default)
    {
        // If a pool already exists and is healthy, return it immediately
        if (_cdnPool is not null)
            return Task.FromResult(_cdnPool);

        _cdnPool = new CdnServerPool(_steamClient, _steamContent, _logger);
        _logger.LogInformation("CDN server pool created");
        return Task.FromResult(_cdnPool);
    }

    // ───── Download Benchmark ─────

    private const uint BenchmarkAppId   = 233780; // Arma 3 DS
    private const uint BenchmarkDepotId = 233781; // Server Content depot
    private static readonly int[] BenchmarkLevels = [1, 2, 4, 8, 16, 32, 64];
    // Each level gets its own slice of unique chunks so CDN edge-cache from one
    // run cannot inflate the apparent speed of the next level.
    private const long BenchmarkTargetBytesPerLevel = 10 * 1024 * 1024; // 10 MB per level

    public async Task<IReadOnlyList<BenchmarkResult>> BenchmarkDownloadAsync(
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Not connected to Steam");

        log?.Report("Fetching product info for benchmark...");

        // 1. Get the public-branch manifest for the server content depot
        var picsReq = new SteamApps.PICSRequest(BenchmarkAppId);
        var productInfo = await _steamApps.PICSGetProductInfo(new[] { picsReq }, Enumerable.Empty<SteamApps.PICSRequest>());
        var appInfo = productInfo.Results!.SelectMany(r => r.Apps).First(a => a.Key == BenchmarkAppId).Value;
        var depots = appInfo.KeyValues["depots"];

        var depotKv = depots[BenchmarkDepotId.ToString()];
        var manifestIdStr = depotKv["manifests"]["public"]["gid"].AsString()
                         ?? depotKv["manifests"]["public"].AsString();
        var manifestId = ulong.Parse(manifestIdStr!);

        // 2. Get depot key + manifest
        byte[]? depotKey = null;
        var keyResult = await _steamApps.GetDepotDecryptionKey(BenchmarkDepotId, BenchmarkAppId);
        if (keyResult.Result == EResult.OK)
            depotKey = keyResult.DepotKey;

        var pool = await EnsureCdnPoolAsync(ct);
        var server = pool.GetServer(ct);
        var reqCode = await _steamContent.GetManifestRequestCode(BenchmarkDepotId, BenchmarkAppId, manifestId);
        var manifest = await _cdnClient.DownloadManifestAsync(BenchmarkDepotId, manifestId, reqCode, server, depotKey);
        pool.ReturnServer(server, false);

        if (manifest.FilenamesEncrypted && depotKey != null)
            manifest.DecryptFilenames(depotKey);

        // 3. Collect enough UNIQUE chunks to give each level its own non-overlapping slice.
        //    This prevents CDN edge-cache warm-up from the previous level inflating results.
        long totalNeeded = BenchmarkTargetBytesPerLevel * BenchmarkLevels.Length;
        var allChunks = new List<DepotManifest.ChunkData>();
        long collected = 0;

        foreach (var file in manifest.Files!.Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory)))
        {
            foreach (var chunk in file.Chunks)
            {
                allChunks.Add(chunk);
                collected += chunk.UncompressedLength;
                if (collected >= totalNeeded) break;
            }
            if (collected >= totalNeeded) break;
        }

        log?.Report($"Collected {allChunks.Count} unique chunks ({collected / 1_048_576.0:F0} MB) partitioned across {BenchmarkLevels.Length} levels");

        // Partition chunks into non-overlapping slices — one slice per level
        int chunksPerLevel = Math.Max(1, allChunks.Count / BenchmarkLevels.Length);
        var results = new List<BenchmarkResult>();

        // 4. Run each parallelism level on its own private chunk slice
        for (int i = 0; i < BenchmarkLevels.Length; i++)
        {
            var level = BenchmarkLevels[i];
            ct.ThrowIfCancellationRequested();

            int sliceStart = i * chunksPerLevel;
            int sliceEnd   = (i == BenchmarkLevels.Length - 1) ? allChunks.Count : sliceStart + chunksPerLevel;
            if (sliceStart >= allChunks.Count) sliceStart = 0; // fallback: reuse from start if manifest too small
            if (sliceEnd   >  allChunks.Count) sliceEnd   = allChunks.Count;

            var levelChunks = allChunks.GetRange(sliceStart, sliceEnd - sliceStart);
            long levelBytes = levelChunks.Sum(c => (long)c.UncompressedLength);
            log?.Report($"Testing {level,2} parallel download(s) ({levelBytes / 1_048_576.0:F1} MB)...");

            using var sem = new SemaphoreSlim(level);
            long bytesDown = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var tasks = levelChunks.Select(async chunk =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var buf = new byte[chunk.UncompressedLength];
                    var written = await DownloadChunkWithRetryAsync(BenchmarkDepotId, chunk, pool, buf, depotKey, ct);
                    Interlocked.Add(ref bytesDown, written);
                }
                finally { sem.Release(); }
            }).ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            var mbps = (bytesDown / 1_048_576.0) / sw.Elapsed.TotalSeconds;
            results.Add(new BenchmarkResult
            {
                Parallelism    = level,
                BytesDownloaded = bytesDown,
                ElapsedSeconds = sw.Elapsed.TotalSeconds,
                MbPerSecond    = mbps
            });

            log?.Report($"  {level,2} thread(s): {mbps:F1} MB/s ({sw.Elapsed.TotalSeconds:F1}s)");
        }

        var best = results.OrderByDescending(r => r.MbPerSecond).First();
        log?.Report($"Recommended: {best.Parallelism} parallel downloads ({best.MbPerSecond:F1} MB/s)");
        return results;
    }

    // ───── SteamKit2 Callback Handlers ─────

    private void StartCallbackLoop()
    {
        if (_isRunning) return;

        _isRunning = true;
        _callbackCts = new CancellationTokenSource();

        _ = Task.Run(() =>
        {
            while (_isRunning && !_callbackCts.Token.IsCancellationRequested)
            {
                _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
        }, _callbackCts.Token);
    }

    private void StopCallbackLoop()
    {
        _isRunning = false;
        _callbackCts?.Cancel();
    }

    private void OnConnected(SteamClient.ConnectedCallback cb)
    {
        _logger.LogInformation("Connected to Steam");

        if (CurrentUsername != null && _pendingAccessToken != null)
        {
            // Log on with real account credentials
            var token = _pendingAccessToken;
            _pendingAccessToken = null;
            _logger.LogInformation("Logging in as {Username}", CurrentUsername);
            _steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = CurrentUsername,
                AccessToken = token
            });
        }
        else
        {
            _steamUser.LogOnAnonymous();
        }
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        _logger.LogInformation("Disconnected from Steam");
        _isConnected = false;
        // Dispose and recreate the pool so it re-discovers CDN servers after reconnect.
        // ReturnServer(faulty=true) calls during the disconnect may have already drained it.
        _cdnPool?.Dispose();
        _cdnPool = null;

        if (_isReconnecting)
        {
            // Intentional disconnect (account transition) — auto-reconnect once
            _isReconnecting = false;
            _logger.LogInformation("Auto-reconnecting to Steam");
            _steamClient.Connect();
        }
        else
        {
            _loginTcs?.TrySetResult(false);
        }

        AuthStateChanged?.Invoke();
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result == EResult.OK)
        {
            _logger.LogInformation("Logged in to Steam{Account}",
                CurrentUsername != null ? $" as {CurrentUsername}" : " (anonymous)");
            _isConnected = true;

            // Pass the cell ID to the pool so Steam routes us to geographically close CDN nodes.
            // The pool may already exist from a previous anonymous login; update it in-place.
            if (_cdnPool is not null)
                _cdnPool.CellId = cb.CellID;

            // For real account logins, set persona state to Online so
            // Steam sends us back our PersonaStateCallback with name + avatar
            if (CurrentUsername != null)
                _steamFriends.SetPersonaState(EPersonaState.Online);

            _loginTcs?.TrySetResult(true);
            AuthStateChanged?.Invoke();
        }
        else
        {
            _logger.LogWarning("Steam login failed: {Result}", cb.Result);
            // If the cached token was rejected, clear it and clear the profile so we fall through to anonymous
            if (CurrentUsername != null)
            {
                ClearTokenCache();
                CurrentUsername = null;
                _profile = null;
                _currentRefreshToken = null;
            }
            _isConnected = false;
            _loginTcs?.TrySetResult(false);
            AuthStateChanged?.Invoke();
        }
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback cb)
    {
        _logger.LogInformation("Logged off from Steam: {Result}", cb.Result);
        _isConnected = false;
        AuthStateChanged?.Invoke();
    }

    private void OnPersonaState(SteamFriends.PersonaStateCallback cb)
    {
        // Only care about our own persona state
        if (cb.FriendID != _steamClient.SteamID) return;

        var hashBytes = cb.AvatarHash;
        string? avatarUrl = null;
        if (hashBytes is { Length: 20 })
        {
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            if (hash != new string('0', 40))
                avatarUrl = $"https://avatars.akamai.steamstatic.com/{hash}_medium.jpg";
        }

        _logger.LogDebug("Persona callback — Name: {Name}, AvatarHash: {Hash}, AvatarUrl: {Url}",
            cb.Name, hashBytes != null ? Convert.ToHexString(hashBytes) : "null", avatarUrl ?? "none");

        _profile = new SteamUserProfile
        {
            SteamId = cb.FriendID.ConvertToUInt64(),
            PersonaName = string.IsNullOrEmpty(cb.Name) ? CurrentUsername ?? "Unknown" : cb.Name,
            AvatarUrl = avatarUrl
        };

        // Update the token cache with the freshly resolved profile
        if (CurrentUsername != null && _currentRefreshToken != null)
            SaveTokenCache(CurrentUsername, _currentRefreshToken, _profile);

        _logger.LogDebug("Persona state resolved: {Name} ({SteamId})", _profile.PersonaName, _profile.SteamId);
        AuthStateChanged?.Invoke();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopCallbackLoop();
            _callbackCts?.Dispose();
            _cdnPool?.Dispose();
            _cdnClient.Dispose();
            _steamClient.Disconnect();
        }
    }

    ~SteamClientService()
    {
        Dispose(false);
    }
}
