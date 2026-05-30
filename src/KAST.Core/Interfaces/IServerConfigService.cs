namespace KAST.Core.Interfaces;

public interface IServerConfigService
{
    /// <summary>
    /// Parses a server.cfg file content into structured key-value pairs.
    /// Uses AST-based parser that supports nested class blocks.
    /// Preserves comments, whitespace, and unknown keys for round-trip fidelity.
    /// </summary>
    ServerConfigData ParseServerConfig(string rawContent);

    /// <summary>
    /// Serializes structured config data back to raw file content.
    /// Preserves comments, whitespace, and unknown lines from the original AST.
    /// </summary>
    string SerializeServerConfig(ServerConfigData data);

    /// <summary>
    /// Parses basic.cfg content.
    /// </summary>
    BasicConfigData ParseBasicConfig(string rawContent);
    string SerializeBasicConfig(BasicConfigData data);

    /// <summary>
    /// Parses an Arma 3 difficulty profile file.
    /// </summary>
    Arma3ProfileData ParseArma3Profile(string rawContent);
    string SerializeArma3Profile(Arma3ProfileData data);
}

// ── AST Node types for Arma 3 config files ──

public abstract class ConfigNode
{
    public string RawText { get; set; } = string.Empty;
}

public class WhitespaceNode : ConfigNode;

public class CommentNode : ConfigNode;

public class KeyValueNode : ConfigNode
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

public class ArrayNode : ConfigNode
{
    public string Key { get; set; } = string.Empty;
    public List<string> Values { get; set; } = [];
    public string? Comment { get; set; }
}

public class ClassNode : ConfigNode
{
    public string Name { get; set; } = string.Empty;
    public List<ConfigNode> Children { get; set; } = [];
}

// ── Config data DTOs ──

public class ServerConfigData
{
    // ── Server Options ──
    public string? Hostname { get; set; }
    public string? Password { get; set; }
    public string? PasswordAdmin { get; set; }
    public string? ServerCommandPassword { get; set; }
    public int MaxPlayers { get; set; } = 32;
    public string? LogFile { get; set; } = "server_console.log";

    // MOTD
    public string? Motd { get; set; }
    public int MotdInterval { get; set; } = 5;

    // Admin UIDs
    public string? Admins { get; set; }

    // File patching exceptions
    public string? FilePatchingExceptions { get; set; }

    // ── Joining Rules ──
    public bool KickDuplicate { get; set; } = true;
    public int VerifySignatures { get; set; } = 2;
    public int AllowedFilePatching { get; set; }
    public int RequiredBuild { get; set; } = -1;
    public int SteamProtocolMaxDataSize { get; set; } = 1024;
    public bool Loopback { get; set; }
    public bool Upnp { get; set; }
    public bool EqualModRequired { get; set; }

    // ── Security Extensions ──
    public string? AllowedLoadFileExtensions { get; set; }
    public string? AllowedPreprocessFileExtensions { get; set; }
    public string? AllowedHTMLLoadExtensions { get; set; }
    public string? AllowedHTMLLoadURIs { get; set; }

    // ── Voting ──
    public bool VotingEnabled { get; set; } = true;
    public double VoteThreshold { get; set; } = 0.33;
    public int VoteMissionPlayers { get; set; } = 3;
    public int VotingTimeOut { get; set; } = 60;

    // ── In-game Settings ──
    public bool DisableVoN { get; set; }
    public int VonCodec { get; set; } = 1;                       // 0=SPEEX, 1=OPUS
    public int VonCodecQuality { get; set; } = 3;
    public bool SkipLobby { get; set; }
    public bool PersistentBattlefield { get; set; } = true;
    public bool AutoInit { get; set; }
    public bool BattlEye { get; set; } = true;
    public string TimeStampFormat { get; set; } = "short";        // none, short, full
    public string TimeStampFormatConsole { get; set; } = "short"; // none, short, full
    public string ForcedDifficulty { get; set; } = "Custom";
    public bool DrawingInMap { get; set; } = true;
    public int ForceRotorLibSimulation { get; set; }              // 0=player choice, 1=AFM, 2=SFM
    public bool AllowProfileGlasses { get; set; } = true;
    public int ZeusCompositionScriptLevel { get; set; } = 1;     // 0=forbidden, 1=attributes, 2=all
    public int OverrideHazeQuality { get; set; } = -1;            // -1=unset, 0=VeryLow, 1=Low, 2=Standard
    public int IdleFPSLimit { get; set; } = 30;
    public bool EnablePlayerDiag { get; set; }
    public bool StatisticsEnabled { get; set; } = true;
    public int CallExtReportLimit { get; set; } = 1000;

    // ── Logging ──
    public bool LogObjectNotFound { get; set; } = true;
    public bool SkipDescriptionParsing { get; set; }
    public bool IgnoreMissionLoadErrors { get; set; }
    public int ArmaUnitsTimeout { get; set; } = 30;
    public int QueueSizeLogG { get; set; } = 1000000;
    public bool NetlogEnabled { get; set; }

    // ── Timeouts ──
    public int DisconnectTimeout { get; set; } = 90;
    public int MaxDesync { get; set; } = 150;
    public int MaxPing { get; set; } = 200;
    public int MaxPacketLoss { get; set; } = 50;
    public bool KickClientOnSlowNetwork { get; set; }
    public int LobbyIdleTimeout { get; set; } = 300;
    public int BriefingTimeOut { get; set; } = 60;
    public int RoleTimeOut { get; set; } = 90;
    public int DebriefingTimeOut { get; set; } = 45;

    // ── Kick Timeout ──
    public int KickTimeoutManual { get; set; } = -1;    // -1=until missionEnd
    public int KickTimeoutConnect { get; set; } = 180;
    public int KickTimeoutBattlEye { get; set; } = 180;
    public int KickTimeoutHarmless { get; set; } = 180;

    // ── Mission ──
    public bool AutoSelectMission { get; set; } = true;
    public bool RandomMissionOrder { get; set; } = true;
    public int MissionsToServerRestart { get; set; }
    public int MissionsToShutdown { get; set; }
    public string? MissionWhitelist { get; set; }
    public string? MissionContentOverride { get; set; }
    public string? MissionHTTPDownloadBaseURL { get; set; }

    // ── Headless Client ──
    public string? HeadlessClients { get; set; }                  // newline-separated IPs
    public string? LocalClient { get; set; }                      // newline-separated IPs

    // ── Scripting ──
    public string? DoubleIdDetected { get; set; }
    public string? OnUserConnected { get; set; }
    public string? OnUserDisconnected { get; set; }
    public string? OnHackedData { get; set; } = "kick (_this select 0)";
    public string? OnDifferentData { get; set; }
    public string? OnUnsignedData { get; set; } = "kick (_this select 0)";
    public string? OnUserKicked { get; set; }
    public string? RegularCheck { get; set; }

    // ── Performance (launch args) ──
    public bool MaxMemOverride { get; set; }
    public int MaxMem { get; set; } = 1024;
    public bool CpuCountOverride { get; set; }
    public int CpuCount { get; set; }
    public bool EnableHT { get; set; }
    public bool EnableRanking { get; set; }

    // Store the full AST for round-trip fidelity
    public string RawContent { get; set; } = string.Empty;
    public List<ConfigNode> Ast { get; set; } = [];
    public List<string> UnknownLines { get; set; } = [];
}

public class BasicConfigData
{
    public int MaxMsgSend { get; set; } = 128;
    public int MaxSizeGuaranteed { get; set; } = 512;
    public int MaxSizeNonguaranteed { get; set; } = 256;
    public int MinBandwidth { get; set; } = 131072;
    public long MaxBandwidth { get; set; } = 10000000000;
    public double MinErrorToSend { get; set; } = 0.001;
    public double MinErrorToSendNear { get; set; } = 0.01;
    public int MaxCustomFileSize { get; set; } = 1024;
    public int MaxPacketSize { get; set; } = 1400;
    public int ViewDistance { get; set; } = 2000;
    public double TerrainGrid { get; set; } = 25;

    public string RawContent { get; set; } = string.Empty;
    public List<ConfigNode> Ast { get; set; } = [];
    public List<string> UnknownLines { get; set; } = [];
}

public class Arma3ProfileData
{
    // Simulation
    public int ReducedDamage { get; set; }

    // Situational awareness
    public int GroupIndicators { get; set; } = 2;       // 0=never, 1=limited distance, 2=always
    public int FriendlyTags { get; set; } = 1;
    public int EnemyTags { get; set; }
    public int DetectedMines { get; set; } = 1;
    public int Commands { get; set; } = 1;              // 0=never, 1=fade out, 2=always
    public int Waypoints { get; set; } = 1;
    public int TacticalPing { get; set; } = 1;          // 0=disable, 1=3D, 2=map, 3=both

    // Personal awareness
    public int WeaponInfo { get; set; } = 2;
    public int StanceIndicator { get; set; } = 2;
    public int StaminaBar { get; set; } = 1;            // 0=disabled, 1=enabled
    public int WeaponCrosshair { get; set; } = 1;
    public int VisionAid { get; set; }

    // View
    public int ThirdPersonView { get; set; } = 1;       // 0=disabled, 1=enabled, 2=vehicles only
    public int CameraShake { get; set; } = 1;

    // Multiplayer
    public int ScoreTable { get; set; } = 1;
    public int DeathMessages { get; set; } = 1;
    public int VonID { get; set; } = 1;

    // Misc
    public int MapContentFriendly { get; set; } = 1;
    public int MapContentEnemy { get; set; } = 1;
    public int MapContentMines { get; set; } = 1;
    public int AutoReport { get; set; }
    public int MultipleSaves { get; set; }

    // AI
    public int AiLevelPreset { get; set; } = 3;         // 0=Low, 1=Normal, 2=High, 3=Custom
    public double SkillAi { get; set; } = 0.5;
    public double PrecisionAi { get; set; } = 0.5;

    public string RawContent { get; set; } = string.Empty;
    public List<ConfigNode> Ast { get; set; } = [];
}
