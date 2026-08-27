using System.Collections.Concurrent;
using System.Diagnostics;
using KAST.Core.Interfaces;
using KAST.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.CDN;

namespace KAST.Infrastructure.Steam;

/// <summary>
/// Self-replenishing pool of CDN servers for Arma 3 content downloads.
///
/// Inspired by the BytexDigital.Steam pool pattern, but stripped down to
/// what KAST actually needs (single-app, single-OS):
///
/// - Proven-good servers are pushed to a <see cref="ConcurrentStack{T}"/> and
///   reused first so we keep hitting fast, local servers.
/// - Faulty servers are permanently discarded instead of cycled back into
///   rotation — the key advantage over a simple round-robin index.
/// - A background task keeps the pool populated above a minimum threshold and
///   automatically re-discovers servers after a disconnect.
/// - Uses <see cref="ContentServerDirectoryService.LoadAsync"/> with the cell ID
///   received at login so Steam routes us to geographically close CDN nodes.
/// </summary>
internal sealed class CdnServerPool : IDisposable
{
    private const int MinimumPoolSize = 10;

    private readonly SteamClient _steamClient;
    private readonly SteamContent _steamContent;
    private readonly ILogger _logger;
    private readonly IOutputSanitizer _sanitizer;
    private readonly CancellationTokenSource _cts;

    // Proven-good servers from the current session — consumed first (LIFO)
    private readonly ConcurrentStack<Server> _proven = new();

    // Freshly discovered servers waiting to be used — fallback supply
    private readonly BlockingCollection<Server> _available = new();

    // Signals the monitor that the pool has dropped below minimum
    private readonly AutoResetEvent _refillNeeded = new(initialState: true);

    private readonly Task _monitorTask;

    /// <summary>
    /// Steam cell ID captured from <c>LoggedOnCallback</c>.
    /// Steam uses this to return geographically close CDN servers.
    /// </summary>
    public uint CellId { get; set; }

    public CdnServerPool(
        SteamClient steamClient,
        SteamContent steamContent,
        ILogger logger,
        IOutputSanitizer sanitizer,
        CancellationToken parentCt = default)
    {
        _steamClient = steamClient;
        _steamContent = steamContent;
        _logger = logger;
        _sanitizer = sanitizer;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        _monitorTask = Task.Run(MonitorAsync);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves a server from the pool.
    /// Prefers proven-good servers from the current session.
    /// Blocks until a server becomes available or <paramref name="ct"/> is cancelled.
    /// </summary>
    public Server GetServer(CancellationToken ct)
    {
        // Fast path: take a proven-good server from the current session
        if (_proven.TryPop(out var proven))
            return proven;

        // Signal the monitor we are running low
        if (_available.Count < MinimumPoolSize)
            _refillNeeded.Set();

        // Block until the monitor delivers a fresh server
        return _available.Take(ct);
    }

    /// <summary>
    /// Returns a server to the pool after use.
    /// <para>
    /// If <paramref name="isFaulty"/> is <c>true</c> the server is permanently
    /// discarded so subsequent downloads never hit it again in this session.
    /// Otherwise it is pushed to the proven stack for immediate reuse.
    /// </para>
    /// </summary>
    public void ReturnServer(Server server, bool isFaulty)
    {
        if (isFaulty)
        {
            _logger.LogDebug("Discarding faulty CDN server: {Host}", server.Host);
            // Do not return it — it stays out of the pool permanently
            return;
        }

        _proven.Push(server);
    }

    // ── Background monitor ────────────────────────────────────────────────────

    /// <summary>
    /// Monitors the CDN server pool and refills it as needed.
    /// </summary>
    private async Task MonitorAsync()
    {
        int throttleSeconds = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _refillNeeded.WaitOne(TimeSpan.FromSeconds(5));

                if (_cts.Token.IsCancellationRequested)
                    break;

                if (_available.Count >= MinimumPoolSize)
                    continue;

                _logger.LogDebug("CDN pool below minimum ({Count}/{Min}) — refilling (cell {Cell})",
                    _available.Count, MinimumPoolSize, CellId);

                await DelayForThrottleAsync(throttleSeconds);
                throttleSeconds = 0;

                await RefillPoolAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex) when (IsRateLimited(ex))
            {
                throttleSeconds = Math.Min(throttleSeconds + 5, 60);
                _logger.LogWarning("CDN directory rate-limited — backing off {Sec}s", throttleSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CDN pool monitor error — will retry");
                try { await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token); } catch { break; }
            }
        }
    }

    /// <summary>
    /// Delays the execution for the specified throttle duration.
    /// </summary>
    /// <param name="throttleSeconds">The number of seconds to delay.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DelayForThrottleAsync(int throttleSeconds)
    {
        if (throttleSeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(throttleSeconds), _cts.Token);
    }

    /// <summary>
    /// Refills the CDN server pool by discovering available servers and adding them to the pool.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RefillPoolAsync()
    {
        using var refillActivity = KastActivitySources.Steam.StartActivity(
            "kast.steam.cdn_pool.refill", ActivityKind.Internal);

        refillActivity?.SetTag("cdn.cell_id", CellId);
        refillActivity?.SetTag("cdn.pool_size_before", _available.Count);

        try
        {
            var servers = await LoadServersAsync();

            if (servers.Count == 0)
            {
                _logger.LogWarning("CDN server discovery returned no results — will retry");
                return;
            }

            var sorted = servers
                .Where(s => s.Type is "CDN" or "SteamCache")
                .OrderBy(s => s.WeightedLoad)
                .ToList();

            foreach (var server in sorted)
                _available.Add(server);

            _logger.LogInformation(
                "CDN pool refilled with {Count} servers (cell {Cell}, best: {Host})",
                sorted.Count,
                CellId,
                sorted.FirstOrDefault()?.Host ?? "none");

            refillActivity?.SetTag("cdn.servers_added", sorted.Count);
            refillActivity?.SetTag("cdn.best_server", sorted.FirstOrDefault()?.Host ?? "none");
            refillActivity?.SetTag("cdn.pool_size_after", _available.Count);
        }
        catch (Exception refillEx) when (refillEx is not OperationCanceledException)
        {
            RecordRefillFailure(refillActivity, refillEx);
            throw;
        }
    }

    /// <summary>
    /// Loads the list of available CDN servers for the current cell.
    /// </summary>
    /// <returns>A read-only collection of CDN servers.</returns>
    private async Task<IReadOnlyCollection<Server>> LoadServersAsync()
    {
        try
        {
            return await ContentServerDirectoryService.LoadAsync(
                _steamClient.Configuration,
                CellId,
                _cts.Token);
        }
        catch (SteamKitWebRequestException)
        {
            // Fallback for unavailable directory service.
            return await _steamContent.GetServersForSteamPipe();
        }
        catch (HttpRequestException)
        {
            // Fallback for network/transport failures.
            return await _steamContent.GetServersForSteamPipe();
        }
        catch (TaskCanceledException) when (!_cts.IsCancellationRequested)
        {
            // Fallback for transient timeout/cancellation from upstream request handling.
            return await _steamContent.GetServersForSteamPipe();
        }
        catch (InvalidOperationException)
        {
            // Fallback for invalid directory lookup state (for example missing/unknown cell context).
            return await _steamContent.GetServersForSteamPipe();
        }
    }

    /// <summary>
    /// Records a refill failure for the CDN server pool.
    /// </summary>
    /// <param name="refillActivity">The activity associated with the refill attempt.</param>
    /// <param name="refillEx">The exception that occurred during the refill.</param>
    private void RecordRefillFailure(Activity? refillActivity, Exception refillEx)
    {
        var safeMessage = _sanitizer.Sanitize(refillEx.Message);

        refillActivity?.SetStatus(ActivityStatusCode.Error, safeMessage);
        refillActivity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = refillEx.GetType().Name,
            ["exception.message"] = safeMessage
        }));
    }

    /// <summary>
    /// Determines whether the specified exception indicates that the request was rate limited.
    /// </summary>
    /// <param name="exception">The exception to check.</param>
    /// <returns><c>true</c> if the exception indicates a rate limit; otherwise, <c>false</c>.</returns>
    private static bool IsRateLimited(Exception exception) =>
        exception.Message.Contains("429", StringComparison.Ordinal) ||
        exception.Message.Contains("Too Many Requests", StringComparison.Ordinal);

    public bool IsMonitoring => !_monitorTask.IsCompleted;

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        _available.Dispose();
        _refillNeeded.Dispose();
        _cts.Dispose();
    }
}
