using Microsoft.JSInterop;

namespace KAST.UI.Services;

public class KeyboardShortcutService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly Dictionary<string, Func<Task>> _shortcuts = new();
    private DotNetObjectReference<KeyboardShortcutService>? _dotNetRef;
    private bool _initialized;

    public KeyboardShortcutService(IJSRuntime js) => _js = js;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _dotNetRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("KAST.keyboard.init", _dotNetRef);
        _initialized = true;
    }

    public void Register(string combo, Func<Task> handler)
    {
        _shortcuts[combo.ToLower()] = handler;
    }

    [JSInvokable]
    public async Task OnKeyDown(string combo)
    {
        if (_shortcuts.TryGetValue(combo.ToLower(), out var handler))
            await handler();
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            try { await _js.InvokeVoidAsync("KAST.keyboard.dispose"); } catch { }
        }
        _dotNetRef?.Dispose();
    }
}
