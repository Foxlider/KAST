using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.UI.Services;
using NSubstitute;

namespace KAST.Tests;

public class ModDownloadManagerTests
{
    [Fact]
    public async Task StartDownloadAsync_QueuesThroughDownloadQueue()
    {
        var queue = Substitute.For<IModDownloadQueueService>();
        queue.QueueDownloadAsync(12, false, Arg.Any<CancellationToken>())
            .Returns(new DownloadTask { Id = 1, ModId = 12, Status = DownloadStatus.Queued });
        var manager = new ModDownloadManager(queue);

        var queued = await manager.StartDownloadAsync(12, isUpdate: false);

        Assert.True(queued);
        await queue.Received(1).QueueDownloadAsync(12, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartDownloadAsync_ReturnsFalseWhenQueueRejectsMod()
    {
        var queue = Substitute.For<IModDownloadQueueService>();
        queue.QueueDownloadAsync(7, false, Arg.Any<CancellationToken>())
            .Returns((DownloadTask?)null);
        var manager = new ModDownloadManager(queue);

        Assert.False(await manager.StartDownloadAsync(7, isUpdate: false));
    }

    [Fact]
    public async Task StartAllOutdatedAsync_DelegatesToDownloadQueue()
    {
        var queue = Substitute.For<IModDownloadQueueService>();
        queue.QueueAllOutdatedAsync(Arg.Any<CancellationToken>()).Returns(42);
        var manager = new ModDownloadManager(queue);

        var queued = await manager.StartAllOutdatedAsync();

        Assert.Equal(42, queued);
        await queue.Received(1).QueueAllOutdatedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Cancel_DelegatesToDownloadQueue()
    {
        var queue = Substitute.For<IModDownloadQueueService>();
        queue.CancelAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        var manager = new ModDownloadManager(queue);

        Assert.True(manager.Cancel(3));
    }
}
