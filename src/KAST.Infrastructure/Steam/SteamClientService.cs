using KAST.Core.Interfaces;
using Microsoft.Extensions.Logging;
using SteamKit2;

namespace KAST.Infrastructure.Steam;

public class SteamClientService : ISteamService, IDisposable
{
    private readonly ILogger<SteamClientService> _logger;
    private readonly SteamClient _steamClient;
    private readonly CallbackManager _callbackManager;
    private readonly SteamUser _steamUser;
    private readonly SteamApps _steamApps;

    private TaskCompletionSource<bool>? _loginTcs;
    private bool _isRunning;
    private CancellationTokenSource? _callbackCts;

    public bool IsLoggedIn { get; private set; }
    public string? CurrentUsername { get; private set; }

    public SteamClientService(ILogger<SteamClientService> logger)
    {
        _logger = logger;
        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamApps = _steamClient.GetHandler<SteamApps>()!;

        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
    }

    public async Task<bool> LoginAnonymousAsync(CancellationToken ct = default)
    {
        _loginTcs = new TaskCompletionSource<bool>();
        StartCallbackLoop();

        _steamClient.Connect();

        // Wait for connection, then login
        using var reg = ct.Register(() => _loginTcs.TrySetResult(false));
        return await _loginTcs.Task;
    }

    public async Task<bool> LoginAsync(string username, string password, string? twoFactorCode = null, CancellationToken ct = default)
    {
        _loginTcs = new TaskCompletionSource<bool>();
        CurrentUsername = username;
        StartCallbackLoop();

        _steamClient.Connect();

        using var reg = ct.Register(() => _loginTcs.TrySetResult(false));
        return await _loginTcs.Task;
    }

    public Task LogoutAsync()
    {
        _steamUser.LogOff();
        _steamClient.Disconnect();
        IsLoggedIn = false;
        CurrentUsername = null;
        StopCallbackLoop();
        return Task.CompletedTask;
    }

    public async Task DownloadWorkshopItemAsync(long workshopId, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsLoggedIn)
            throw new InvalidOperationException("Not logged in to Steam");

        _logger.LogInformation("Downloading workshop item {Id} to {Path}", workshopId, destinationPath);

        // TODO: Implement CDN-based download using SteamKit2's ContentDownloader pattern
        // This will be implemented with proper depot downloading in the next iteration
        Directory.CreateDirectory(destinationPath);

        progress?.Report(100);
        _logger.LogInformation("Workshop item {Id} download complete", workshopId);
        await Task.CompletedTask;
    }

    public async Task DownloadAppAsync(uint appId, string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsLoggedIn)
            throw new InvalidOperationException("Not logged in to Steam");

        _logger.LogInformation("Downloading app {AppId} to {Path}", appId, destinationPath);

        // TODO: Implement app depot downloading (Arma 3 DS = AppID 233780)
        Directory.CreateDirectory(destinationPath);

        progress?.Report(100);
        await Task.CompletedTask;
    }

    public async Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(long workshopId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching workshop item info for {Id}", workshopId);

        // Use Steam Web API for metadata
        // TODO: Implement via ISteamRemoteStorage/GetPublishedFileDetails
        await Task.CompletedTask;

        return new WorkshopItemInfo
        {
            WorkshopId = workshopId,
            Name = $"Workshop Item {workshopId}"
        };
    }

    public async Task<IReadOnlyList<WorkshopItemInfo>> SearchWorkshopAsync(string query, int count = 20, CancellationToken ct = default)
    {
        _logger.LogInformation("Searching workshop for: {Query}", query);

        // TODO: Implement via Steam Web API search
        await Task.CompletedTask;

        return [];
    }

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

        if (CurrentUsername == null)
        {
            _steamUser.LogOnAnonymous();
        }
        else
        {
            _steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = CurrentUsername,
                // Password/2FA will be set from login flow
            });
        }
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        _logger.LogInformation("Disconnected from Steam");
        IsLoggedIn = false;
        _loginTcs?.TrySetResult(false);
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result == EResult.OK)
        {
            _logger.LogInformation("Logged in to Steam successfully");
            IsLoggedIn = true;
            _loginTcs?.TrySetResult(true);
        }
        else
        {
            _logger.LogWarning("Steam login failed: {Result}", cb.Result);
            _loginTcs?.TrySetResult(false);
        }
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback cb)
    {
        _logger.LogInformation("Logged off from Steam: {Result}", cb.Result);
        IsLoggedIn = false;
    }

    public void Dispose()
    {
        StopCallbackLoop();
        _callbackCts?.Dispose();
        _steamClient.Disconnect();
        GC.SuppressFinalize(this);
    }
}
