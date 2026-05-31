using KAST.Infrastructure.Services;
using KAST.Infrastructure.Steam;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SteamKit2;
using SteamKit2.CDN;

namespace KAST.Tests;

public class CdnServerPoolTests
{
    [Fact]
    public void ReturnServer_NonFaulty_CanBeRetrievedByGetServer()
    {
        using var parent = new CancellationTokenSource();
        parent.Cancel(); // prevent monitor loop from doing any refill work

        var steamClient = new SteamClient();
        var steamContent = steamClient.GetHandler<SteamContent>();
        Assert.NotNull(steamContent);

        var logger = Substitute.For<ILogger>();
        using var pool = new CdnServerPool(steamClient, steamContent!, logger, new OutputSanitizer(), parent.Token);

        var server = CreateServerStub();
        pool.ReturnServer(server, isFaulty: false);

        var result = pool.GetServer(CancellationToken.None);

        Assert.Same(server, result);
    }

    [Fact]
    public void ReturnServer_Faulty_IsDiscarded()
    {
        using var parent = new CancellationTokenSource();
        parent.Cancel();

        var steamClient = new SteamClient();
        var steamContent = steamClient.GetHandler<SteamContent>();
        Assert.NotNull(steamContent);

        var logger = Substitute.For<ILogger>();
        using var pool = new CdnServerPool(steamClient, steamContent!, logger, new OutputSanitizer(), parent.Token);

        var server = CreateServerStub();
        pool.ReturnServer(server, isFaulty: true);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => pool.GetServer(cancelled.Token));
    }

    [Fact]
    public void GetServer_WhenEmpty_AndTokenCancelled_ThrowsOperationCanceled()
    {
        using var parent = new CancellationTokenSource();
        parent.Cancel();

        var steamClient = new SteamClient();
        var steamContent = steamClient.GetHandler<SteamContent>();
        Assert.NotNull(steamContent);

        var logger = Substitute.For<ILogger>();
        using var pool = new CdnServerPool(steamClient, steamContent!, logger, new OutputSanitizer(), parent.Token);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => pool.GetServer(cancelled.Token));
    }

    private static Server CreateServerStub()
    {
        var server = (Server)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Server));

        var hostProp = typeof(Server).GetProperty("Host");
        if (hostProp is { CanWrite: true })
            hostProp.SetValue(server, "test-cdn-host");

        return server;
    }
}
