using System.Collections.Concurrent;
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
        CancellationToken parentCt = default)
    {
        _steamClient = steamClient;
        _steamContent = steamContent;
        _logger = logger;
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

    private async Task MonitorAsync()
    {
        int throttleSeconds = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Sleep up to 5 s between checks; wake early if the pool dips below minimum
                _refillNeeded.WaitOne(TimeSpan.FromSeconds(5));

                if (_available.Count >= MinimumPoolSize)
                    continue;

                _logger.LogDebug("CDN pool below minimum ({Count}/{Min}) — refilling (cell {Cell})",
                    _available.Count, MinimumPoolSize, CellId);

                if (throttleSeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(throttleSeconds), _cts.Token);
                    throttleSeconds = 0;
                }

                // Use ContentServerDirectoryService with our cell ID so Steam routes to
                // the closest CDN nodes (the same API BytexDigital uses)
                IReadOnlyCollection<Server> servers;
                try
                {
                    servers = await ContentServerDirectoryService.LoadAsync(
                        _steamClient.Configuration,
                        CellId,
                        _cts.Token);
                }
                catch
                {
                    // Fallback: use the SteamContent handler (does not carry cell ID hint
                    // but always works, even before the cell ID is known)
                    servers = await _steamContent.GetServersForSteamPipe();
                }

                if (servers.Count == 0)
                {
                    _logger.LogWarning("CDN server discovery returned no results — will retry");
                    continue;
                }

                var sorted = servers
                    .Where(s => s.Type is "CDN" or "SteamCache")
                    .OrderBy(s => s.WeightedLoad)
                    .ToList();

                foreach (var s in sorted)
                    _available.Add(s);

                _logger.LogInformation("CDN pool refilled with {Count} servers (cell {Cell}, best: {Host})",
                    sorted.Count, CellId, sorted.FirstOrDefault()?.Host ?? "none");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex.Message.Contains("429") || ex.Message.Contains("Too Many Requests"))
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

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _cts.Cancel();
        _available.Dispose();
        _refillNeeded.Dispose();
        _cts.Dispose();
    }
}
