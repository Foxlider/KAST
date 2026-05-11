using System.Collections.Concurrent;
using KAST.Core.Events;

namespace KAST.UI.Services;

/// <summary>
/// Singleton in-memory buffer of server console output, keyed by instance ID.
/// Survives page navigation so the Monitor tab can replay history when the
/// user returns to a server's ServerEdit page.
/// </summary>
public sealed class ServerConsoleStore
{
    private const int MaxPerInstance = 1000;

    private readonly ConcurrentDictionary<int, Queue<LogEntryEvent>> _buffers = new();

    public void Add(LogEntryEvent entry)
    {
        var queue = _buffers.GetOrAdd(entry.ServerInstanceId, _ => new Queue<LogEntryEvent>());
        lock (queue)
        {
            queue.Enqueue(entry);
            while (queue.Count > MaxPerInstance)
                queue.Dequeue();
        }
    }

    public IReadOnlyList<LogEntryEvent> GetHistory(int instanceId)
    {
        if (!_buffers.TryGetValue(instanceId, out var queue))
            return [];

        lock (queue)
            return queue.ToArray();
    }

    public void Clear(int instanceId)
    {
        if (_buffers.TryGetValue(instanceId, out var queue))
            lock (queue) queue.Clear();
    }
}
