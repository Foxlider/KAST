using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace KAST.UI.Services;

public class ThemeService(ProtectedLocalStorage storage) : IDisposable
{
    private const string StorageKey = "kast_accent_color";
    private CancellationTokenSource? _cts;
    private bool _userOverride;

    public string AccentColor { get; private set; } = "#ff6b35"; // default orange

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        try
        {
            var result = await storage.GetAsync<string>(StorageKey);
            if (result.Success && !string.IsNullOrEmpty(result.Value))
            {
                AccentColor = result.Value;
                _userOverride = true;
                OnChange?.Invoke();
            }
        }
        catch
        { /* ignore during pre-render or when storage is unavailable */ }

        StartAprilFoolsIfNeeded();
    }

    public async Task SetAccentAsync(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;
        AccentColor = color;
        _userOverride = true;
        try
        {
            await storage.SetAsync(StorageKey, color);
        }
        catch { /* ignore */ }
        OnChange?.Invoke();
    }

    private void StartAprilFoolsIfNeeded()
    {
        // treat April 1st in local time; use DateTime.Now
        var localNow = DateTime.Now;
        if (localNow is not { Month: 4, Day: 1 }) return;
        
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = AprilFoolsLoopAsync(_cts.Token);
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
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }
        }
        catch (TaskCanceledException) { /* Ignore */ }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        _cts?.Cancel();
        _cts?.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
