using KAST.Core.Enums;
using KAST.Core.Models;
using KAST.Infrastructure.Services.Content;

namespace KAST.Tests;

public class ContentProgressTrackerTests
{
    [Fact]
    public void Keys_HaveExpectedFormat()
    {
        Assert.Equal("server:12", ContentProgressTracker.ServerKey(12));
        Assert.Equal("mod:34", ContentProgressTracker.ModKey(34));
    }

    [Fact]
    public void GetOrCreate_SameKey_ReturnsSameStateInstance()
    {
        var tracker = new ContentProgressTracker();
        var steps = new[] { new ContentStep { Name = "s1" } };

        var first = tracker.GetOrCreate("mod:1", ContentType.SteamMod, "Mod 1", steps);
        var second = tracker.GetOrCreate("mod:1", ContentType.SteamMod, "Ignored", new[] { new ContentStep { Name = "s2" } });

        Assert.Same(first, second);
        Assert.Equal("Mod 1", second.Label);
        Assert.Single(second.Steps);
        Assert.Equal("s1", second.Steps[0].Name);
    }

    [Fact]
    public void Create_ExistingKey_ReplacesExistingState()
    {
        var tracker = new ContentProgressTracker();

        var first = tracker.Create("mod:2", ContentType.SteamMod, "First", new[] { new ContentStep { Name = "a" } });
        var second = tracker.Create("mod:2", ContentType.LocalMod, "Second", new[] { new ContentStep { Name = "b" } });

        Assert.NotSame(first, second);
        var current = tracker.Get("mod:2");
        Assert.NotNull(current);
        Assert.Same(second, current);
        Assert.Equal(ContentType.LocalMod, current!.Type);
        Assert.Equal("Second", current.Label);
    }

    [Fact]
    public void Remove_DeletesState_AndGetAllReflectsCurrentSnapshot()
    {
        var tracker = new ContentProgressTracker();
        tracker.Create("mod:1", ContentType.SteamMod, "M1", new[] { new ContentStep { Name = "x" } });
        tracker.Create("server:1", ContentType.Server, "S1", new[] { new ContentStep { Name = "y" } });

        Assert.Equal(2, tracker.GetAll().Count);
        Assert.True(tracker.Remove("mod:1"));
        Assert.False(tracker.Remove("missing"));
        Assert.Null(tracker.Get("mod:1"));
        Assert.Single(tracker.GetAll());
    }
}
