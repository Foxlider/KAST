using KAST.Core.Events;
using KAST.Infrastructure.Services;

namespace KAST.Tests;

public class ArmaRptEventDetectorTests
{
    private readonly ArmaRptEventDetector _sut = new();
    private static readonly DateTime Timestamp = new(2026, 6, 6, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Detect_SteamInitialized_ReturnsPorts()
    {
        var events = _sut.Detect(1, "19:00:12 Initializing Steam server - Game Port: 2302, Steam Query Port: 2303", Timestamp);

        var evt = Assert.Single(events);
        Assert.Equal(ServerRuntimeEventKind.SteamInitialized, evt.Kind);
        Assert.Equal(2302, evt.GamePort);
        Assert.Equal(2303, evt.SteamQueryPort);
    }

    [Fact]
    public void Detect_SteamQueryOverflow_ReturnsWarning()
    {
        var events = _sut.Detect(1, "19:00:12 Warning: Steam Query data overflow, Mods/Signatures will not be correctly received by clients.", Timestamp);

        var evt = Assert.Single(events);
        Assert.Equal(ServerRuntimeEventKind.SteamQueryOverflow, evt.Kind);
        Assert.Equal(ServerRuntimeEventSeverity.Warning, evt.Severity);
    }

    [Theory]
    [InlineData("19:01:09 Admin logged in, player: Sr. Chief Neumann [3SA], playerUID: 76561198390146983, IP: 10.0.0.22:2304.", ServerRuntimeEventKind.AdminLogin)]
    [InlineData("22:15:02 Admin logged out, player: Sr. Chief Neumann [3SA], playerUID: 76561198390146983, IP: 10.0.0.22:2304.", ServerRuntimeEventKind.AdminLogout)]
    public void Detect_AdminActivity_ReturnsPlayerDetails(string line, ServerRuntimeEventKind kind)
    {
        var events = _sut.Detect(1, line, Timestamp);

        var evt = Assert.Single(events);
        Assert.Equal(kind, evt.Kind);
        Assert.Equal("Sr. Chief Neumann [3SA]", evt.PlayerName);
        Assert.Equal("76561198390146983", evt.PlayerUid);
        Assert.Equal("10.0.0.22:2304", evt.PlayerIp);
    }

    [Fact]
    public void Detect_MissionBlock_ReturnsMissionStartedAfterDirectory()
    {
        Assert.Empty(_sut.Detect(1, "19:18:45 Starting mission:", Timestamp));
        Assert.Empty(_sut.Detect(1, "19:18:45  Mission file: 40k_Saturday (__cur_mp)", Timestamp));
        Assert.Empty(_sut.Detect(1, "19:18:45  Mission world: Farabad", Timestamp));

        var events = _sut.Detect(1, "19:18:45  Mission directory: mpmissions\\__cur_mp.Farabad\\", Timestamp);

        var evt = Assert.Single(events);
        Assert.Equal(ServerRuntimeEventKind.MissionStarted, evt.Kind);
        Assert.Equal("40k_Saturday (__cur_mp)", evt.MissionFile);
        Assert.Equal("Farabad", evt.MissionWorld);
        Assert.Equal("mpmissions\\__cur_mp.Farabad\\", evt.MissionDirectory);
    }

    [Fact]
    public void Detect_MissingHeader_ReturnsWarning()
    {
        var events = _sut.Detect(1, "19:01:18 Mission 40k_Saturday.Farabad: Missing 'description.ext::Header'", Timestamp);

        var evt = Assert.Single(events);
        Assert.Equal(ServerRuntimeEventKind.MissionHeaderMissing, evt.Kind);
        Assert.Equal(ServerRuntimeEventSeverity.Warning, evt.Severity);
    }

    [Fact]
    public void Detect_MissingDownloadableContent_ReturnsCritical()
    {
        var events = _sut.Detect(1, "19:00:11 Warning Message: You cannot play/edit this mission; it is dependent on downloadable content that has been deleted.\\na3_characters_f", Timestamp);

        var evt = Assert.Single(events);
        Assert.Equal(ServerRuntimeEventKind.MissingDownloadableContent, evt.Kind);
        Assert.Equal(ServerRuntimeEventSeverity.Critical, evt.Severity);
    }
}
