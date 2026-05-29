using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KAST.Core.Interfaces;

namespace KAST.Infrastructure.Services;

/// <summary>
/// AST-based Arma 3 config parser/serializer.
/// Handles nested class blocks, arrays, key-value pairs, comments, and whitespace
/// with full round-trip fidelity.
/// </summary>
public partial class ServerConfigService : IServerConfigService
{
    // ── AST Parser ──

    private static List<ConfigNode> ParseAst(string content)
    {
        var lines = content.Split('\n');
        var index = 0;
        return ParseLines(lines, ref index);
    }

    private static ConfigNode? TryParseClassLine(string[] lines, string trimmed, ref int index)
    {
        var classMatch = ClassNameRegex().Match(trimmed);
        if (!classMatch.Success)
            return null;

        var classNode = new ClassNode
        {
            Name = classMatch.Groups[1].Value,
            RawText = lines[index]
        };
        index++;

        // Skip to opening brace (may be on the same line via trimmed check or next lines)
        if (!trimmed.Contains('{'))
        {
            while (index < lines.Length)
            {
                var nextTrimmed = lines[index].Trim();
                if (nextTrimmed.StartsWith("{"))
                {
                    index++;
                    break;
                }
                if (string.IsNullOrWhiteSpace(nextTrimmed))
                {
                    index++;
                    continue;
                }
                // Unexpected non-brace line, treat as part of class header
                index++;
                break;
            }
        }

        classNode.Children.AddRange(ParseLines(lines, ref index));
        return classNode;
    }

    private static ConfigNode? TryParseArrayLine(string[] lines, string trimmed, ref int index)
    {
        var arrayMatch = ArrayRegex().Match(trimmed);
        if (!arrayMatch.Success)
            return null;

        var arrayNode = new ArrayNode
        {
            Key = arrayMatch.Groups[1].Value,
            RawText = lines[index],
            Comment = arrayMatch.Groups[3].Success ? arrayMatch.Groups[3].Value.Trim() : null
        };
        var valuesStr = arrayMatch.Groups[2].Value;
        arrayNode.Values = ParseArrayValues(valuesStr);
        index++;
        return arrayNode;
    }

    private static ConfigNode? TryParseKeyValueLine(string[] lines, string trimmed, ref int index)
    {
        var kvMatch = KeyValueRegex().Match(trimmed);
        if (!kvMatch.Success)
            return null;

        var node = new KeyValueNode
        {
            Key = kvMatch.Groups[1].Value,
            Value = kvMatch.Groups[2].Value.Trim(),
            Comment = kvMatch.Groups[3].Success ? kvMatch.Groups[3].Value.Trim() : null,
            RawText = lines[index]
        };
        index++;
        return node;
    }

    private static List<ConfigNode> ParseLines(string[] lines, ref int index)
    {
        var nodes = new List<ConfigNode>();

        while (index < lines.Length)
        {
            var rawLine = lines[index];
            var trimmed = rawLine.Trim();

            // Empty / whitespace
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                nodes.Add(new WhitespaceNode { RawText = rawLine });
                index++;
                continue;
            }

            // Comment
            if (trimmed.StartsWith("//"))
            {
                nodes.Add(new CommentNode { RawText = rawLine });
                index++;
                continue;
            }

            // End of class block
            if (ClassEndRegex().IsMatch(trimmed))
            {
                index++;
                break;
            }

            // Try to parse as class
            var classNode = TryParseClassLine(lines, trimmed, ref index);
            if (classNode != null)
            {
                nodes.Add(classNode);
                continue;
            }

            // Try to parse as array
            var arrayNode = TryParseArrayLine(lines, trimmed, ref index);
            if (arrayNode != null)
            {
                nodes.Add(arrayNode);
                continue;
            }

            // Try to parse as key-value
            var kvNode = TryParseKeyValueLine(lines, trimmed, ref index);
            if (kvNode != null)
            {
                nodes.Add(kvNode);
                continue;
            }

            // Unrecognized line — preserve as whitespace node
            nodes.Add(new WhitespaceNode { RawText = rawLine });
            index++;
        }

        return nodes;
    }

    private static List<string> ParseArrayValues(string valuesStr)
    {
        var values = new List<string>();
        foreach (var part in valuesStr.Split(','))
        {
            var v = part.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(v))
                values.Add(v);
        }
        return values;
    }

    // ── AST Serializer ──

    private static string SerializeAst(List<ConfigNode> nodes, int indent = 0)
    {
        var sb = new StringBuilder();
        var prefix = new string('\t', indent);

        foreach (var node in nodes)
        {
            switch (node)
            {
                case ClassNode cls:
                    sb.AppendLine($"{prefix}class {cls.Name}");
                    sb.AppendLine($"{prefix}{{");
                    sb.Append(SerializeAst(cls.Children, indent + 1));
                    sb.AppendLine($"{prefix}}};");
                    break;

                case ArrayNode arr:
                    var vals = string.Join(", ", arr.Values.Select(v => $"\"{v}\""));
                    var arrLine = $"{prefix}{arr.Key}[] = {{{vals}}};";
                    if (!string.IsNullOrEmpty(arr.Comment))
                        arrLine += $" // {arr.Comment}";
                    sb.AppendLine(arrLine);
                    break;

                case KeyValueNode kv:
                    var kvLine = $"{prefix}{kv.Key} = {kv.Value};";
                    if (!string.IsNullOrEmpty(kv.Comment))
                        kvLine += $" // {kv.Comment}";
                    sb.AppendLine(kvLine);
                    break;

                case CommentNode:
                    sb.AppendLine(node.RawText);
                    break;

                case WhitespaceNode:
                    sb.AppendLine(node.RawText);
                    break;
            }
        }

        return sb.ToString();
    }

    // ── AST helpers ──

    private static string? FindValue(List<ConfigNode> ast, string key)
    {
        foreach (var node in ast)
        {
            if (node is KeyValueNode kv && kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return kv.Value.Trim('"');
        }
        return null;
    }

    private static List<string>? FindArrayValues(List<ConfigNode> ast, string key)
    {
        foreach (var node in ast)
        {
            if (node is ArrayNode arr && arr.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return arr.Values;
        }
        return null;
    }

    private static void SetValue(List<ConfigNode> ast, string key, string value, bool quoted = false)
    {
        var formatted = quoted ? $"\"{value}\"" : value;
        foreach (var node in ast)
        {
            if (node is KeyValueNode kv && kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                kv.Value = formatted;
                return;
            }
        }
        // Key doesn't exist — insert before first comment/whitespace trailing block
        ast.Add(new KeyValueNode { Key = key, Value = formatted });
    }

    private static void SetArrayValue(List<ConfigNode> ast, string key, List<string> values)
    {
        foreach (var node in ast)
        {
            if (node is ArrayNode arr && arr.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                arr.Values = values;
                return;
            }
        }
        ast.Add(new ArrayNode { Key = key, Values = values });
    }

    // ── Server Config (server.cfg) ──

    private static void ExtractNumericServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        // Joining Rules
        if (int.TryParse(FindValue(ast, "maxPlayers"), out var mp)) data.MaxPlayers = mp;
        if (int.TryParse(FindValue(ast, "verifySignatures"), out var vs)) data.VerifySignatures = vs;
        if (int.TryParse(FindValue(ast, "allowedFilePatching"), out var afp)) data.AllowedFilePatching = afp;
        if (int.TryParse(FindValue(ast, "requiredBuild"), out var rb)) data.RequiredBuild = rb;
        if (int.TryParse(FindValue(ast, "steamProtocolMaxDataSize"), out var spmd)) data.SteamProtocolMaxDataSize = spmd;

        // MOTD
        if (int.TryParse(FindValue(ast, "motdInterval"), out var mi)) data.MotdInterval = mi;

        // Voting
        if (double.TryParse(FindValue(ast, "voteThreshold"), CultureInfo.InvariantCulture, out var vt)) data.VoteThreshold = vt;
        if (int.TryParse(FindValue(ast, "voteMissionPlayers"), out var vmp)) data.VoteMissionPlayers = vmp;
        if (int.TryParse(FindValue(ast, "votingTimeOut"), out var vto)) data.VotingTimeOut = vto;

        // In-game Settings
        if (int.TryParse(FindValue(ast, "vonCodec"), out var vc)) data.VonCodec = vc;
        if (int.TryParse(FindValue(ast, "vonCodecQuality"), out var vcq)) data.VonCodecQuality = vcq;

        // Logging
        if (int.TryParse(FindValue(ast, "armaUnitsTimeout"), out var aut)) data.ArmaUnitsTimeout = aut;
        if (int.TryParse(FindValue(ast, "queueSizeLogG"), out var qslg)) data.QueueSizeLogG = qslg;

        // Timeouts
        if (int.TryParse(FindValue(ast, "disconnectTimeout"), out var dt)) data.DisconnectTimeout = dt;
        if (int.TryParse(FindValue(ast, "maxDesync"), out var md)) data.MaxDesync = md;
        if (int.TryParse(FindValue(ast, "maxPing"), out var mpi)) data.MaxPing = mpi;
        if (int.TryParse(FindValue(ast, "maxPacketLoss"), out var mpl)) data.MaxPacketLoss = mpl;
        if (int.TryParse(FindValue(ast, "lobbyIdleTimeout"), out var lit)) data.LobbyIdleTimeout = lit;
        if (int.TryParse(FindValue(ast, "briefingTimeOut"), out var bto)) data.BriefingTimeOut = bto;
        if (int.TryParse(FindValue(ast, "roleTimeOut"), out var rto)) data.RoleTimeOut = rto;
        if (int.TryParse(FindValue(ast, "debriefingTimeOut"), out var dbt)) data.DebriefingTimeOut = dbt;
    }

    private static void ExtractStringServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        // Server Options
        data.Hostname = FindValue(ast, "hostname");
        data.Password = FindValue(ast, "password");
        data.PasswordAdmin = FindValue(ast, "passwordAdmin");
        data.ServerCommandPassword = FindValue(ast, "serverCommandPassword");
        data.LogFile = FindValue(ast, "logFile") ?? "server_console.log";

        // In-game Settings
        data.TimeStampFormat = FindValue(ast, "timeStampFormat") ?? "short";
        data.ForcedDifficulty = FindValue(ast, "forcedDifficulty") ?? "Custom";

        // Scripting
        data.DoubleIdDetected = FindValue(ast, "doubleIdDetected");
        data.OnUserConnected = FindValue(ast, "onUserConnected");
        data.OnUserDisconnected = FindValue(ast, "onUserDisconnected");
        data.OnHackedData = FindValue(ast, "onHackedData") ?? "kick (_this select 0)";
        data.OnDifferentData = FindValue(ast, "onDifferentData");
        data.OnUnsignedData = FindValue(ast, "onUnsignedData") ?? "kick (_this select 0)";
        data.OnUserKicked = FindValue(ast, "onUserKicked");
    }

    private static void ExtractBooleanServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        data.KickDuplicate = FindValue(ast, "kickDuplicate") != "0";
        data.Loopback = FindValue(ast, "loopback") == "1";
        data.Upnp = FindValue(ast, "upnp") == "1";
        data.DisableVoN = FindValue(ast, "disableVoN") == "1";
        data.SkipLobby = FindValue(ast, "skipLobby") == "1";
        data.PersistentBattlefield = FindValue(ast, "persistent") == "1";
        data.AutoInit = FindValue(ast, "autoInit") is "1" or "true";
        data.BattlEye = FindValue(ast, "BattlEye") != "0";
        data.DrawingInMap = FindValue(ast, "drawingInMap") != "0";
        data.LogObjectNotFound = FindValue(ast, "LogObjectNotFound") != "0";
        data.SkipDescriptionParsing = FindValue(ast, "SkipDescriptionParsing") == "1";
        data.IgnoreMissionLoadErrors = FindValue(ast, "ignoreMissionLoadErrors") == "1";
        data.NetlogEnabled = FindValue(ast, "netlog") == "1";
        data.AutoSelectMission = FindValue(ast, "autoSelectMission") != "0";
        data.RandomMissionOrder = FindValue(ast, "randomMissionOrder") != "0";
        data.KickClientOnSlowNetwork = FindArrayValues(ast, "kickClientsOnSlowNetwork")?.Any(v => v == "1") ?? false;
    }

    private static void ExtractArrayServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        var motdValues = FindArrayValues(ast, "motd");
        if (motdValues is { Count: > 0 })
            data.Motd = string.Join("\n", motdValues);

        var adminValues = FindArrayValues(ast, "admins");
        if (adminValues is { Count: > 0 })
            data.Admins = string.Join("\n", adminValues);

        var allowedVoteCmds = FindArrayValues(ast, "allowedVoteCmds");
        data.VotingEnabled = allowedVoteCmds == null || allowedVoteCmds.Count > 0;

        var hcValues = FindArrayValues(ast, "headlessClients");
        if (hcValues is { Count: > 0 })
            data.HeadlessClients = string.Join("\n", hcValues);
        var lcValues = FindArrayValues(ast, "localClient");
        if (lcValues is { Count: > 0 })
            data.LocalClient = string.Join("\n", lcValues);
    }

    public ServerConfigData ParseServerConfig(string rawContent)
    {
        var ast = ParseAst(rawContent);
        var data = new ServerConfigData
        {
            RawContent = rawContent,
            Ast = ast
        };

        ExtractStringServerFields(ast, data);
        ExtractBooleanServerFields(ast, data);
        ExtractNumericServerFields(ast, data);
        ExtractArrayServerFields(ast, data);

        return data;
    }

    private static void SerializeStringServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        if (data.Hostname != null) SetValue(ast, "hostname", data.Hostname, quoted: true);
        if (data.Password != null) SetValue(ast, "password", data.Password, quoted: true);
        if (data.PasswordAdmin != null) SetValue(ast, "passwordAdmin", data.PasswordAdmin, quoted: true);
        if (data.ServerCommandPassword != null) SetValue(ast, "serverCommandPassword", data.ServerCommandPassword, quoted: true);
        SetValue(ast, "logFile", data.LogFile ?? "server_console.log", quoted: true);
        SetValue(ast, "timeStampFormat", data.TimeStampFormat, quoted: true);
        SetValue(ast, "forcedDifficulty", data.ForcedDifficulty, quoted: true);
    }

    private static void SerializeBooleanServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        SetValue(ast, "kickDuplicate", data.KickDuplicate ? "1" : "0");
        SetValue(ast, "loopback", data.Loopback ? "1" : "0");
        SetValue(ast, "upnp", data.Upnp ? "1" : "0");
        SetValue(ast, "disableVoN", data.DisableVoN ? "1" : "0");
        SetValue(ast, "skipLobby", data.SkipLobby ? "1" : "0");
        SetValue(ast, "persistent", data.PersistentBattlefield ? "1" : "0");
        SetValue(ast, "BattlEye", data.BattlEye ? "1" : "0");
        SetValue(ast, "drawingInMap", data.DrawingInMap ? "1" : "0");
        SetValue(ast, "LogObjectNotFound", data.LogObjectNotFound ? "1" : "0");
        SetValue(ast, "SkipDescriptionParsing", data.SkipDescriptionParsing ? "1" : "0");
        SetValue(ast, "ignoreMissionLoadErrors", data.IgnoreMissionLoadErrors ? "1" : "0");
        SetValue(ast, "randomMissionOrder", data.RandomMissionOrder ? "1" : "0");
        SetValue(ast, "autoSelectMission", data.AutoSelectMission ? "1" : "0");
    }

    private static void SerializeNumericServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        SetValue(ast, "maxPlayers", data.MaxPlayers.ToString());
        SetValue(ast, "verifySignatures", data.VerifySignatures.ToString());
        SetValue(ast, "allowedFilePatching", data.AllowedFilePatching.ToString());
        if (data.RequiredBuild > 0)
            SetValue(ast, "requiredBuild", data.RequiredBuild.ToString());
        SetValue(ast, "steamProtocolMaxDataSize", data.SteamProtocolMaxDataSize.ToString());
        SetValue(ast, "motdInterval", data.MotdInterval.ToString());
        SetValue(ast, "vonCodec", data.VonCodec.ToString());
        SetValue(ast, "vonCodecQuality", data.VonCodecQuality.ToString());
        SetValue(ast, "armaUnitsTimeout", data.ArmaUnitsTimeout.ToString());
        SetValue(ast, "queueSizeLogG", data.QueueSizeLogG.ToString());
        SetValue(ast, "disconnectTimeout", data.DisconnectTimeout.ToString());
        SetValue(ast, "maxDesync", data.MaxDesync.ToString());
        SetValue(ast, "maxPing", data.MaxPing.ToString());
        SetValue(ast, "maxPacketLoss", data.MaxPacketLoss.ToString());
        SetValue(ast, "lobbyIdleTimeout", data.LobbyIdleTimeout.ToString());
        SetValue(ast, "briefingTimeOut", data.BriefingTimeOut.ToString());
        SetValue(ast, "roleTimeOut", data.RoleTimeOut.ToString());
        SetValue(ast, "debriefingTimeOut", data.DebriefingTimeOut.ToString());
    }

    private static void SerializeVotingServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        SetValue(ast, "voteMissionPlayers", data.VotingEnabled ? data.VoteMissionPlayers.ToString() : "1");
        SetValue(ast, "voteThreshold", data.VotingEnabled
            ? data.VoteThreshold.ToString(CultureInfo.InvariantCulture)
            : "0");
        SetValue(ast, "votingTimeOut", data.VotingTimeOut.ToString());
        if (!data.VotingEnabled)
        {
            SetArrayValue(ast, "allowedVoteCmds", []);
            SetArrayValue(ast, "allowedVotedAdminCmds", []);
        }
    }

    private static void SerializeArrayServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        if (data.Motd != null)
            SetArrayValue(ast, "motd", data.Motd.Split('\n').ToList());
        if (data.Admins != null)
            SetArrayValue(ast, "admins", data.Admins.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());

        var kickSlowVal = data.KickClientOnSlowNetwork ? "1" : "0";
        SetArrayValue(ast, "kickClientsOnSlowNetwork", [kickSlowVal, kickSlowVal, kickSlowVal, kickSlowVal]);

        if (!string.IsNullOrEmpty(data.HeadlessClients))
            SetArrayValue(ast, "headlessClients", data.HeadlessClients.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
        if (!string.IsNullOrEmpty(data.LocalClient))
            SetArrayValue(ast, "localClient", data.LocalClient.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList());
    }

    private static void SerializeScriptingServerFields(List<ConfigNode> ast, ServerConfigData data)
    {
        if (data.DoubleIdDetected != null) SetValue(ast, "doubleIdDetected", data.DoubleIdDetected, quoted: true);
        if (data.OnUserConnected != null) SetValue(ast, "onUserConnected", data.OnUserConnected, quoted: true);
        if (data.OnUserDisconnected != null) SetValue(ast, "onUserDisconnected", data.OnUserDisconnected, quoted: true);
        if (data.OnHackedData != null) SetValue(ast, "onHackedData", data.OnHackedData, quoted: true);
        if (data.OnDifferentData != null) SetValue(ast, "onDifferentData", data.OnDifferentData, quoted: true);
        if (data.OnUnsignedData != null) SetValue(ast, "onUnsignedData", data.OnUnsignedData, quoted: true);
        if (data.OnUserKicked != null) SetValue(ast, "onUserKicked", data.OnUserKicked, quoted: true);
    }

    public string SerializeServerConfig(ServerConfigData data)
    {
        var ast = data.Ast;

        if (ast.Count == 0)
        {
            ast.Add(new CommentNode { RawText = "// KAST Server Configuration" });
            ast.Add(new CommentNode { RawText = "// Auto-generated — manual edits are preserved" });
            ast.Add(new WhitespaceNode { RawText = "" });
        }

        SerializeStringServerFields(ast, data);
        SerializeBooleanServerFields(ast, data);
        SerializeNumericServerFields(ast, data);
        SerializeVotingServerFields(ast, data);
        SerializeArrayServerFields(ast, data);
        SerializeScriptingServerFields(ast, data);

        return SerializeAst(ast).TrimEnd('\r', '\n');
    }

    // ── Basic Config (basic.cfg) ──

    public BasicConfigData ParseBasicConfig(string rawContent)
    {
        var ast = ParseAst(rawContent);
        var data = new BasicConfigData
        {
            RawContent = rawContent,
            Ast = ast
        };

        if (int.TryParse(FindValue(ast, "MaxMsgSend"), out var v1)) data.MaxMsgSend = v1;
        if (int.TryParse(FindValue(ast, "MaxSizeGuaranteed"), out var v2)) data.MaxSizeGuaranteed = v2;
        if (int.TryParse(FindValue(ast, "MaxSizeNonguaranteed"), out var v3)) data.MaxSizeNonguaranteed = v3;
        if (int.TryParse(FindValue(ast, "MinBandwidth"), out var v4)) data.MinBandwidth = v4;
        if (long.TryParse(FindValue(ast, "MaxBandwidth"), out var v5)) data.MaxBandwidth = v5;
        if (double.TryParse(FindValue(ast, "MinErrorToSend"), CultureInfo.InvariantCulture, out var v6)) data.MinErrorToSend = v6;
        if (double.TryParse(FindValue(ast, "MinErrorToSendNear"), CultureInfo.InvariantCulture, out var v7)) data.MinErrorToSendNear = v7;
        if (int.TryParse(FindValue(ast, "MaxCustomFileSize"), out var v8)) data.MaxCustomFileSize = v8;
        if (int.TryParse(FindValue(ast, "maxPacketSize"), out var v9)) data.MaxPacketSize = v9;
        if (int.TryParse(FindValue(ast, "viewDistance"), out var v10)) data.ViewDistance = v10;
        if (double.TryParse(FindValue(ast, "terrainGrid"), CultureInfo.InvariantCulture, out var v11)) data.TerrainGrid = v11;

        // Also check inside "class sockets" for maxPacketSize
        foreach (var node in ast)
        {
            if (node is ClassNode cls && cls.Name.Equals("sockets", StringComparison.OrdinalIgnoreCase) && int.TryParse(FindValue(cls.Children, "maxPacketSize"), out var ps))
                data.MaxPacketSize = ps;
        }

        return data;
    }

    public string SerializeBasicConfig(BasicConfigData data)
    {
        var ast = data.Ast;

        if (ast.Count == 0)
        {
            ast.Add(new CommentNode { RawText = "// KAST Basic Server Configuration" });
            ast.Add(new WhitespaceNode { RawText = "" });
        }

        SetValue(ast, "viewDistance", data.ViewDistance.ToString());
        SetValue(ast, "terrainGrid", data.TerrainGrid.ToString("0.###", CultureInfo.InvariantCulture));
        SetValue(ast, "MaxMsgSend", data.MaxMsgSend.ToString());
        SetValue(ast, "MaxSizeGuaranteed", data.MaxSizeGuaranteed.ToString());
        SetValue(ast, "MaxSizeNonguaranteed", data.MaxSizeNonguaranteed.ToString());
        SetValue(ast, "MinBandwidth", data.MinBandwidth.ToString());
        SetValue(ast, "MaxBandwidth", data.MaxBandwidth.ToString());
        SetValue(ast, "MinErrorToSend", data.MinErrorToSend.ToString(CultureInfo.InvariantCulture));
        SetValue(ast, "MinErrorToSendNear", data.MinErrorToSendNear.ToString(CultureInfo.InvariantCulture));
        SetValue(ast, "MaxCustomFileSize", data.MaxCustomFileSize.ToString());

        // Handle maxPacketSize inside class sockets
        var socketsClass = ast.OfType<ClassNode>().FirstOrDefault(c => c.Name.Equals("sockets", StringComparison.OrdinalIgnoreCase));
        if (socketsClass != null)
        {
            SetValue(socketsClass.Children, "maxPacketSize", data.MaxPacketSize.ToString());
        }
        else
        {
            ast.Add(new ClassNode
            {
                Name = "sockets",
                Children = [new KeyValueNode { Key = "maxPacketSize", Value = data.MaxPacketSize.ToString() }]
            });
        }

        return SerializeAst(ast).TrimEnd('\r', '\n');
    }

    // ── Arma 3 Difficulty Profile ──

    private static void ParseArma3ProfileOptions(List<ConfigNode> ast, Arma3ProfileData data)
    {
        var optionsAst = FindNestedClass(ast, "DifficultyPresets", "CustomDifficulty", "Options");
        if (optionsAst == null)
            return;

        if (int.TryParse(FindValue(optionsAst, "reducedDamage"), out var v)) data.ReducedDamage = v;
        if (int.TryParse(FindValue(optionsAst, "groupIndicators"), out v)) data.GroupIndicators = v;
        if (int.TryParse(FindValue(optionsAst, "friendlyTags"), out v)) data.FriendlyTags = v;
        if (int.TryParse(FindValue(optionsAst, "enemyTags"), out v)) data.EnemyTags = v;
        if (int.TryParse(FindValue(optionsAst, "detectedMines"), out v)) data.DetectedMines = v;
        if (int.TryParse(FindValue(optionsAst, "commands"), out v)) data.Commands = v;
        if (int.TryParse(FindValue(optionsAst, "waypoints"), out v)) data.Waypoints = v;
        if (int.TryParse(FindValue(optionsAst, "tacticalPing"), out v)) data.TacticalPing = v;
        if (int.TryParse(FindValue(optionsAst, "weaponInfo"), out v)) data.WeaponInfo = v;
        if (int.TryParse(FindValue(optionsAst, "stanceIndicator"), out v)) data.StanceIndicator = v;
        if (int.TryParse(FindValue(optionsAst, "staminaBar"), out v)) data.StaminaBar = v;
        if (int.TryParse(FindValue(optionsAst, "weaponCrosshair"), out v)) data.WeaponCrosshair = v;
        if (int.TryParse(FindValue(optionsAst, "visionAid"), out v)) data.VisionAid = v;
        if (int.TryParse(FindValue(optionsAst, "thirdPersonView"), out v)) data.ThirdPersonView = v;
        if (int.TryParse(FindValue(optionsAst, "cameraShake"), out v)) data.CameraShake = v;
        if (int.TryParse(FindValue(optionsAst, "scoreTable"), out v)) data.ScoreTable = v;
        if (int.TryParse(FindValue(optionsAst, "deathMessages"), out v)) data.DeathMessages = v;
        if (int.TryParse(FindValue(optionsAst, "vonID"), out v)) data.VonID = v;
        if (int.TryParse(FindValue(optionsAst, "mapContentFriendly"), out v)) data.MapContentFriendly = v;
        if (int.TryParse(FindValue(optionsAst, "mapContentEnemy"), out v)) data.MapContentEnemy = v;
        if (int.TryParse(FindValue(optionsAst, "mapContentMines"), out v)) data.MapContentMines = v;
        if (int.TryParse(FindValue(optionsAst, "autoReport"), out v)) data.AutoReport = v;
        if (int.TryParse(FindValue(optionsAst, "multipleSaves"), out v)) data.MultipleSaves = v;
    }

    private static void ParseArma3ProfileAILevels(List<ConfigNode> ast, Arma3ProfileData data)
    {
        var customDiffAst = FindNestedClass(ast, "DifficultyPresets", "CustomDifficulty");
        if (customDiffAst != null && int.TryParse(FindValue(customDiffAst, "aiLevelPreset"), out var ai))
            data.AiLevelPreset = ai;

        var customAiAst = FindNestedClass(ast, "DifficultyPresets", "CustomAILevel");
        if (customAiAst != null)
        {
            if (double.TryParse(FindValue(customAiAst, "skillAI"), CultureInfo.InvariantCulture, out var sk)) data.SkillAi = sk;
            if (double.TryParse(FindValue(customAiAst, "precisionAI"), CultureInfo.InvariantCulture, out var pr)) data.PrecisionAi = pr;
        }
    }

    public Arma3ProfileData ParseArma3Profile(string rawContent)
    {
        var ast = ParseAst(rawContent);
        var data = new Arma3ProfileData
        {
            RawContent = rawContent,
            Ast = ast
        };

        ParseArma3ProfileOptions(ast, data);
        ParseArma3ProfileAILevels(ast, data);

        return data;
    }

    public string SerializeArma3Profile(Arma3ProfileData data)
    {
        // Always generate a clean profile file (the structure is rigid)
        var sb = new StringBuilder();
        sb.AppendLine("//");
        sb.AppendLine("// Arma3Profile — KAST Auto-generated");
        sb.AppendLine("//");
        sb.AppendLine("class DifficultyPresets");
        sb.AppendLine("{");
        sb.AppendLine("\tclass CustomDifficulty");
        sb.AppendLine("\t{");
        sb.AppendLine("\t\tclass Options");
        sb.AppendLine("\t\t{");
        sb.AppendLine($"\t\t\t/* Simulation */");
        sb.AppendLine($"\t\t\treducedDamage = {data.ReducedDamage};");
        sb.AppendLine($"\t\t\t/* Situational awareness */");
        sb.AppendLine($"\t\t\tgroupIndicators = {data.GroupIndicators};\t\t// 0=never, 1=limited distance, 2=always");
        sb.AppendLine($"\t\t\tfriendlyTags = {data.FriendlyTags};");
        sb.AppendLine($"\t\t\tenemyTags = {data.EnemyTags};");
        sb.AppendLine($"\t\t\tdetectedMines = {data.DetectedMines};");
        sb.AppendLine($"\t\t\tcommands = {data.Commands};\t\t\t// 0=never, 1=fade out, 2=always");
        sb.AppendLine($"\t\t\twaypoints = {data.Waypoints};");
        sb.AppendLine($"\t\t\ttacticalPing = {data.TacticalPing};\t\t\t// 0=disable, 1=3D, 2=map, 3=both");
        sb.AppendLine($"\t\t\t/* Personal awareness */");
        sb.AppendLine($"\t\t\tweaponInfo = {data.WeaponInfo};");
        sb.AppendLine($"\t\t\tstanceIndicator = {data.StanceIndicator};");
        sb.AppendLine($"\t\t\tstaminaBar = {data.StaminaBar};");
        sb.AppendLine($"\t\t\tweaponCrosshair = {data.WeaponCrosshair};");
        sb.AppendLine($"\t\t\tvisionAid = {data.VisionAid};");
        sb.AppendLine($"\t\t\t/* View */");
        sb.AppendLine($"\t\t\tthirdPersonView = {data.ThirdPersonView};\t\t// 0=disabled, 1=enabled, 2=vehicles only");
        sb.AppendLine($"\t\t\tcameraShake = {data.CameraShake};");
        sb.AppendLine($"\t\t\t/* Multiplayer */");
        sb.AppendLine($"\t\t\tscoreTable = {data.ScoreTable};");
        sb.AppendLine($"\t\t\tdeathMessages = {data.DeathMessages};");
        sb.AppendLine($"\t\t\tvonID = {data.VonID};");
        sb.AppendLine($"\t\t\t/* Misc */");
        sb.AppendLine($"\t\t\tmapContentFriendly = {data.MapContentFriendly};");
        sb.AppendLine($"\t\t\tmapContentEnemy = {data.MapContentEnemy};");
        sb.AppendLine($"\t\t\tmapContentMines = {data.MapContentMines};");
        sb.AppendLine($"\t\t\tautoReport = {data.AutoReport};");
        sb.AppendLine($"\t\t\tmultipleSaves = {data.MultipleSaves};");
        sb.AppendLine("\t\t};");
        sb.AppendLine();
        sb.AppendLine($"\t\taiLevelPreset = {data.AiLevelPreset};\t\t// 0=Low, 1=Normal, 2=High, 3=Custom");
        sb.AppendLine("\t};");
        sb.AppendLine("\tclass CustomAILevel");
        sb.AppendLine("\t{");
        sb.AppendLine($"\t\tskillAI = {data.SkillAi.ToString(CultureInfo.InvariantCulture)};");
        sb.AppendLine($"\t\tprecisionAI = {data.PrecisionAi.ToString(CultureInfo.InvariantCulture)};");
        sb.AppendLine("\t};");
        sb.AppendLine("};");

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static List<ConfigNode>? FindNestedClass(List<ConfigNode> ast, params string[] path)
    {
        var current = ast;
        foreach (var name in path)
        {
            var cls = current.OfType<ClassNode>().FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cls == null) return null;
            current = cls.Children;
        }
        return current;
    }

    // ── Regex patterns ──

    [GeneratedRegex(@"^class\s+(\w+)\s*\{?\s*$")]
    private static partial Regex ClassNameRegex();

    [GeneratedRegex(@"^\s*\};\s*$")]
    private static partial Regex ClassEndRegex();

    [GeneratedRegex(@"^(\w+)\[\]\s*=\s*\{(.*)\}\s*;\s*(?://\s*(.*))?$")]
    private static partial Regex ArrayRegex();

    [GeneratedRegex(@"^(\w+)\s*=\s*(.*?)\s*;\s*(?://\s*(.*))?$")]
    private static partial Regex KeyValueRegex();
}
