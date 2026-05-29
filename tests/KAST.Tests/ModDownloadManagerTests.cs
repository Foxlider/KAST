using KAST.Core.Interfaces;
using KAST.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace KAST.Tests;

public class ModDownloadManagerTests
{
    [Fact]
    public async Task StartDownload_RunsDownloadInBackgroundScope()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.DownloadModAsync(12, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.SetResult();
                return release.Task;
            });
        var manager = CreateManager(modService);

        var queued = manager.StartDownload(12, isUpdate: false);

        Assert.True(queued);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(manager.IsActive(12));
        release.SetResult();
        await WaitForAsync(() => !manager.IsActive(12));
        await modService.Received(1).DownloadModAsync(12, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartDownload_RejectsDuplicateActiveDownload()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.DownloadModAsync(7, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.SetResult();
                return release.Task;
            });
        var manager = CreateManager(modService);

        Assert.True(manager.StartDownload(7, isUpdate: false));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(manager.StartDownload(7, isUpdate: false));

        release.SetResult();
        await WaitForAsync(() => !manager.IsActive(7));
        await modService.Received(1).DownloadModAsync(7, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CancelsActiveDownloadToken()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.DownloadModAsync(3, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ct = call.ArgAt<CancellationToken>(2);
                started.SetResult(ct);
                return release.Task;
            });
        var manager = CreateManager(modService);

        Assert.True(manager.StartDownload(3, isUpdate: false));
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(manager.Cancel(3));
        Assert.True(token.IsCancellationRequested);

        release.SetCanceled(token);
        await WaitForAsync(() => !manager.IsActive(3));
    }

    [Fact]
    public async Task StartDownload_UsesUpdateWhenRequested()
    {
        var modService = Substitute.For<IModService>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        modService.UpdateModFilesAsync(5, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.SetResult();
                return Task.CompletedTask;
            });
        var manager = CreateManager(modService);

        Assert.True(manager.StartDownload(5, isUpdate: true));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForAsync(() => !manager.IsActive(5));
        await modService.Received(1).UpdateModFilesAsync(5, Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
    }

    private static ModDownloadManager CreateManager(IModService modService)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => modService);
        var provider = services.BuildServiceProvider();
        return new ModDownloadManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ModDownloadManager>.Instance);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
