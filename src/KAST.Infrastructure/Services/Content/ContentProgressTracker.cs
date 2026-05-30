using System.Collections.Concurrent;
using KAST.Core.Enums;
using KAST.Core.Models;

namespace KAST.Infrastructure.Services.Content;

/// <summary>
/// Singleton store mapping content keys to their live install state.
/// Keys: "server:{instanceId}", "mod:{modId}"
/// </summary>
public class ContentProgressTracker
{
    private readonly ConcurrentDictionary<string, ContentInstallState> _states = new();

    public static string ServerKey(int instanceId) => $"server:{instanceId}";
    public static string ModKey(int modId) => $"mod:{modId}";

    public ContentInstallState GetOrCreate(string key, ContentType type, string label, IReadOnlyList<ContentStep> steps)
    {
        return _states.GetOrAdd(key, _ => new ContentInstallState
        {
            Key = key,
            Type = type,
            Label = label,
            Steps = steps.ToList()
        });
    }

    public ContentInstallState Create(string key, ContentType type, string label, IReadOnlyList<ContentStep> steps)
    {
        var state = new ContentInstallState
        {
            Key = key,
            Type = type,
            Label = label,
            Steps = steps.ToList()
        };
        _states[key] = state;
        return state;
    }

    public void Set(ContentInstallState state)
    {
        _states[state.Key] = state;
    }

    public ContentInstallState? Get(string key)
        => _states.TryGetValue(key, out var s) ? s : null;

    public bool Remove(string key)
        => _states.TryRemove(key, out _);

    public IReadOnlyList<ContentInstallState> GetAll()
        => _states.Values.ToList();
}
