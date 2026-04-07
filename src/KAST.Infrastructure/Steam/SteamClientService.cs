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

    // Temporary credentials for the login callback flow
    private string? _pendingAccessToken;
    private string? _currentRefreshToken;

    // QR auth state
    private QrAuthSession? _activeQrSession;

    // CDN state — cached across downloads
    private Server[]? _cdnServers;

    // Token cache
    private static readonly string TokenCachePath = Path.Combine(
        AppContext.BaseDirectory, "steam-token-cache.json");

    // Connection + profile state
    private bool _isConnected;                 // true when Steam network is reachable (anon or real)
    private SteamUserProfile? _profile;

    public bool IsAuthenticated => _isConnected && CurrentUsername != null;
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
        _loginTcs = new TaskCompletionSource<bool>();
        CurrentUsername = null;
        _pendingAccessToken = null;
        _currentRefreshToken = null;
        StartCallbackLoop();

        if (_steamClient.IsConnected)
        {
            // Already connected — just send the anonymous logon directly
            _steamUser.LogOnAnonymous();
        }
        else
        {
            _steamClient.Connect();
        }

        using var reg = ct.Register(() => _loginTcs.TrySetResult(false));
        return await _loginTcs.Task;
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
        // Ensure we're connected (may not be if logout's auto-reconnect failed)
        if (!_isConnected)
        {
            _logger.LogWarning("Not connected to Steam — reconnecting before QR auth");
            await LoginAnonymousAsync(ct);
        }

        if (!_isConnected)
            return new SteamQrAuthSession { ErrorMessage = "Failed to connect to Steam" };

        try
        {
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

        // 2. Get CDN server list
        var servers = await GetCdnServersAsync(ct);
        var server = servers.First();
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
        try
        {
            manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download manifest from {Host}, retrying with next server", server.Host);
            // Retry with a different server
            if (servers.Length > 1)
            {
                server = servers[1];
                manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
            }
            else throw;
        }

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

                // CDN.Client handles decryption + decompression internally
                var chunkBuffer = new byte[chunk.UncompressedLength];
                var written = await _cdnClient.DownloadDepotChunkAsync(depotId, chunk, server, chunkBuffer, depotKey);

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

    public async Task DownloadAppAsync(
        uint appId, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Not connected to Steam");
        if (!IsAuthenticated)
            throw new InvalidOperationException("Must be signed in with a Steam account to download app content");

        _logger.LogInformation("Starting app download for AppId {AppId} → {Path}", appId, destinationPath);
        progress?.Report(0);

        // 1. Get product info to discover depots and their manifests
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
        var depotManifests = new List<(uint DepotId, ulong ManifestId)>();

        foreach (var depot in depots.Children)
        {
            if (!uint.TryParse(depot.Name, out var depotId))
                continue;

            // Filter: only download depots for the current OS
            var config = depot["config"];
            if (config != KeyValue.Invalid)
            {
                var oslist = config["oslist"].AsString();
                if (!string.IsNullOrEmpty(oslist))
                {
                    var currentOs = OperatingSystem.IsWindows() ? "windows" : "linux";
                    if (!oslist.Contains(currentOs, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping depot {DepotId} (OS filter: {OsList})", depotId, oslist);
                        continue;
                    }
                }
            }

            // Get manifest ID from the "public" branch
            var depotManifestsKv = depot["manifests"];
            if (depotManifestsKv == KeyValue.Invalid) continue;

            var publicManifest = depotManifestsKv["public"];
            if (publicManifest == KeyValue.Invalid) continue;

            // SteamKit2 can store manifest as the value directly or under "gid"
            var manifestIdStr = publicManifest["gid"].AsString() ?? publicManifest.AsString();
            if (string.IsNullOrEmpty(manifestIdStr) || !ulong.TryParse(manifestIdStr, out var manifestId))
                continue;

            depotManifests.Add(((uint)depotId, (ulong)manifestId));
            _logger.LogInformation("Depot {DepotId}: ManifestId={ManifestId}", depotId, manifestId);
        }

        if (depotManifests.Count == 0)
            throw new InvalidOperationException($"No downloadable depots found for AppId {appId}");

        // 3. Download each depot
        var servers = await GetCdnServersAsync(ct);
        Directory.CreateDirectory(destinationPath);

        long totalDownloaded = 0;
        long totalSize = 0;

        // First pass: collect total size from all manifests
        var manifests = new List<(uint DepotId, byte[]? DepotKey, DepotManifest Manifest)>();

        foreach (var (depotId, manifestId) in depotManifests)
        {
            ct.ThrowIfCancellationRequested();

            // Get depot key
            byte[]? depotKey = null;
            var depotKeyResult = await _steamApps.GetDepotDecryptionKey(depotId, appId);
            if (depotKeyResult.Result == EResult.OK)
                depotKey = depotKeyResult.DepotKey;
            else
                _logger.LogWarning("Could not get depot key for {DepotId}: {Result}", depotId, depotKeyResult.Result);

            // Get manifest request code and download manifest
            var manifestRequestCode = await _steamContent.GetManifestRequestCode(depotId, appId, manifestId);
            var server = servers.First();

            DepotManifest manifest;
            try
            {
                manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CDN download manifest failed for depot {DepotId}, trying next server", depotId);
                if (servers.Length > 1)
                {
                    server = servers[1];
                    manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
                }
                else throw;
            }

            if (manifest.FilenamesEncrypted && depotKey != null)
                manifest.DecryptFilenames(depotKey);

            manifests.Add((depotId, depotKey, manifest));
            totalSize += (long)(manifest.TotalUncompressedSize);
        }

        _logger.LogInformation("Total download size: {Size} bytes across {Count} depots",
            totalSize, manifests.Count);

        // Second pass: download files from each depot
        foreach (var (depotId, depotKey, manifest) in manifests)
        {
            var files = manifest.Files?
                .Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory))
                .ToList() ?? [];

            var server = servers.First();

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
                    var written = await _cdnClient.DownloadDepotChunkAsync(depotId, chunk, server, chunkBuffer, depotKey);

                    fs.Position = (long)chunk.Offset;
                    await fs.WriteAsync(chunkBuffer.AsMemory(0, written), ct);

                    totalDownloaded += chunk.UncompressedLength;
                    if (totalSize > 0)
                        progress?.Report((double)totalDownloaded / totalSize * 100.0);
                }
            }

            _logger.LogInformation("Depot {DepotId} download complete ({FileCount} files)", depotId, files.Count);
        }

        _logger.LogInformation("App {AppId} download complete → {Path}", appId, destinationPath);
        progress?.Report(100);
    }

    // ───── CDN Server Discovery ─────

    private async Task<Server[]> GetCdnServersAsync(CancellationToken ct)
    {
        if (_cdnServers is { Length: > 0 }) return _cdnServers;

        var servers = await _steamContent.GetServersForSteamPipe();
        _cdnServers = servers
            .Where(s => s.Type is "CDN" or "SteamCache")
            .OrderBy(s => s.WeightedLoad)
            .ToArray();

        if (_cdnServers.Length == 0)
            throw new InvalidOperationException("No CDN servers available from Steam");

        _logger.LogInformation("Discovered {Count} CDN servers", _cdnServers.Length);
        return _cdnServers;
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
        _cdnServers = null; // invalidate CDN server cache

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
        StopCallbackLoop();
        _callbackCts?.Dispose();
        _cdnClient.Dispose();
        _steamClient.Disconnect();
        GC.SuppressFinalize(this);
    }
}
