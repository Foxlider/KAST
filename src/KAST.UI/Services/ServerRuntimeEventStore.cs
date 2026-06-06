using System.Collections.Concurrent;
using KAST.Core.Events;

namespace KAST.UI.Services;

public sealed class ServerRuntimeEventStore
{
    private const int MaxPerInstance = 200;

    private readonly ConcurrentDictionary<int, Queue<ServerRuntimeEvent>> _buffers = new();

    public void Add(ServerRuntimeEvent runtimeEvent)
    {
        var queue = _buffers.GetOrAdd(runtimeEvent.ServerInstanceId, _ => new Queue<ServerRuntimeEvent>());
        lock (queue)
        {
            queue.Enqueue(runtimeEvent);
            while (queue.Count > MaxPerInstance)
                queue.Dequeue();
        }
    }

    public IReadOnlyList<ServerRuntimeEvent> GetHistory(int instanceId)
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
