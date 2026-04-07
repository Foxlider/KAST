namespace KAST.Core.Interfaces;

public interface IServerConfigService
{
    /// <summary>
    /// Parses a server.cfg file content into structured key-value pairs.
    /// Preserves comments and unknown keys.
    /// </summary>
    ServerConfigData ParseServerConfig(string rawContent);

    /// <summary>
    /// Serializes structured config data back to raw file content.
    /// Preserves comments and unknown lines from the original.
    /// </summary>
    string SerializeServerConfig(ServerConfigData data);

    /// <summary>
    /// Parses basic.cfg content.
    /// </summary>
    BasicConfigData ParseBasicConfig(string rawContent);
    string SerializeBasicConfig(BasicConfigData data);
}

public class ServerConfigData
{
    public string? Hostname { get; set; }
    public string? Password { get; set; }
    public string? PasswordAdmin { get; set; }
    public string? ServerCommandPassword { get; set; }
    public int MaxPlayers { get; set; } = 32;
    public int VoteThreshold { get; set; } = 33;
    public int VoteMissionPlayers { get; set; } = 1;
    public bool VotingEnabled { get; set; } = true;
    public bool PersistentBattlefield { get; set; } = true;
    public bool BattlEye { get; set; } = true;
    public string? Motd { get; set; }
    public int MotdInterval { get; set; } = 5;

    // Store raw lines for round-trip fidelity
    public string RawContent { get; set; } = string.Empty;
    public List<string> UnknownLines { get; set; } = [];
}

public class BasicConfigData
{
    public int MaxMsgSend { get; set; } = 128;
    public int MaxSizeGuaranteed { get; set; } = 512;
    public int MaxSizeNonguaranteed { get; set; } = 256;
    public int MinBandwidth { get; set; } = 131072;
    public int MaxBandwidth { get; set; } = 10000000000;
    public double MinErrorToSend { get; set; } = 0.001;
    public double MinErrorToSendNear { get; set; } = 0.01;
    public int MaxCustomFileSize { get; set; }
    public int TerrainGridViewDistance { get; set; } = 50;

    public string RawContent { get; set; } = string.Empty;
    public List<string> UnknownLines { get; set; } = [];
}
