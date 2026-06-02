using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;
using SteamKit2.Internal;

namespace KAST.Infrastructure.Steam;

public class SteamClientService : ISteamService, IDisposable
{
    private readonly ILogger<SteamClientService> _logger;
    private readonly IOutputSanitizer _sanitizer;
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
    private readonly SemaphoreSlim _credentialAuthLock = new(1, 1);
    private readonly SemaphoreSlim _anonLoginLock = new(1, 1);
    private Task<bool>? _anonymousLoginTask;

    // Temporary credentials for the login callback flow
    private string? _pendingAccessToken;
    private string? _currentRefreshToken;

    // QR auth state
    private QrAuthSession? _activeQrSession;
    private CredentialsAuthSession? _activeCredentialSession;
    private CredentialAuthenticator? _activeCredentialAuthenticator;

    private sealed class CredentialAuthenticator(SteamCredentialAuthSession session, ILogger logger) : IAuthenticator
    {
        private readonly object _sync = new();
        private TaskCompletionSource<string>? _pendingCodeTcs;

        public bool SubmitGuardCode(string code)
        {
            TaskCompletionSource<string>? tcs;
            lock (_sync)
            {
                tcs = _pendingCodeTcs;
                _pendingCodeTcs = null;
            }

            if (tcs is null)
                return false;

            tcs.TrySetResult(code);
            return true;
        }

        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            logger.LogInformation("Steam credential auth requested Steam Guard app code");
            lock (_sync)
            {
                _pendingCodeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            session.RequiresGuardCode = true;
            session.WaitingForDeviceConfirmation = false;
            session.GuardCodePrompt = previousCodeWasIncorrect
                ? "Incorrect app code. Enter a new Steam Guard code from your mobile app."
                : "Enter the Steam Guard code from your mobile app.";
            session.StateChanged?.Invoke();

            return _pendingCodeTcs.Task;
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            logger.LogInformation("Steam credential auth requested email code for {Email}", email);
            lock (_sync)
            {
                _pendingCodeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            session.RequiresGuardCode = true;
            session.WaitingForDeviceConfirmation = false;
            session.GuardCodePrompt = previousCodeWasIncorrect
                ? $"Incorrect email code. Enter the new code sent to {email}."
                : $"Enter the code sent to {email}.";
            session.StateChanged?.Invoke();

            return _pendingCodeTcs.Task;
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            logger.LogInformation("Steam credential auth requested device confirmation");
            session.RequiresGuardCode = false;
            session.WaitingForDeviceConfirmation = true;
            session.GuardCodePrompt = "Approve the sign-in request in your Steam mobile or desktop client.";
            session.StateChanged?.Invoke();
            return Task.FromResult(true);
        }
    }

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

    public SteamClientService(ILogger<SteamClientService> logger, IOutputSanitizer sanitizer)
    {
        _logger = logger;
        _sanitizer = sanitizer;
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
        _activeCredentialSession = null;
        _activeCredentialAuthenticator = null;
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
        using var activity = KastActivitySources.Steam.StartActivity(
            "kast.steam.auth.qr.begin", ActivityKind.Client);
        try
        {
            // Ensure we're connected (may not be if logout's auto-reconnect failed)
            if (!_isConnected)
            {
                _logger.LogWarning("Not connected to Steam — reconnecting before QR auth");
                var ok = await LoginAnonymousAsync(ct);
                if (!ok || !_isConnected)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to connect to Steam");
                    return new SteamQrAuthSession { ErrorMessage = "Failed to connect to Steam" };
                }
            }

            var qrSession = await _steamClient.Authentication.BeginAuthSessionViaQRAsync(
                new AuthSessionDetails());

            _activeQrSession = qrSession;

            var session = new SteamQrAuthSession
            {
                ChallengeUrl = qrSession.ChallengeURL
            };

            activity?.SetTag("auth.challenge_url", qrSession.ChallengeURL);
            _logger.LogInformation("QR auth session started with challenge URL");

            // Steam periodically refreshes the challenge URL — relay it to the UI
            qrSession.ChallengeURLChanged = () =>
            {
                _logger.LogInformation("QR challenge URL refreshed");
                session.ChallengeUrl = qrSession.ChallengeURL;
                session.ChallengeUrlChanged?.Invoke(qrSession.ChallengeURL);
            };

            return session;
        }
        catch (Exception ex)
        {
            var safeMessage = _sanitizer.Sanitize(ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, safeMessage);
            RecordExceptionEvent(activity, ex);
            _logger.LogError(ex, "Failed to begin QR auth session");
            return new SteamQrAuthSession { ErrorMessage = safeMessage };
        }
        finally
        {
            _qrAuthLock.Release();
        }
    }



    public async Task<SteamCredentialAuthSession> BeginCredentialLoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return new SteamCredentialAuthSession { ErrorMessage = "Username is required" };

        if (string.IsNullOrWhiteSpace(password))
            return new SteamCredentialAuthSession { ErrorMessage = "Password is required" };

        await _credentialAuthLock.WaitAsync(ct);
        using var activity = KastActivitySources.Steam.StartActivity(
            "kast.steam.auth.credential.begin", ActivityKind.Client);
        try
        {
            activity?.SetTag("auth.username", username);
            _logger.LogInformation("Starting credential auth for {Username}", username);

            if (!_isConnected)
            {
                _logger.LogWarning("Not connected to Steam - reconnecting before credential auth");
                var ok = await LoginAnonymousAsync(ct);
                if (!ok || !_isConnected)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to connect to Steam");
                    return new SteamCredentialAuthSession { ErrorMessage = "Failed to connect to Steam" };
                }
            }

            var session = new SteamCredentialAuthSession();
            var authenticator = new CredentialAuthenticator(session, _logger);

            var credentialsSession = await _steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = true,
                    Authenticator = authenticator
                });

            _activeCredentialAuthenticator = authenticator;
            _activeCredentialSession = credentialsSession;
            _logger.LogInformation("Credential auth session started for {Username}", username);
            return session;
        }
        catch (Exception ex)
        {
            var safeMessage = _sanitizer.Sanitize(ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, safeMessage);
            RecordExceptionEvent(activity, ex);
            _logger.LogError(ex, "Failed to begin credential auth session for {Username}", username);
            return new SteamCredentialAuthSession { ErrorMessage = safeMessage };
        }
        finally
        {
            _credentialAuthLock.Release();
        }
    }

    public Task<bool> SubmitCredentialGuardCodeAsync(SteamCredentialAuthSession session, string code, CancellationToken ct = default)
    {
        _ = session;
        _ = ct;

        if (string.IsNullOrWhiteSpace(code))
            return Task.FromResult(false);

        var accepted = _activeCredentialAuthenticator?.SubmitGuardCode(code.Trim()) == true;
        return Task.FromResult(accepted);
    }

    public async Task<bool> PollCredentialLoginAsync(SteamCredentialAuthSession session, CancellationToken ct = default)
    {
        if (_activeCredentialSession is not { } credentialSession)
            return false;

        using var activity = KastActivitySources.Steam.StartActivity(
            "kast.steam.auth.credential.poll", ActivityKind.Client);
        try
        {
            _logger.LogInformation("Starting credential auth polling");
            var result = await credentialSession.PollingWaitForResultAsync(ct);

            activity?.SetTag("auth.account", result.AccountName);
            activity?.SetTag("auth.success", true);
            _logger.LogInformation("Credential auth succeeded for {Account}", result.AccountName);

            CurrentUsername = result.AccountName;
            _pendingAccessToken = result.RefreshToken;
            _currentRefreshToken = result.RefreshToken;
            SaveTokenCache(result.AccountName, result.RefreshToken);

            _loginTcs = new TaskCompletionSource<bool>();
            _isReconnecting = true;
            _steamClient.Disconnect();

            using var reg = ct.Register(() => _loginTcs.TrySetResult(false));
            var loggedIn = await _loginTcs.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            _activeCredentialSession = null;
            _activeCredentialAuthenticator = null;
            session.RequiresGuardCode = false;
            session.WaitingForDeviceConfirmation = false;
            session.GuardCodePrompt = null;
            session.StateChanged?.Invoke();
            activity?.SetTag("auth.logon_complete", loggedIn);
            return loggedIn;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Credential polling cancelled");
            _logger.LogWarning("Credential auth polling was cancelled");
            _activeCredentialSession = null;
            _activeCredentialAuthenticator = null;
            return false;
        }
        catch (Exception ex)
        {
            var safeMessage = _sanitizer.Sanitize(ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, safeMessage);
            RecordExceptionEvent(activity, ex);
            _logger.LogWarning(ex, "Credential auth polling failed");
            _activeCredentialSession = null;
            _activeCredentialAuthenticator = null;
            session.ErrorMessage = safeMessage;
            session.StateChanged?.Invoke();
            return false;
        }
    }

    public async Task<bool> PollQrLoginAsync(SteamQrAuthSession session, CancellationToken ct = default)
    {
        if (_activeQrSession is not { } qrSession)
            return false;

        using var activity = KastActivitySources.Steam.StartActivity(
            "kast.steam.auth.qr.poll", ActivityKind.Client);
        try
        {
            _logger.LogInformation("Starting QR auth polling");
            // Block until the user scans the QR code and confirms in the Steam app
            var result = await qrSession.PollingWaitForResultAsync(ct);

            activity?.SetTag("auth.account", result.AccountName);
            activity?.SetTag("auth.success", true);
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
            activity?.SetTag("auth.logon_complete", loggedIn);
            return loggedIn;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "QR polling cancelled");
            _logger.LogWarning("QR auth polling was cancelled");
            _activeQrSession = null;
            return false;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, _sanitizer.Sanitize(ex.Message));
            RecordExceptionEvent(activity, ex);
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

        using var activity = KastActivitySources.Content.StartActivity(
            "kast.steam.unified.workshop_details", ActivityKind.Client);
        activity?.SetTag("workshop.id",          workshopId);

        try
        {
            var publishedFileService = _steamUnifiedMessages.CreateService<PublishedFile>();
            var request = new CPublishedFile_GetDetails_Request();
            request.publishedfileids.Add((ulong)workshopId);

            var response = await publishedFileService.GetDetails(request).ToTask().WaitAsync(ct);

            activity?.SetTag("steam.result", response.Result.ToString());

            if (response.Result != EResult.OK)
            {
                _logger.LogWarning("GetDetails returned {Result} for {Id}", response.Result, workshopId);
                activity?.SetStatus(ActivityStatusCode.Error, response.Result.ToString());
                return null;
            }

            var details = response.Body.publishedfiledetails.FirstOrDefault();
            if (details == null)
            {
                activity?.SetTag("workshop.found", false);
                return null;
            }

            activity?.SetTag("workshop.found", true);
            activity?.SetTag("workshop.name", details.title);
            activity?.SetTag("workshop.app_id", (int)details.consumer_appid);
            activity?.SetTag("workshop.manifest_id", details.hcontent_file);
            activity?.SetTag("workshop.size_bytes", (long)details.file_size);
            activity?.SetTag("workshop.subscriptions", (int)details.subscriptions);

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
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, _sanitizer.Sanitize(ex.Message));
            RecordExceptionEvent(activity, ex);
            throw;
        }
    }

    public Task<IReadOnlyList<WorkshopItemInfo>> SearchWorkshopAsync(string query, int count = 20, CancellationToken ct = default)
    {
        _logger.LogWarning("Workshop search not yet implemented");
        return Task.FromResult<IReadOnlyList<WorkshopItemInfo>>([]);
    }

    // ───── Workshop Download (via SteamKit2 CDN.Client) ─────

    // Arma 3 AppID — used as both appId and depotId for workshop items (same as FASTER & BytexDigital)
    private const uint Arma3AppId = 107410;

    public async Task<ulong> DownloadWorkshopItemAsync(
        long workshopId, string destinationPath,
        IProgress<double>? progress = null,
        IProgress<DownloadFileProgress>? fileProgress = null,
        IProgress<string>? statusProgress = null,
        int maxParallelDownloads = 4,
        CancellationToken ct = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Not connected to Steam");
        if (!IsAuthenticated)
            throw new InvalidOperationException("Must be signed in with a Steam account to download mods");

        using var workshopActivity = KastActivitySources.Content.StartActivity(
            "kast.steam.workshop_download", ActivityKind.Internal);
        workshopActivity?.SetTag("workshop.id", workshopId);

        try
        {
            _logger.LogInformation("Starting CDN download of workshop item {Id}", workshopId);
            progress?.Report(0);
            statusProgress?.Report("Fetching Workshop details");

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
            workshopActivity?.SetTag("workshop.name", details.title);
            workshopActivity?.SetTag("workshop.manifest_id", manifestId);
            workshopActivity?.SetTag("workshop.depot_id", depotId);
            workshopActivity?.SetTag("workshop.app_id", appId);

            // 2. Get CDN server pool + depot decryption key
            statusProgress?.Report("Preparing Steam CDN");
            var pool = await EnsureCdnPoolAsync(ct);
            var depotKey = await GetDepotKeyAsync(depotId, appId);

            // 3-5. Get manifest request code + download manifest (automatic CDN fallback)
            statusProgress?.Report("Checking against Steam manifest");
            var manifestRequestCode = await _steamContent.GetManifestRequestCode(depotId, appId, manifestId);
            var manifest = await DownloadManifestWithFallbackAsync(
                depotId, manifestId, manifestRequestCode, depotKey, pool, workshopActivity, ct);

            var files = manifest.Files?
                .Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory))
                .ToList() ?? [];

            if (files.Count == 0)
                throw new InvalidOperationException($"Manifest for workshop item {workshopId} contains no files");

            _logger.LogInformation("Manifest contains {FileCount} files, total {Size} bytes",
                files.Count, manifest.TotalUncompressedSize);
            workshopActivity?.SetTag("workshop.file_count", files.Count);
            workshopActivity?.SetTag("workshop.size_mb", Math.Round(manifest.TotalUncompressedSize / 1_048_576.0, 1));

            // 6. Download all file chunks via CDN.Client (parallel workers, per-file spans)
            long totalSize = files.Sum(f => (long)f.TotalSize);
            Directory.CreateDirectory(destinationPath);
            statusProgress?.Report("Preparing file list");
            foreach (var file in files)
            {
                var relativePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar);
                fileProgress?.Report(new DownloadFileProgress(
                    relativePath,
                    BytesDownloaded: 0,
                    TotalBytes: (long)file.TotalSize,
                    ProgressPercent: file.TotalSize == 0 ? 100 : 0,
                    BytesPerSecond: 0,
                    IsComplete: file.TotalSize == 0));
            }

            statusProgress?.Report("Downloading files");
            var (bytesTransferred, _, dlElapsed, skippedFiles) = await DownloadFilesInParallelAsync(
                files, depotId, depotKey, pool, destinationPath,
                parentSpanContext: workshopActivity?.Context ?? Activity.Current?.Context ?? default,
                progressBase: 0, progressTotal: totalSize,
                progress, fileProgress, logProgress: null,
                logPrefix: $"Workshop {workshopId}",
                maxParallelWorkers: Math.Clamp(maxParallelDownloads, 1, 64), ct);

            statusProgress?.Report("Checking downloaded files");
            var verifiedFiles = ValidateManifestFiles(destinationPath, files, skippedFiles);
            statusProgress?.Report("Pruning stale files");
            var prunedFiles = PruneFilesOutsideManifest(destinationPath, files);

            var totalMbDownloaded = bytesTransferred / 1_048_576.0;
            var avgMbps = dlElapsed.TotalSeconds > 0 ? totalMbDownloaded / dlElapsed.TotalSeconds : 0;

            workshopActivity?.SetTag("workshop.mb_transferred", Math.Round(totalMbDownloaded, 1));
            workshopActivity?.SetTag("workshop.duration_s", Math.Round(dlElapsed.TotalSeconds, 1));
            workshopActivity?.SetTag("workshop.avg_mbps", Math.Round(avgMbps, 2));
            workshopActivity?.SetTag("workshop.files_verified", verifiedFiles);
            workshopActivity?.SetTag("workshop.files_skipped", skippedFiles.Count);
            workshopActivity?.SetTag("workshop.files_pruned", prunedFiles);

            _logger.LogInformation("Workshop item {Id} download complete ({Mb:F1} MB in {Sec:F1}s, {Mbps:F1} MB/s avg)",
                workshopId, totalMbDownloaded, dlElapsed.TotalSeconds, avgMbps);
            progress?.Report(100);
            statusProgress?.Report("Download complete");
            return manifestId;
        }
        catch (Exception ex)
        {
            workshopActivity?.SetStatus(ActivityStatusCode.Error, _sanitizer.Sanitize(ex.Message));
            RecordExceptionEvent(workshopActivity, ex);
            throw;
        }
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

        using var depotActivity = KastActivitySources.Content.StartActivity(
            "kast.steam.depot", ActivityKind.Internal);
        depotActivity?.SetTag("depot.id", depotId);
        depotActivity?.SetTag("depot.manifest_id", manifest.ManifestGID);
        depotActivity?.SetTag("depot.file_count", files.Count);
        depotActivity?.SetTag("depot.size_mb", Math.Round(depotTotalBytes / 1_048_576.0, 1));

        try
        {

            // ── Phase 1: Quick scan — existence + size only (no hashing) ─────────
            var depotMb = depotTotalBytes / 1_048_576.0;
            _logger.LogInformation("Depot {DepotId}: scanning {Count} files ({Size:F0} MB)", depotId, files.Count, depotMb);
            logProgress?.Report($"  Depot {depotId}: scanning {files.Count} files ({depotMb:F0} MB)...");

            var toDownload = new List<DepotManifest.FileData>();
            var toVerify = new List<DepotManifest.FileData>();

            using (var scanActivity = KastActivitySources.Content.StartActivity(
                "kast.steam.depot.scan", ActivityKind.Internal))
            {
                scanActivity?.SetTag("depot.id", depotId);
                foreach (var file in files)
                {
                    var filePath = ResolveContentPath(destinationPath, file.FileName.Replace('\\', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(filePath);
                    if (dir != null) Directory.CreateDirectory(dir);

                    if (!File.Exists(filePath) || new FileInfo(filePath).Length != (long)file.TotalSize)
                        toDownload.Add(file);
                    else
                        toVerify.Add(file);
                }
                scanActivity?.SetTag("scan.to_download", toDownload.Count);
                scanActivity?.SetTag("scan.to_verify", toVerify.Count);
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
                using var verifyActivity = KastActivitySources.Content.StartActivity(
                    "kast.steam.depot.verify", ActivityKind.Internal);
                verifyActivity?.SetTag("depot.id", depotId);
                verifyActivity?.SetTag("verify.files", toVerify.Count);
                verifyActivity?.SetTag("verify.threads", maxParallelHash);

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

                            var path = ResolveContentPath(destinationPath, file.FileName.Replace('\\', Path.DirectorySeparatorChar));
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

                verifyActivity?.SetTag("verify.ok", toVerify.Count - hashFailed);
                verifyActivity?.SetTag("verify.failed", hashFailed);
                if (hashFailed > 0)
                    verifyActivity?.SetStatus(ActivityStatusCode.Ok, $"{hashFailed} file(s) corrupted");

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

                using var dlActivity = KastActivitySources.Content.StartActivity(
                    "kast.steam.depot.download", ActivityKind.Internal);
                dlActivity?.SetTag("depot.id", depotId);
                dlActivity?.SetTag("download.file_count", toDownload.Count);
                dlActivity?.SetTag("download.size_mb", Math.Round(downloadBytes / 1_048_576.0, 1));
                dlActivity?.SetTag("download.workers", maxParallelDownloads);

                var (bytesTransferred, filesTransferred, dlElapsed, _) = await DownloadFilesInParallelAsync(
                    toDownload, depotId, depotKey, pool, destinationPath,
                    parentSpanContext: dlActivity?.Context ?? Activity.Current?.Context ?? default,
                    progressBase: totalDownloaded, progressTotal: totalSize,
                    progress, null, logProgress,
                    logPrefix: $"Depot {depotId}",
                    maxParallelWorkers: maxParallelDownloads, ct);

                var totalMbDownloaded = bytesTransferred / 1_048_576.0;
                var avgMbps = dlElapsed.TotalSeconds > 0 ? totalMbDownloaded / dlElapsed.TotalSeconds : 0;

                totalDownloaded += bytesTransferred;
                downloadedCount = filesTransferred;

                dlActivity?.SetTag("download.mb_transferred", Math.Round(totalMbDownloaded, 1));
                dlActivity?.SetTag("download.duration_s", Math.Round(dlElapsed.TotalSeconds, 1));
                dlActivity?.SetTag("download.avg_mbps", Math.Round(avgMbps, 2));

                _logger.LogInformation("Depot {DepotId}: download complete — {MB:F1} MB in {Sec:F1}s ({Mbps:F1} MB/s avg)",
                    depotId, totalMbDownloaded, dlElapsed.TotalSeconds, avgMbps);
                logProgress?.Report(
                    $"  Depot {depotId}: {downloadedCount} file(s) downloaded ({totalMbDownloaded:F1} MB in {dlElapsed.TotalSeconds:F0}s, avg {avgMbps:F1} MB/s).");
            }

            if (totalSize > 0)
                progress?.Report((double)totalDownloaded / totalSize * 100.0);

            _logger.LogInformation("Depot {DepotId}: {Verified} verified, {Downloaded} downloaded ({FileCount} total)",
                depotId, verifiedCount, downloadedCount, files.Count);
            logProgress?.Report($"  Depot {depotId}: {verifiedCount} up-to-date, {downloadedCount} updated.");

            depotActivity?.SetTag("depot.files_verified", verifiedCount);
            depotActivity?.SetTag("depot.files_downloaded", downloadedCount);
        }
        catch (Exception ex)
        {
            depotActivity?.SetStatus(ActivityStatusCode.Error, _sanitizer.Sanitize(ex.Message));
            RecordExceptionEvent(depotActivity, ex);
            throw;
        }

        return (totalDownloaded, verifiedCount, downloadedCount);
    }

    /// <summary>
    /// Downloads depot data with retry attempts, rotating CDN servers between failures and
    /// backing off when Steam is throttling or temporarily unavailable.
    /// </summary>
    private const int MaxChunkRetries = 5;
    private const int MaxManifestRetries = 6;

    /// Records an exception as a span event using the OTel semantic convention,
    /// equivalent to Activity.RecordException() from the OpenTelemetry SDK.
    private void RecordExceptionEvent(Activity? activity, Exception ex)
    {
        if (activity is null) return;
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"]    = ex.GetType().FullName ?? ex.GetType().Name,
            ["exception.message"] = _sanitizer.Sanitize(ex.Message)
        }));
    }

    /// <summary>
    /// Fetches the depot decryption key for <paramref name="depotId"/>.
    /// Returns <c>null</c> and logs a warning when the key is unavailable.
    /// </summary>
    private async Task<byte[]?> GetDepotKeyAsync(uint depotId, uint appId)
    {
        var result = await _steamApps.GetDepotDecryptionKey(depotId, appId);
        if (result.Result == EResult.OK)
            return result.DepotKey;
        _logger.LogWarning("Could not get depot key for {DepotId} (AppId {AppId}): {Result}",
            depotId, appId, result.Result);
        return null;
    }

    /// <summary>
    /// Downloads a depot manifest via <see cref="_cdnClient"/>, automatically rotating to a
    /// fresh CDN server if the first attempt fails.  Handles pool server lifecycle (GetServer /
    /// ReturnServer) internally so callers do not need to manage server state.  Also decrypts
    /// filenames when a valid <paramref name="depotKey"/> is provided.
    /// Optionally records a <c>manifest.server_fallback</c> span event on
    /// <paramref name="spanActivity"/> when a CDN server rotation occurs.
    /// </summary>
    private async Task<DepotManifest> DownloadManifestWithFallbackAsync(
        uint depotId, ulong manifestId, ulong requestCode, byte[]? depotKey,
        CdnServerPool pool, Activity? spanActivity, CancellationToken ct)
    {
        DepotManifest? manifest = null;
        Exception? lastEx = null;

        for (var attempt = 1; attempt <= MaxManifestRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var server = pool.GetServer(ct);
        try
        {
            manifest = await _cdnClient.DownloadManifestAsync(depotId, manifestId, requestCode, server, depotKey);
            pool.ReturnServer(server, false);
            break;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            pool.ReturnServer(server, false);
            throw;
        }
        catch (Exception ex)
        {
            lastEx = ex;
            var transient = IsTransientSteamCdnException(ex);
            _logger.LogWarning(ex, "CDN manifest failed on {Host} for depot {DepotId} — trying next server",
                server.Host, depotId);
            spanActivity?.AddEvent(new ActivityEvent("manifest.server_fallback", tags: new ActivityTagsCollection
            {
                ["cdn.server"]        = server.Host,
                ["exception.message"] = _sanitizer.Sanitize(ex.Message)
            }));
            pool.ReturnServer(server, true);

            if (attempt == MaxManifestRetries)
                break;

            await Task.Delay(GetSteamRetryDelay(attempt, transient), ct);
        }
        }

        if (manifest == null)
        {
            throw new IOException(
                $"Failed to download depot manifest {manifestId} for depot {depotId} after {MaxManifestRetries} attempts. Steam may be rate limiting or temporarily unavailable.",
                lastEx);
        }

        if (manifest.FilenamesEncrypted && depotKey != null)
            manifest.DecryptFilenames(depotKey);

        return manifest;
    }

    /// <summary>
    /// Downloads <paramref name="files"/> in parallel to <paramref name="destinationPath"/>,
    /// creating a <c>kast.steam.file_download</c> child span (parented to
    /// <paramref name="parentSpanContext"/>) for every file.
    /// </summary>
    /// <param name="progressBase">
    /// Bytes already accounted-for in the overall operation (0 for a standalone download;
    /// non-zero when this is one phase of a multi-phase operation sharing a single progress bar).
    /// </param>
    /// <param name="progressTotal">
    /// Total bytes denominator for <paramref name="progress"/>. Pass 0 to suppress reporting.
    /// </param>
    /// <param name="logPrefix">Label prepended to debug log messages, e.g. "Workshop 12345" or "Depot 107410".</param>
    /// <returns>Bytes transferred, number of files completed, and wall-clock elapsed time.</returns>
    private async Task<(long BytesTransferred, int FilesTransferred, TimeSpan Elapsed, IReadOnlyCollection<string> SkippedFiles)>
        DownloadFilesInParallelAsync(
            IList<DepotManifest.FileData> files,
            uint depotId, byte[]? depotKey, CdnServerPool pool,
            string destinationPath,
            ActivityContext parentSpanContext,
            long progressBase, long progressTotal,
            IProgress<double>? progress,
            IProgress<DownloadFileProgress>? fileProgress,
            IProgress<string>? logProgress,
            string logPrefix,
            int maxParallelWorkers,
            CancellationToken ct)
    {
        long bytesDownloaded = 0;
        int filesDone = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastReportBytes = 0;
        var lastReportTime = sw.Elapsed;
        // Pre-compute so the lambda closure doesn't call Sum on every speed report.
        var downloadMb = files.Sum(f => (long)f.TotalSize) / 1_048_576.0;
        var skippedFiles = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Parallel.ForEachAsync only ever keeps MaxDegreeOfParallelism items in-flight.
        // Unlike Select().ToArray() + Task.WhenAll, it never queues thousands of async
        // tasks up-front, so cancellation is instant (only active files need to abort).
        // When any body throws, Parallel.ForEachAsync cancels the token passed to all
        // other in-progress bodies, so the "first error aborts the rest" invariant is
        // preserved without a manual CancellationTokenSource.
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxParallelWorkers),
                CancellationToken = ct
            },
            async (file, fileCt) =>
            {
                var relativePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar);
                var filePath = ResolveContentPath(destinationPath, relativePath);
                var fileSizeMb = file.TotalSize / 1_048_576.0;

                // Explicitly parent each span to the caller's span context.
                // Worker threads start with null Activity.Current so we must pass it explicitly.
                using var fileActivity = KastActivitySources.Content.StartActivity(
                    "kast.steam.file_download",
                    ActivityKind.Internal,
                    parentSpanContext);
                fileActivity?.SetTag("file.size_mb", Math.Round(fileSizeMb, 2));
                fileActivity?.SetTag("file.chunk_count", file.Chunks.Count);
                fileActivity?.SetTag("file.relative_path", _sanitizer.ToDisplayPath(relativePath));
                fileActivity?.SetTag("depot.id", depotId);

                try
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (dir != null) Directory.CreateDirectory(dir);

                    _logger.LogDebug("{Prefix}: downloading file {Path} ({Size:F2} MB)",
                        logPrefix, _sanitizer.ToDisplayPath(relativePath), fileSizeMb);

                    var tempPath = Path.Combine(
                        dir ?? destinationPath,
                        $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.kastdownload");
                    var downloadAttempt = 0;
                    var shouldMoveDownloadedFile = true;
                    long fileBytesDownloaded = 0;
                    var fileSw = System.Diagnostics.Stopwatch.StartNew();
                    const int maxHashRetries = 2;
                    while (true)
                    {
                        downloadAttempt++;
                        try
                        {
                            fileBytesDownloaded = 0;
                            fileSw.Restart();
                            fileProgress?.Report(new DownloadFileProgress(
                                relativePath,
                                BytesDownloaded: 0,
                                TotalBytes: (long)file.TotalSize,
                                ProgressPercent: file.TotalSize == 0 ? 100 : 0,
                                BytesPerSecond: 0,
                                IsComplete: file.TotalSize == 0));

                            await using (var fs = new FileStream(
                                             tempPath,
                                             FileMode.CreateNew,
                                             FileAccess.Write,
                                             FileShare.None,
                                             bufferSize: 1024 * 1024,
                                             FileOptions.SequentialScan | FileOptions.Asynchronous))
                            {
                                if (file.TotalSize > 0)
                                    fs.SetLength((long)file.TotalSize);

                                foreach (var chunk in file.Chunks.OrderBy(c => c.Offset))
                                {
                                    fileCt.ThrowIfCancellationRequested();
                                    var buf = new byte[chunk.UncompressedLength];
                                    var written = await DownloadChunkWithRetryAsync(depotId, chunk, pool, buf, depotKey, fileCt);
                                    fs.Position = (long)chunk.Offset;
                                    await fs.WriteAsync(buf.AsMemory(0, written), fileCt);

                                    var newBytes = Interlocked.Add(ref bytesDownloaded, written);
                                    fileBytesDownloaded += written;
                                    var fileProgressPercent = file.TotalSize > 0
                                        ? (double)fileBytesDownloaded / file.TotalSize * 100.0
                                        : 100.0;
                                    var fileSpeed = fileSw.Elapsed.TotalSeconds > 0
                                        ? fileBytesDownloaded / fileSw.Elapsed.TotalSeconds
                                        : 0;
                                    fileProgress?.Report(new DownloadFileProgress(
                                        relativePath,
                                        fileBytesDownloaded,
                                        (long)file.TotalSize,
                                        Math.Clamp(fileProgressPercent, 0, 100),
                                        fileSpeed));

                                    if (progressTotal > 0)
                                        progress?.Report((double)(progressBase + newBytes) / progressTotal * 100.0);
                                }
                            }

                            ValidateDownloadedFile(tempPath, file);
                            break; // success
                        }
                        catch (IOException ex) when (ex.Message.Contains("hash verification") && downloadAttempt < maxHashRetries)
                        {
                            _logger.LogWarning("{Prefix}: hash mismatch for {File}, retry {Attempt}/{Max}",
                                logPrefix, relativePath, downloadAttempt, maxHashRetries);
                            TryDeleteTempFile(tempPath);
                            tempPath = Path.Combine(
                                dir ?? destinationPath,
                                $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.kastdownload");
                        }
                        catch (IOException ex) when (ex.Message.Contains("hash verification") &&
                                                    Path.GetExtension(relativePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("{Prefix}: hash mismatch for non-critical file {File}, skipping",
                                logPrefix, relativePath);
                            TryDeleteTempFile(tempPath);
                            skippedFiles.Add(relativePath);
                            fileProgress?.Report(new DownloadFileProgress(
                                relativePath,
                                fileBytesDownloaded,
                                (long)file.TotalSize,
                                0,
                                0,
                                IsComplete: true,
                                IsSkipped: true,
                                Status: "Skipped"));
                            shouldMoveDownloadedFile = false;
                            break; // skip this file, continue with remaining files
                        }
                        catch
                        {
                            TryDeleteTempFile(tempPath);
                            throw;
                        }
                    }

                    if (shouldMoveDownloadedFile)
                    {
                        fileProgress?.Report(new DownloadFileProgress(
                            relativePath,
                            fileBytesDownloaded,
                            (long)file.TotalSize,
                            100,
                            0,
                            Status: "Moving..."));
                        File.Move(tempPath, filePath, overwrite: true);
                        var fileSpeed = fileSw.Elapsed.TotalSeconds > 0
                            ? fileBytesDownloaded / fileSw.Elapsed.TotalSeconds
                            : 0;
                        fileProgress?.Report(new DownloadFileProgress(
                            relativePath,
                            (long)file.TotalSize,
                            (long)file.TotalSize,
                            100,
                            fileSpeed,
                            IsComplete: true,
                            Status: "Complete"));
                    }

                    var done = Interlocked.Increment(ref filesDone);
                    _logger.LogDebug("{Prefix}: [{Done}/{Total}] file downloaded: {Path}",
                        logPrefix, done, files.Count, _sanitizer.ToDisplayPath(relativePath));

                    // Speed + progress report: every 10 files, large files (≥ 50 MB), last file, or every 5 s
                    var nowBytes = Interlocked.Read(ref bytesDownloaded);
                    var elapsed = sw.Elapsed;
                    var secSinceReport = (elapsed - lastReportTime).TotalSeconds;

                    if (done % 10 == 0 || fileSizeMb >= 50 || done == files.Count || secSinceReport >= 5)
                    {
                        var deltaBytes = nowBytes - lastReportBytes;
                        var mbps = secSinceReport > 0 ? (deltaBytes / 1_048_576.0) / secSinceReport : 0;
                        var totalMbDone = nowBytes / 1_048_576.0;

                        var report = $"  [{done}/{files.Count}] {_sanitizer.ToDisplayPath(relativePath)}  —  {totalMbDone:F0}/{downloadMb:F0} MB  ({mbps:F1} MB/s)";
                        logProgress?.Report(report);
                        // When no logProgress channel exists (e.g. workshop download), surface via logger.
                        if (logProgress is null)
                            _logger.LogDebug("{Prefix}: {Report}", logPrefix, report.TrimStart());

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
                    fileActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    fileActivity?.SetStatus(ActivityStatusCode.Error, _sanitizer.Sanitize(ex.Message));
                    RecordExceptionEvent(fileActivity, ex);
                    throw;
                }
            });

        sw.Stop();
        return (Interlocked.Read(ref bytesDownloaded), filesDone, sw.Elapsed, skippedFiles.ToArray());
    }

    private static string ResolveContentPath(string destinationPath, string relativePath)
    {
        var destinationRoot = Path.GetFullPath(destinationPath);
        var rootWithSeparator = destinationRoot.EndsWith(Path.DirectorySeparatorChar)
            ? destinationRoot
            : destinationRoot + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Depot file path '{relativePath}' would write outside the destination directory.");

        return fullPath;
    }

    private static int ValidateManifestFiles(
        string destinationPath,
        IReadOnlyCollection<DepotManifest.FileData> files,
        IReadOnlyCollection<string>? skippedFiles = null)
    {
        var skippedSet = skippedFiles is { Count: > 0 }
            ? skippedFiles.ToHashSet(GetContentPathComparer())
            : null;

        var verified = 0;
        foreach (var file in files)
        {
            var relativePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar);
            if (skippedSet?.Contains(relativePath) == true)
                continue;

            var filePath = ResolveContentPath(destinationPath, relativePath);
            if (!FileMatchesManifest(filePath, file))
                throw new IOException($"Downloaded file '{file.FileName}' is missing or failed final manifest verification.");
            verified++;
        }

        return verified;
    }

    private static int PruneFilesOutsideManifest(string destinationPath, IReadOnlyCollection<DepotManifest.FileData> files)
    {
        if (!Directory.Exists(destinationPath))
            return 0;

        var expectedPaths = files
            .Select(file => ResolveContentPath(destinationPath, file.FileName.Replace('\\', Path.DirectorySeparatorChar)))
            .ToHashSet(GetContentPathComparer());

        var pruned = 0;
        foreach (var filePath in Directory.EnumerateFiles(destinationPath, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(filePath);
            if (expectedPaths.Contains(fullPath))
                continue;

            try
            {
                File.Delete(fullPath);
                pruned++;
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to remove stale file '{fullPath}' from workshop mod directory.", ex);
            }
        }

        PruneEmptyDirectories(destinationPath);
        return pruned;
    }

    private static StringComparer GetContentPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static void PruneEmptyDirectories(string destinationPath)
    {
        foreach (var dir in Directory.EnumerateDirectories(destinationPath, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (IOException)
            {
                // Non-empty or transiently locked; harmless to leave behind.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup only.
            }
        }
    }

    private static void ValidateDownloadedFile(string filePath, DepotManifest.FileData file)
    {
        var info = new FileInfo(filePath);
        if (info.Length != (long)file.TotalSize)
            throw new IOException($"Downloaded file '{file.FileName}' has length {info.Length}, expected {file.TotalSize}.");

        if (file.FileHash is not { Length: > 0 })
            return;

        using var sha1 = System.Security.Cryptography.SHA1.Create();
        using var stream = File.OpenRead(filePath);
        if (!sha1.ComputeHash(stream).SequenceEqual(file.FileHash))
            throw new IOException($"Downloaded file '{file.FileName}' failed hash verification.");
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup; a future repair run will ignore stale temp files.
        }
    }

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
                var transient = IsTransientSteamCdnException(ex);
                _logger.LogWarning(ex,
                    "Chunk download failed on {Server} (attempt {Attempt}/{Max}) — discarding and retrying",
                    server.Host, attempt + 1, MaxChunkRetries);

                // Record chunk failure as an event on the file span (if still active)
                Activity.Current?.AddEvent(new ActivityEvent("chunk.retry", tags: new ActivityTagsCollection
                {
                    ["chunk.id"] = chunk.ChunkID is { Length: > 0 } ? Convert.ToHexString(chunk.ChunkID) : "unknown",
                    ["chunk.attempt"] = attempt + 1,
                    ["cdn.server"]    = server.Host,
                    ["error"]         = _sanitizer.Sanitize(ex.Message),
                    ["error.transient"] = transient
                }));
                pool.ReturnServer(server, true); // faulty — permanently discard it

                if (attempt < MaxChunkRetries - 1)
                    await Task.Delay(GetSteamRetryDelay(attempt + 1, transient), ct);
            }
        }

        throw new IOException(
            $"Failed to download chunk after {MaxChunkRetries} attempts. Steam may be rate limiting or temporarily unavailable.",
            lastEx);
    }

    private static TimeSpan GetSteamRetryDelay(int attempt, bool transient)
    {
        var baseSeconds = transient ? 3 : 1;
        var seconds = Math.Min(45, baseSeconds * Math.Pow(2, Math.Max(0, attempt - 1)));
        var jitterMs = Random.Shared.Next(250, 1250);
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitterMs);
    }

    private static bool IsTransientSteamCdnException(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException!)
        {
            var typeName = current.GetType().FullName ?? current.GetType().Name;
            var message = current.Message;

            if (typeName.Contains("SteamKitWebRequestException", StringComparison.OrdinalIgnoreCase) &&
                ContainsTransientHttpSignal(message))
            {
                return true;
            }

            if (current is HttpRequestException or IOException or TimeoutException &&
                ContainsTransientHttpSignal(message))
            {
                return true;
            }

            if (ContainsTransientHttpSignal(message))
                return true;
        }

        return false;
    }

    private static bool ContainsTransientHttpSignal(string message)
    {
        return message.Contains("429", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
               || message.Contains("500", StringComparison.OrdinalIgnoreCase)
               || message.Contains("502", StringComparison.OrdinalIgnoreCase)
               || message.Contains("503", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase)
               || message.Contains("504", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Gateway Timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);
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

        using var appActivity = KastActivitySources.Content.StartActivity(
            "kast.steam.app_download", ActivityKind.Internal);
                appActivity?.SetTag("app.id", appId);
                appActivity?.SetTag("app.branch", branch);
        appActivity?.SetTag("app.parallel_workers", maxParallelDownloads);

        try
        {
            _logger.LogInformation("Starting app download for AppId {AppId}", appId);
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
            appActivity?.SetTag("app.depot_count", depotManifests.Count);
            appActivity?.SetTag("app.os", currentOs);

            // 3. Connect to the CDN pool
            logProgress?.Report("Connecting to CDN servers...");
            var pool = await EnsureCdnPoolAsync(ct);
            Directory.CreateDirectory(destinationPath);

            long totalDownloaded = 0;
            long totalSize = 0;

            // First pass: fetch all manifests and collect total size
            var manifestCache = LoadManifestCache(destinationPath);
            var newManifestCache = new Dictionary<uint, ulong>(manifestCache);
            var manifests = new List<(uint DepotId, ulong ManifestId, byte[]? DepotKey, DepotManifest Manifest)>();

            foreach (var (depotId, manifestId) in depotManifests)
            {
                ct.ThrowIfCancellationRequested();
                logProgress?.Report($"Fetching manifest for depot {depotId}...");

                // Get depot key
                var depotKey = await GetDepotKeyAsync(depotId, appId);

                // Download manifest (automatic CDN fallback; server lifecycle managed by helper)
                var manifestRequestCode = await _steamContent.GetManifestRequestCode(depotId, appId, manifestId);
                DepotManifest manifest;
                using (var manifestActivity = KastActivitySources.Steam.StartActivity(
                    "kast.steam.depot.manifest", ActivityKind.Client))
                {
                    manifestActivity?.SetTag("depot.id", depotId);
                    manifestActivity?.SetTag("depot.manifest_id", manifestId);
                    manifestActivity?.SetTag("depot.key_obtained", depotKey != null);

                    // Pass manifestActivity so the helper can record a manifest.server_fallback event
                    manifest = await DownloadManifestWithFallbackAsync(
                        depotId, manifestId, manifestRequestCode, depotKey, pool, manifestActivity, ct);

                    if (manifest.FilenamesEncrypted)
                    {
                        _logger.LogWarning("Depot {DepotId}: filenames are encrypted and no valid depot key — skipping", depotId);
                        logProgress?.Report($"Depot {depotId}: skipped (encrypted filenames, no depot key). Try logging in with a Steam account that owns the game.");
                        manifestActivity?.SetTag("depot.skipped", true);
                        manifestActivity?.SetTag("depot.skip_reason", "encrypted_no_key");
                        manifestActivity?.SetStatus(ActivityStatusCode.Error, "encrypted filenames, no depot key");
                        continue;
                    }

                    manifestActivity?.SetTag("depot.file_count", manifest.Files?.Count ?? 0);
                    manifestActivity?.SetTag("depot.size_mb", Math.Round(manifest.TotalUncompressedSize / 1_048_576.0, 1));
                } // end manifestActivity

                manifests.Add((depotId, manifestId, depotKey, manifest));
                totalSize += (long)(manifest.TotalUncompressedSize);
                var sizeMb = manifest.TotalUncompressedSize / 1_048_576.0;
                logProgress?.Report($"Depot {depotId}: {manifest.Files?.Count ?? 0} files, {sizeMb:F0} MB");
            }

            var totalMb = totalSize / 1_048_576.0;
            _logger.LogInformation("Total size: {Size} bytes across {Count} depots", totalSize, manifests.Count);
            logProgress?.Report($"Total: {totalMb:F0} MB across {manifests.Count} depot(s). Checking for changes...");
            appActivity?.SetTag("app.total_size_mb", Math.Round(totalMb, 1));
            appActivity?.SetTag("app.manifests_fetched", manifests.Count);

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
                grandVerified += verified;
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

            appActivity?.SetTag("app.files_verified", grandVerified);
            appActivity?.SetTag("app.files_downloaded", grandDownloaded);
            appActivity?.SetTag("app.depots_skipped", skippedDepots);
        }
        catch (Exception ex)
        {
            appActivity?.SetStatus(ActivityStatusCode.Error, _sanitizer.Sanitize(ex.Message));
            RecordExceptionEvent(appActivity, ex);
            throw;
        }
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
        using var fs = File.OpenRead(filePath);
        return sha1.ComputeHash(fs).SequenceEqual(file.FileHash);
    }

    // ───── CDN Server Pool ─────

    private Task<CdnServerPool> EnsureCdnPoolAsync(CancellationToken ct = default)
    {
        // If a pool already exists and is healthy, return it immediately
        if (_cdnPool is not null)
            return Task.FromResult(_cdnPool);

        _cdnPool = new CdnServerPool(_steamClient, _steamContent, _logger, _sanitizer);
        _logger.LogInformation("CDN server pool created");
        return Task.FromResult(_cdnPool);
    }

    // ───── Download Benchmark ─────

    private const uint BenchmarkAppId = 233780; // Arma 3 DS
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

        using var benchActivity = KastActivitySources.Steam.StartActivity(
            "kast.steam.benchmark", ActivityKind.Internal);
        benchActivity?.SetTag("benchmark.app_id", BenchmarkAppId);
        benchActivity?.SetTag("benchmark.depot_id", BenchmarkDepotId);
        benchActivity?.SetTag("benchmark.levels", string.Join(",", BenchmarkLevels));
        benchActivity?.SetTag("benchmark.mb_per_level",
            BenchmarkTargetBytesPerLevel / 1_048_576.0);

        try
        {
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

            // 2. Get depot key + manifest (with automatic CDN fallback)
            var depotKey = await GetDepotKeyAsync(BenchmarkDepotId, BenchmarkAppId);

            var pool = await EnsureCdnPoolAsync(ct);
            var reqCode = await _steamContent.GetManifestRequestCode(BenchmarkDepotId, BenchmarkAppId, manifestId);
            var manifest = await DownloadManifestWithFallbackAsync(
                BenchmarkDepotId, manifestId, reqCode, depotKey, pool, benchActivity, ct);

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
            benchActivity?.SetTag("benchmark.chunks_collected", allChunks.Count);
            benchActivity?.SetTag("benchmark.mb_collected", Math.Round(collected / 1_048_576.0, 1));

            // Partition chunks into non-overlapping slices — one slice per level
            int chunksPerLevel = Math.Max(1, allChunks.Count / BenchmarkLevels.Length);
            var results = new List<BenchmarkResult>();

            // 4. Run each parallelism level on its own private chunk slice
            for (int i = 0; i < BenchmarkLevels.Length; i++)
            {
                var level = BenchmarkLevels[i];
                ct.ThrowIfCancellationRequested();

                int sliceStart = i * chunksPerLevel;
                int sliceEnd = (i == BenchmarkLevels.Length - 1) ? allChunks.Count : sliceStart + chunksPerLevel;
                if (sliceStart >= allChunks.Count) sliceStart = 0; // fallback: reuse from start if manifest too small
                if (sliceEnd > allChunks.Count) sliceEnd = allChunks.Count;

                var levelChunks = allChunks.GetRange(sliceStart, sliceEnd - sliceStart);
                long levelBytes = levelChunks.Sum(c => (long)c.UncompressedLength);
                log?.Report($"Testing {level,2} parallel download(s) ({levelBytes / 1_048_576.0:F1} MB)...");

                // Capture parent so the level span is correctly nested under the benchmark span
                var benchContext = benchActivity?.Context ?? Activity.Current?.Context ?? default;
                using var levelActivity = KastActivitySources.Steam.StartActivity(
                    "kast.steam.benchmark.level",
                    ActivityKind.Internal,
                    benchContext);
                levelActivity?.SetTag("benchmark.parallelism", level);
                levelActivity?.SetTag("benchmark.chunk_count", levelChunks.Count);
                levelActivity?.SetTag("benchmark.slice_mb", Math.Round(levelBytes / 1_048_576.0, 1));

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
                levelActivity?.SetTag("benchmark.bytes_downloaded", bytesDown);
                levelActivity?.SetTag("benchmark.elapsed_s", Math.Round(sw.Elapsed.TotalSeconds, 2));
                levelActivity?.SetTag("benchmark.mbps", Math.Round(mbps, 2));

                results.Add(new BenchmarkResult
                {
                    Parallelism = level,
                    BytesDownloaded = bytesDown,
                    ElapsedSeconds = sw.Elapsed.TotalSeconds,
                    MbPerSecond = mbps
                });

                log?.Report($"  {level,2} thread(s): {mbps:F1} MB/s ({sw.Elapsed.TotalSeconds:F1}s)");
            }

            var best = results.OrderByDescending(r => r.MbPerSecond).First();
            log?.Report($"Recommended: {best.Parallelism} parallel workers ({best.MbPerSecond:F1} MB/s)");

            benchActivity?.SetTag("benchmark.recommended_parallelism", best.Parallelism);
            benchActivity?.SetTag("benchmark.best_mbps", Math.Round(best.MbPerSecond, 2));

            return results;
        }
        catch (Exception ex)
        {
            benchActivity?.SetStatus(ActivityStatusCode.Error, _sanitizer.Sanitize(ex.Message));
            RecordExceptionEvent(benchActivity, ex);
            throw;
        }
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
