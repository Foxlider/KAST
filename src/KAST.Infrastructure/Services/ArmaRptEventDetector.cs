using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KAST.Core.Events;
using KAST.Core.Interfaces;

namespace KAST.Infrastructure.Services;

public sealed partial class ArmaRptEventDetector : IArmaRptEventDetector
{
    private readonly ConcurrentDictionary<int, MissionStartState> _missionStarts = new();

    public IReadOnlyList<ServerRuntimeEvent> Detect(int serverInstanceId, string line, DateTime timestamp)
    {
        var text = StripTimestamp(line);
        var events = new List<ServerRuntimeEvent>();

        if (TryDetectMissionStart(serverInstanceId, timestamp, line, text, events))
            return events;

        if (TryMatchSteamInitialized(serverInstanceId, timestamp, line, text, out var steamInitialized))
            return [steamInitialized];

        if (string.Equals(text, "Connected to Steam servers", StringComparison.OrdinalIgnoreCase))
            return [Create(serverInstanceId, timestamp, ServerRuntimeEventSeverity.Info,
                ServerRuntimeEventKind.SteamConnected, "Steam connected", "Connected to Steam servers.", line)];

        if (text.Contains("Steam Query data overflow", StringComparison.OrdinalIgnoreCase))
            return [Create(serverInstanceId, timestamp, ServerRuntimeEventSeverity.Warning,
                ServerRuntimeEventKind.SteamQueryOverflow, "Steam Query overflow", text, line)];

        if (TryMatchAdmin(serverInstanceId, timestamp, line, text, out var adminEvent))
            return [adminEvent];

        if (text.Contains("Missing 'description.ext::Header'", StringComparison.OrdinalIgnoreCase))
            return [Create(serverInstanceId, timestamp, ServerRuntimeEventSeverity.Warning,
                ServerRuntimeEventKind.MissionHeaderMissing, "Mission header missing", text, line)];

        if (text.StartsWith("Warning Message: You cannot play/edit this mission; it is dependent on downloadable content that has been deleted.",
                StringComparison.OrdinalIgnoreCase))
        {
            return [Create(serverInstanceId, timestamp, ServerRuntimeEventSeverity.Critical,
                ServerRuntimeEventKind.MissingDownloadableContent, "Missing mission dependency", text, line)];
        }

        var severity = GetGenericSeverity(text);
        if (severity is null)
            return [];

        return [Create(serverInstanceId, timestamp, severity.Value, GetGenericKind(severity.Value),
            severity == ServerRuntimeEventSeverity.Warning ? "Server warning" : "Server error", text, line)];
    }

    private bool TryDetectMissionStart(
        int serverInstanceId,
        DateTime timestamp,
        string sourceLine,
        string text,
        List<ServerRuntimeEvent> events)
    {
        if (string.Equals(text, "Starting mission:", StringComparison.OrdinalIgnoreCase))
        {
            _missionStarts[serverInstanceId] = new MissionStartState(timestamp);
            return true;
        }

        if (!_missionStarts.TryGetValue(serverInstanceId, out var state))
            return false;

        var fileMatch = MissionFileRegex().Match(text);
        if (fileMatch.Success)
        {
            state.MissionFile = fileMatch.Groups["file"].Value.Trim();
            return true;
        }

        var worldMatch = MissionWorldRegex().Match(text);
        if (worldMatch.Success)
        {
            state.MissionWorld = worldMatch.Groups["world"].Value.Trim();
            return true;
        }

        var directoryMatch = MissionDirectoryRegex().Match(text);
        if (!directoryMatch.Success)
            return false;

        state.MissionDirectory = directoryMatch.Groups["directory"].Value.Trim();
        _missionStarts.TryRemove(serverInstanceId, out _);

        var missionName = FirstNonBlank(state.MissionFile, state.MissionWorld, "mission");
        events.Add(new ServerRuntimeEvent(
            serverInstanceId,
            state.Timestamp,
            ServerRuntimeEventSeverity.Info,
            ServerRuntimeEventKind.MissionStarted,
            "Mission started",
            $"Mission started: {missionName}",
            sourceLine,
            state.MissionFile,
            state.MissionWorld,
            state.MissionDirectory));
        return true;
    }

    private static bool TryMatchSteamInitialized(
        int serverInstanceId,
        DateTime timestamp,
        string sourceLine,
        string text,
        out ServerRuntimeEvent runtimeEvent)
    {
        var match = SteamInitializedRegex().Match(text);
        if (!match.Success)
        {
            runtimeEvent = default!;
            return false;
        }

        var gamePort = int.Parse(match.Groups["gamePort"].Value);
        var queryPort = int.Parse(match.Groups["queryPort"].Value);
        runtimeEvent = new ServerRuntimeEvent(
            serverInstanceId,
            timestamp,
            ServerRuntimeEventSeverity.Info,
            ServerRuntimeEventKind.SteamInitialized,
            "Steam initialized",
            $"Game port {gamePort}, query port {queryPort}.",
            sourceLine,
            GamePort: gamePort,
            SteamQueryPort: queryPort);
        return true;
    }

    private static bool TryMatchAdmin(
        int serverInstanceId,
        DateTime timestamp,
        string sourceLine,
        string text,
        out ServerRuntimeEvent runtimeEvent)
    {
        var match = AdminRegex().Match(text);
        if (!match.Success)
        {
            runtimeEvent = default!;
            return false;
        }

        var action = match.Groups["action"].Value;
        var player = match.Groups["player"].Value.Trim();
        var uid = match.Groups["uid"].Value.Trim();
        var ip = match.Groups["ip"].Value.Trim();
        var isLogin = string.Equals(action, "in", StringComparison.OrdinalIgnoreCase);
        runtimeEvent = new ServerRuntimeEvent(
            serverInstanceId,
            timestamp,
            ServerRuntimeEventSeverity.Info,
            isLogin ? ServerRuntimeEventKind.AdminLogin : ServerRuntimeEventKind.AdminLogout,
            isLogin ? "Admin logged in" : "Admin logged out",
            $"{player} {(isLogin ? "logged in" : "logged out")} as admin.",
            sourceLine,
            PlayerName: player,
            PlayerUid: uid,
            PlayerIp: ip);
        return true;
    }

    private static ServerRuntimeEvent Create(
        int serverInstanceId,
        DateTime timestamp,
        ServerRuntimeEventSeverity severity,
        ServerRuntimeEventKind kind,
        string title,
        string message,
        string sourceLine)
        => new(serverInstanceId, timestamp, severity, kind, title, message, sourceLine);

    private static ServerRuntimeEventSeverity? GetGenericSeverity(string text)
    {
        if (text.Contains("fatal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("critical", StringComparison.OrdinalIgnoreCase))
            return ServerRuntimeEventSeverity.Critical;

        if (text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("exception", StringComparison.OrdinalIgnoreCase))
            return ServerRuntimeEventSeverity.Error;

        if (text.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("warn", StringComparison.OrdinalIgnoreCase))
            return ServerRuntimeEventSeverity.Warning;

        return null;
    }

    private static ServerRuntimeEventKind GetGenericKind(ServerRuntimeEventSeverity severity)
        => severity == ServerRuntimeEventSeverity.Warning
            ? ServerRuntimeEventKind.Warning
            : ServerRuntimeEventKind.Error;

    private static string StripTimestamp(string line)
        => TimestampRegex().Replace(line, "").Trim();

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private sealed class MissionStartState(DateTime timestamp)
    {
        public DateTime Timestamp { get; } = timestamp;
        public string? MissionFile { get; set; }
        public string? MissionWorld { get; set; }
        public string? MissionDirectory { get; set; }
    }

    [GeneratedRegex(@"^\s*\d{1,2}:\d{2}:\d{2}\s+")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^Initializing Steam server - Game Port:\s*(?<gamePort>\d+),\s*Steam Query Port:\s*(?<queryPort>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SteamInitializedRegex();

    [GeneratedRegex(@"^Admin logged (?<action>in|out), player:\s*(?<player>.*?),\s*playerUID:\s*(?<uid>\d+),\s*IP:\s*(?<ip>.+)\.$", RegexOptions.IgnoreCase)]
    private static partial Regex AdminRegex();

    [GeneratedRegex(@"^Mission file:\s*(?<file>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MissionFileRegex();

    [GeneratedRegex(@"^Mission world:\s*(?<world>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MissionWorldRegex();

    [GeneratedRegex(@"^Mission directory:\s*(?<directory>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MissionDirectoryRegex();
}
