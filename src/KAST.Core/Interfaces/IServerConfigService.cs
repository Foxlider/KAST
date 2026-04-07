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
    public int MaxCustomFileSize { get; set; }
    public int TerrainGridViewDistance { get; set; } = 50;

    public string RawContent { get; set; } = string.Empty;
    public List<ConfigNode> Ast { get; set; } = [];
    public List<string> UnknownLines { get; set; } = [];
}
