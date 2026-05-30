using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;
using KAST.Infrastructure.Data;
using KAST.Infrastructure.Services.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace KAST.Tests;

public class ContentOrchestratorTests
{
    [Fact]
    public void Validate_UnknownInstallerType_ReturnsEmpty()
    {
        var provider = BuildServiceProvider();
        var orchestrator = BuildOrchestrator(provider, new[]
        {
            new FakeInstaller(ContentType.LocalMod, (_, _, _) => Task.CompletedTask)
        });

        var result = orchestrator.Validate(ContentType.Server, new ContentInstallRequest
        {
            Type = ContentType.Server,
            DestinationPath = "/tmp/srv"
        });

        Assert.Empty(result);
    }

    [Fact]
    public void Validate_KnownInstallerType_DelegatesToInstaller()
    {
        var provider = BuildServiceProvider();
        var orchestrator = BuildOrchestrator(provider, new[]
        {
            new FakeInstaller(ContentType.LocalMod, (_, _, _) => Task.CompletedTask)
        });

        var result = orchestrator.Validate(ContentType.LocalMod, new ContentInstallRequest
        {
            Type = ContentType.LocalMod,
            DestinationPath = "/tmp/mod"
        });

        Assert.Single(result);
        Assert.Equal("ok", result[0].Label);
    }

    [Fact]
    public async Task StartModInstall_Success_CompletesAndInvokesCallback()
    {
        var provider = BuildServiceProvider();
        var callbackCalled = false;

        var installers = new[]
        {
            new FakeInstaller(ContentType.SteamMod, async (_, state, _) =>
            {
                state.BeginStep(0);
                await Task.Delay(10);
                state.CompleteStep(0);
            })
        };

        var orchestrator = BuildOrchestrator(provider, installers);

        var state = orchestrator.StartModInstall(
            modId: 5,
            type: ContentType.SteamMod,
            destinationPath: "/tmp/mod5",
            workshopId: 333310405,
            onComplete: (_, _) =>
            {
                callbackCalled = true;
                return Task.CompletedTask;
            });

        await WaitForAsync(() => state.IsComplete || state.ErrorMessage is not null);

        Assert.True(state.IsComplete);
        Assert.Null(state.ErrorMessage);
        Assert.False(state.IsDownloading);
        Assert.True(callbackCalled);
        Assert.False(orchestrator.IsRunning(ContentProgressTracker.ModKey(5)));
    }

    [Fact]
    public async Task StartModInstall_Error_PopulatesStateAndCallsOnError()
    {
        var provider = BuildServiceProvider();
        Exception? captured = null;

        var installers = new[]
        {
            new FakeInstaller(ContentType.SteamMod, (_, _, _) => throw new InvalidOperationException("boom"))
        };

        var orchestrator = BuildOrchestrator(provider, installers);

        var state = orchestrator.StartModInstall(
            modId: 9,
            type: ContentType.SteamMod,
            destinationPath: "/tmp/mod9",
            onError: (_, _, ex) =>
            {
                captured = ex;
                return Task.CompletedTask;
            });

        await WaitForAsync(() => state.ErrorMessage is not null);

        Assert.NotNull(state.ErrorMessage);
        Assert.Contains("boom", state.ErrorMessage);
        Assert.NotNull(captured);
        Assert.IsType<InvalidOperationException>(captured);
    }

    [Fact]
    public async Task StartModInstall_AlreadyRunning_ReturnsSameState()
    {
        var provider = BuildServiceProvider();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var installers = new[]
        {
            new FakeInstaller(ContentType.SteamMod, async (_, state, ct) =>
            {
                state.BeginStep(0);
                await gate.Task.WaitAsync(ct);
                state.CompleteStep(0);
            })
        };

        var orchestrator = BuildOrchestrator(provider, installers);

        var first = orchestrator.StartModInstall(15, ContentType.SteamMod, "/tmp/mod15");
        var second = orchestrator.StartModInstall(15, ContentType.SteamMod, "/tmp/mod15");

        Assert.Same(first, second);

        gate.SetResult(true);
        await WaitForAsync(() => first.IsComplete || first.ErrorMessage is not null);
        Assert.True(first.IsComplete);
    }

    [Fact]
    public async Task StartModInstall_ParallelModDownloads_AllowsConfiguredConcurrentMods()
    {
        var provider = BuildServiceProvider();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var installers = new[]
        {
            new FakeInstaller(ContentType.SteamMod, async (_, state, ct) =>
            {
                Interlocked.Increment(ref started);
                state.BeginStep(0);
                await gate.Task.WaitAsync(ct);
                state.CompleteStep(0);
            })
        };

        var orchestrator = BuildOrchestrator(provider, installers);

        var first = orchestrator.StartModInstall(31, ContentType.SteamMod, "/tmp/mod31", maxParallelModDownloads: 2);
        var second = orchestrator.StartModInstall(32, ContentType.SteamMod, "/tmp/mod32", maxParallelModDownloads: 2);
        var third = orchestrator.StartModInstall(33, ContentType.SteamMod, "/tmp/mod33", maxParallelModDownloads: 2);

        await WaitForAsync(() => Volatile.Read(ref started) == 2);
        Assert.Equal(2, Volatile.Read(ref started));
        Assert.True(first.Steps[0].Status == ContentStepStatus.InProgress);
        Assert.True(second.Steps[0].Status == ContentStepStatus.InProgress);
        Assert.True(third.Steps[0].Status == ContentStepStatus.Pending);

        gate.SetResult(true);
        await WaitForAsync(() => first.IsComplete && second.IsComplete && third.IsComplete);
        Assert.Equal(3, Volatile.Read(ref started));
    }

    [Fact]
    public void Cancel_WhenKeyIsNotActive_DoesNotThrow()
    {
        var provider = BuildServiceProvider();
        var orchestrator = BuildOrchestrator(provider, new[]
        {
            new FakeInstaller(ContentType.SteamMod, (_, _, _) => Task.CompletedTask)
        });

        var ex = Record.Exception(() => orchestrator.Cancel("mod:404"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Cancel_ModInstall_SetsCancelledError()
    {
        var provider = BuildServiceProvider();

        var installers = new[]
        {
            new FakeInstaller(ContentType.SteamMod, async (_, state, ct) =>
            {
                state.BeginStep(0);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                state.CompleteStep(0);
            })
        };

        var orchestrator = BuildOrchestrator(provider, installers);
        var key = ContentProgressTracker.ModKey(22);
        var state = orchestrator.StartModInstall(22, ContentType.SteamMod, "/tmp/mod22");

        await Task.Delay(50);
        orchestrator.Cancel(key);

        await WaitForAsync(() => state.ErrorMessage is not null);

        Assert.Equal("Cancelled", state.ErrorMessage);
        Assert.False(state.IsDownloading);
        Assert.Equal(ContentStepStatus.Failed, state.Steps[0].Status);
        Assert.False(orchestrator.IsRunning(key));
    }

    [Fact]
    public async Task StartModInstall_SpuriousOperationCanceled_SetsNetworkTimeoutError()
    {
        var provider = BuildServiceProvider();
        var orchestrator = BuildOrchestrator(provider, new[]
        {
            new FakeInstaller(ContentType.SteamMod, (_, _, _) => throw new OperationCanceledException("transient"))
        });

        var state = orchestrator.StartModInstall(28, ContentType.SteamMod, "/tmp/mod28");
        await WaitForAsync(() => state.ErrorMessage is not null);

        Assert.Contains("Network timeout", state.ErrorMessage);
    }

    [Fact]
    public async Task StartServerInstall_UpdatesServerInstallFields()
    {
        var dbName = Guid.NewGuid().ToString();
        var provider = BuildServiceProvider(dbName);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KastDbContext>();
            db.ServerInstances.Add(new ServerInstance
            {
                Id = 100,
                Name = "Srv100",
                InstallPath = "/tmp/srv100",
                Status = ServerInstanceStatus.Stopped
            });
            await db.SaveChangesAsync();
        }

        var installers = new[]
        {
            new FakeInstaller(ContentType.Server, async (_, state, _) =>
            {
                state.BeginStep(0);
                await Task.Delay(10);
                state.CompleteStep(0);
            })
        };

        var orchestrator = BuildOrchestrator(provider, installers);

        var state = orchestrator.StartServerInstall(100, "/tmp/srv100", new ServerInstance
        {
            Id = 100,
            Name = "Srv100",
            InstallPath = "/tmp/srv100"
        }, 3);

        await WaitForAsync(() => state.IsComplete || state.ErrorMessage is not null);
        Assert.True(state.IsComplete);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<KastDbContext>();
        var updated = await verifyDb.ServerInstances.FindAsync(100);
        Assert.NotNull(updated);
        Assert.Equal(ServerInstanceStatus.Stopped, updated!.Status);
        Assert.NotNull(updated.InstalledAt);
        Assert.False(string.IsNullOrWhiteSpace(updated.InstalledBuildId));
    }

    [Fact]
    public async Task StartServerInstall_AlreadyRunning_ReturnsSameState()
    {
        var dbName = Guid.NewGuid().ToString();
        var provider = BuildServiceProvider(dbName);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator = BuildOrchestrator(provider, new[]
        {
            new FakeInstaller(ContentType.Server, async (_, state, ct) =>
            {
                state.BeginStep(0);
                await gate.Task.WaitAsync(ct);
                state.CompleteStep(0);
            })
        });

        var model = new ServerInstance { Id = 501, Name = "Srv501", InstallPath = "/tmp/srv501" };
        var first = orchestrator.StartServerInstall(501, "/tmp/srv501", model, 2);
        var second = orchestrator.StartServerInstall(501, "/tmp/srv501", model, 2);

        Assert.Same(first, second);
        gate.SetResult(true);
        await WaitForAsync(() => first.IsComplete || first.ErrorMessage is not null);
        Assert.True(first.IsComplete);
    }

    private static ContentOrchestrator BuildOrchestrator(IServiceProvider provider, IEnumerable<IContentInstaller> installers)
    {
        var tracker = new ContentProgressTracker();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = Substitute.For<ILogger<ContentOrchestrator>>();
        return new ContentOrchestrator(scopeFactory, tracker, installers, logger);
    }

    private static ServiceProvider BuildServiceProvider(string? dbName = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<KastDbContext>(opts => opts.UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString()));
        return services.BuildServiceProvider();
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var start = Environment.TickCount64;
        while (!predicate())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("Condition not met in time.");
            await Task.Delay(25);
        }
    }

    private sealed class FakeInstaller : IContentInstaller
    {
        private readonly Func<ContentInstallRequest, ContentInstallState, CancellationToken, Task> _run;

        public FakeInstaller(ContentType type, Func<ContentInstallRequest, ContentInstallState, CancellationToken, Task> run)
        {
            Type = type;
            _run = run;
        }

        public ContentType Type { get; }

        public IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request)
            => new[] { new ContentStep { Name = "Step" } };

        public Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
            => _run(request, state, ct);

        public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
            => new[] { new ContentValidationResult("ok", true) };
    }
}
