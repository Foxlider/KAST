using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Services;
using KAST.Tests.Helpers;

namespace KAST.Tests;

public class MonitoringServiceTests : IDisposable
{
    private readonly Infrastructure.Data.KastDbContext _db = DbHelper.CreateInMemoryDb();

    [Fact]
    public async Task GetAllInstanceMetricsAsync_SamplesProcessesConcurrently()
    {
        _db.ServerInstances.AddRange(
            new ServerInstance { Name = "A", ProcessId = 1001, Status = ServerInstanceStatus.Running },
            new ServerInstance { Name = "B", ProcessId = 1002, Status = ServerInstanceStatus.Running },
            new ServerInstance { Name = "C", ProcessId = 1003, Status = ServerInstanceStatus.Running });
        await _db.SaveChangesAsync();
        var processManager = new DelayedProcessManager();
        var sut = new MonitoringService(processManager, _db);

        var metrics = await sut.GetAllInstanceMetricsAsync();

        Assert.Equal(3, metrics.Count);
        Assert.True(processManager.MaxConcurrentCalls > 1);
    }

    public void Dispose() => _db.Dispose();

    private sealed class DelayedProcessManager : IProcessManagerService
    {
        private int _activeCalls;

        public int MaxConcurrentCalls { get; private set; }

        public async Task<(double CpuPercent, long MemoryBytes)?> GetProcessMetricsAsync(int processId, CancellationToken ct = default)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, active);
            try
            {
                await Task.Delay(100, ct);
                return (processId / 100.0, processId);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public Task<int> StartServerProcessAsync(string executablePath, string arguments, Action<int, string>? onOutputLine = null,
            Action<int, int>? onProcessExited = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task StopProcessAsync(int processId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public bool IsProcessRunning(int processId) => true;
    }
}
