using KAST.Infrastructure.Services;

namespace KAST.Tests;

public class ModDownloadCancellationRegistryTests
{
    [Fact]
    public void Cancel_CancelsOnlyTheRegisteredMod()
    {
        var registry = new ModDownloadCancellationRegistry();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using var firstRegistration = registry.Register(1, first);
        using var secondRegistration = registry.Register(2, second);

        Assert.True(registry.Cancel(1));

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
    }

    [Fact]
    public void DisposeRegistration_RemovesTheModFromTheRegistry()
    {
        var registry = new ModDownloadCancellationRegistry();
        using var cancellationSource = new CancellationTokenSource();
        var registration = registry.Register(1, cancellationSource);

        registration.Dispose();

        Assert.False(registry.Cancel(1));
        Assert.False(cancellationSource.IsCancellationRequested);
    }

    [Fact]
    public void CancelOrMarkPending_CancelsTheWorkerWhenItRegisters()
    {
        var registry = new ModDownloadCancellationRegistry();
        using var cancellationSource = new CancellationTokenSource();

        Assert.True(registry.CancelOrMarkPending(1));

        using var registration = registry.Register(1, cancellationSource);
        Assert.True(cancellationSource.IsCancellationRequested);
    }
}
