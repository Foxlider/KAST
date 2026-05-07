using System.Threading.Tasks;
using Microsoft.JSInterop;
using System.Threading;

namespace KAST.UI.Services;

public class ThemeService : IDisposable
{
    private const string StorageKey = "kast_accent_color";
    private readonly IJSRuntime? _js;
    private CancellationTokenSource? _cts;
    private bool _userOverride;

    public ThemeService(IJSRuntime? js)
    {
        _js = js;
    }

    public string AccentColor { get; private set; } = "#ff6b35"; // default orange

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        if (_js is null)
        {
            // still start auto behavior based on server time
            StartAprilFoolsIfNeeded();
            return;
        }

        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(stored))
            {
                AccentColor = stored!;
                _userOverride = true;
                OnChange?.Invoke();
            }
        }
        catch
        {
            // ignore (server render or localStorage unavailable)
        }

        StartAprilFoolsIfNeeded();
    }

    public async Task SetAccentAsync(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;
        AccentColor = color;
        _userOverride = true;
        try
        {
            if (_js is not null)
                await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, color);
        }
        catch
        {
            // ignore
        }
        OnChange?.Invoke();
    }

    private void StartAprilFoolsIfNeeded()
    {
        var now = DateTime.UtcNow;
        // treat April 1st in local time; use DateTime.Now
        var localNow = DateTime.Now;
        if (localNow.Month == 4 && localNow.Day == 1 || true)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ = AprilFoolsLoopAsync(_cts.Token);
        }
    }

    private async Task AprilFoolsLoopAsync(CancellationToken ct)
    {
        var palette = new[] { "#ff6b35", "#ff8a50", "#ffd166", "#4cc9f0", "#4cc9b0", "#e76f51", "#06d6a0" };
        var idx = 0;
        try
        {
            while (!ct.IsCancellationRequested && !_userOverride)
            {
                AccentColor = palette[idx % palette.Length];
                OnChange?.Invoke();
                idx++;
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (TaskCanceledException) { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
