using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace KAST.UI.Services;

public class ThemeService(ProtectedLocalStorage storage) : IDisposable
{
    private const string AccentKey  = "kast_accent_color";
    private const string DarkModeKey = "kast_dark_mode";
    private CancellationTokenSource? _cts;
    private bool _userAccentOverride;

    /// <summary>Hex accent color applied as Secondary palette entry.</summary>
    public string AccentColor { get; private set; } = "#FF7043"; // Deep Orange 400

    /// <summary>Whether the dark theme is active.</summary>
    public bool IsDarkMode { get; private set; } = true;

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        try
        {
            var accentResult = await storage.GetAsync<string>(AccentKey);
            if (accentResult.Success && !string.IsNullOrEmpty(accentResult.Value))
            {
                AccentColor = accentResult.Value;
                _userAccentOverride = true;
            }

            var darkResult = await storage.GetAsync<bool>(DarkModeKey);
            if (darkResult.Success)
                IsDarkMode = darkResult.Value;
        }
        catch { /* ignore during pre-render or when storage is unavailable */ }

        StartAprilFoolsIfNeeded();
        OnChange?.Invoke();
    }

    public async Task SetAccentAsync(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return;
        AccentColor = color;
        _userAccentOverride = true;
        try { await storage.SetAsync(AccentKey, color); } catch { }
        OnChange?.Invoke();
    }

    public async Task SetDarkModeAsync(bool isDark)
    {
        IsDarkMode = isDark;
        try { await storage.SetAsync(DarkModeKey, isDark); } catch { }
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
        // Cycle through Material Design 400 hues on April Fools' Day
        var palette = new[]
        {
            "#FF7043", "#FFA726", "#FFCA28", "#66BB6A",
            "#26C6DA", "#42A5F5", "#5C6BC0", "#AB47BC",
            "#EC407A", "#EF5350",
        };
        var idx = 0;
        try
        {
            while (!ct.IsCancellationRequested && !_userAccentOverride)
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
