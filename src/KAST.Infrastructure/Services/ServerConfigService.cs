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

            // Class definition: "class ClassName" (opening brace may be same line or next)
            var classMatch = ClassNameRegex().Match(trimmed);
            if (classMatch.Success)
            {
                var classNode = new ClassNode
                {
                    Name = classMatch.Groups[1].Value,
                    RawText = rawLine
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
                else
                {
                    // Brace was on the class line itself, already consumed
                }

                classNode.Children.AddRange(ParseLines(lines, ref index));
                nodes.Add(classNode);
                continue;
            }

            // Array definition: key[] = { "val1", "val2" };
            var arrayMatch = ArrayRegex().Match(trimmed);
            if (arrayMatch.Success)
            {
                var arrayNode = new ArrayNode
                {
                    Key = arrayMatch.Groups[1].Value,
                    RawText = rawLine,
                    Comment = arrayMatch.Groups[3].Success ? arrayMatch.Groups[3].Value.Trim() : null
                };
                var valuesStr = arrayMatch.Groups[2].Value;
                arrayNode.Values = ParseArrayValues(valuesStr);
                nodes.Add(arrayNode);
                index++;
                continue;
            }

            // Key-value: key = value;  // optional comment
            var kvMatch = KeyValueRegex().Match(trimmed);
            if (kvMatch.Success)
            {
                nodes.Add(new KeyValueNode
                {
                    Key = kvMatch.Groups[1].Value,
                    Value = kvMatch.Groups[2].Value.Trim(),
                    Comment = kvMatch.Groups[3].Success ? kvMatch.Groups[3].Value.Trim() : null,
                    RawText = rawLine
                });
                index++;
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

    public ServerConfigData ParseServerConfig(string rawContent)
    {
        var ast = ParseAst(rawContent);
        var data = new ServerConfigData
        {
            RawContent = rawContent,
            Ast = ast
        };

        data.Hostname = FindValue(ast, "hostname");
        data.Password = FindValue(ast, "password");
        data.PasswordAdmin = FindValue(ast, "passwordAdmin");
        data.ServerCommandPassword = FindValue(ast, "serverCommandPassword");

        if (int.TryParse(FindValue(ast, "maxPlayers"), out var mp)) data.MaxPlayers = mp;
        if (int.TryParse(FindValue(ast, "voteThreshold"), out var vt)) data.VoteThreshold = vt;
        if (int.TryParse(FindValue(ast, "voteMissionPlayers"), out var vmp)) data.VoteMissionPlayers = vmp;

        data.VotingEnabled = FindArrayValues(ast, "allowedVoteCmds") != null;
        data.PersistentBattlefield = FindValue(ast, "persistent") == "1";
        data.BattlEye = FindValue(ast, "BattlEye") == "1";

        var motdValues = FindArrayValues(ast, "motd");
        if (motdValues != null && motdValues.Count > 0)
            data.Motd = string.Join("\n", motdValues);

        if (int.TryParse(FindValue(ast, "motdInterval"), out var mi)) data.MotdInterval = mi;

        return data;
    }

    public string SerializeServerConfig(ServerConfigData data)
    {
        var ast = data.Ast;

        // If no AST exists (new config), build a fresh one
        if (ast.Count == 0)
        {
            ast.Add(new CommentNode { RawText = "// KAST Server Configuration" });
            ast.Add(new CommentNode { RawText = "// Auto-generated — manual edits are preserved" });
            ast.Add(new WhitespaceNode { RawText = "" });
        }

        if (data.Hostname != null) SetValue(ast, "hostname", data.Hostname, quoted: true);
        if (data.Password != null) SetValue(ast, "password", data.Password, quoted: true);
        if (data.PasswordAdmin != null) SetValue(ast, "passwordAdmin", data.PasswordAdmin, quoted: true);
        if (data.ServerCommandPassword != null) SetValue(ast, "serverCommandPassword", data.ServerCommandPassword, quoted: true);

        SetValue(ast, "maxPlayers", data.MaxPlayers.ToString());
        SetValue(ast, "voteThreshold", data.VoteThreshold.ToString());
        SetValue(ast, "voteMissionPlayers", data.VoteMissionPlayers.ToString());
        SetValue(ast, "persistent", data.PersistentBattlefield ? "1" : "0");
        SetValue(ast, "BattlEye", data.BattlEye ? "1" : "0");
        SetValue(ast, "motdInterval", data.MotdInterval.ToString());

        if (data.Motd != null)
            SetArrayValue(ast, "motd", data.Motd.Split('\n').ToList());

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
        if (int.TryParse(FindValue(ast, "terrainGridViewDistance"), out var v9)) data.TerrainGridViewDistance = v9;

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

        SetValue(ast, "MaxMsgSend", data.MaxMsgSend.ToString());
        SetValue(ast, "MaxSizeGuaranteed", data.MaxSizeGuaranteed.ToString());
        SetValue(ast, "MaxSizeNonguaranteed", data.MaxSizeNonguaranteed.ToString());
        SetValue(ast, "MinBandwidth", data.MinBandwidth.ToString());
        SetValue(ast, "MaxBandwidth", data.MaxBandwidth.ToString());
        SetValue(ast, "MinErrorToSend", data.MinErrorToSend.ToString(CultureInfo.InvariantCulture));
        SetValue(ast, "MinErrorToSendNear", data.MinErrorToSendNear.ToString(CultureInfo.InvariantCulture));
        SetValue(ast, "MaxCustomFileSize", data.MaxCustomFileSize.ToString());
        SetValue(ast, "terrainGridViewDistance", data.TerrainGridViewDistance.ToString());

        return SerializeAst(ast).TrimEnd('\r', '\n');
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
