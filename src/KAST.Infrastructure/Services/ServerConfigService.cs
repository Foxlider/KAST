using System.Text.RegularExpressions;
using KAST.Core.Interfaces;

namespace KAST.Infrastructure.Services;

public partial class ServerConfigService : IServerConfigService
{
    public ServerConfigData ParseServerConfig(string rawContent)
    {
        var data = new ServerConfigData { RawContent = rawContent };

        foreach (var line in rawContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                continue;

            var match = ConfigLineRegex().Match(trimmed);
            if (!match.Success)
            {
                data.UnknownLines.Add(line);
                continue;
            }

            var key = match.Groups[1].Value.ToLowerInvariant();
            var value = match.Groups[2].Value.Trim().TrimEnd(';').Trim('"');

            switch (key)
            {
                case "hostname": data.Hostname = value; break;
                case "password": data.Password = value; break;
                case "passwordadmin": data.PasswordAdmin = value; break;
                case "servercommandpassword": data.ServerCommandPassword = value; break;
                case "maxplayers": data.MaxPlayers = int.TryParse(value, out var mp) ? mp : 32; break;
                case "votethreshold": data.VoteThreshold = int.TryParse(value, out var vt) ? vt : 33; break;
                case "votemissionplayers": data.VoteMissionPlayers = int.TryParse(value, out var vmp) ? vmp : 1; break;
                case "allowedvotecmds[]": data.VotingEnabled = true; break;
                case "persistent": data.PersistentBattlefield = value == "1"; break;
                case "battleye": data.BattlEye = value == "1"; break;
                case "motd[]": data.Motd = value; break;
                case "motdinterval": data.MotdInterval = int.TryParse(value, out var mi) ? mi : 5; break;
                default: data.UnknownLines.Add(line); break;
            }
        }

        return data;
    }

    public string SerializeServerConfig(ServerConfigData data)
    {
        var lines = new List<string>
        {
            "// KAST Server Configuration",
            "// Auto-generated — manual edits are preserved",
            ""
        };

        if (data.Hostname != null) lines.Add($"hostname = \"{data.Hostname}\";");
        if (data.Password != null) lines.Add($"password = \"{data.Password}\";");
        if (data.PasswordAdmin != null) lines.Add($"passwordAdmin = \"{data.PasswordAdmin}\";");
        if (data.ServerCommandPassword != null) lines.Add($"serverCommandPassword = \"{data.ServerCommandPassword}\";");
        lines.Add($"maxPlayers = {data.MaxPlayers};");
        lines.Add($"voteThreshold = {data.VoteThreshold};");
        lines.Add($"voteMissionPlayers = {data.VoteMissionPlayers};");
        lines.Add($"persistent = {(data.PersistentBattlefield ? 1 : 0)};");
        lines.Add($"BattlEye = {(data.BattlEye ? 1 : 0)};");
        if (data.Motd != null) lines.Add($"motd[] = {{\"{data.Motd}\"}};");
        lines.Add($"motdInterval = {data.MotdInterval};");

        lines.Add("");
        lines.Add("// Additional settings");
        lines.AddRange(data.UnknownLines);

        return string.Join('\n', lines);
    }

    public BasicConfigData ParseBasicConfig(string rawContent)
    {
        var data = new BasicConfigData { RawContent = rawContent };

        foreach (var line in rawContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                continue;

            var match = ConfigLineRegex().Match(trimmed);
            if (!match.Success)
            {
                data.UnknownLines.Add(line);
                continue;
            }

            var key = match.Groups[1].Value.ToLowerInvariant();
            var value = match.Groups[2].Value.Trim().TrimEnd(';');

            switch (key)
            {
                case "maxmsgsend": data.MaxMsgSend = int.TryParse(value, out var v1) ? v1 : 128; break;
                case "maxsizeguaranteed": data.MaxSizeGuaranteed = int.TryParse(value, out var v2) ? v2 : 512; break;
                case "maxsizenonguaranteed": data.MaxSizeNonguaranteed = int.TryParse(value, out var v3) ? v3 : 256; break;
                case "minbandwidth": data.MinBandwidth = int.TryParse(value, out var v4) ? v4 : 131072; break;
                case "maxbandwidth": data.MaxBandwidth = int.TryParse(value, out var v5) ? v5 : 10000000000; break;
                case "minerrortosend": data.MinErrorToSend = double.TryParse(value, out var v6) ? v6 : 0.001; break;
                case "minerrortosendnear": data.MinErrorToSendNear = double.TryParse(value, out var v7) ? v7 : 0.01; break;
                case "maxcustomfilesize": data.MaxCustomFileSize = int.TryParse(value, out var v8) ? v8 : 0; break;
                case "terraingridviewdistance": data.TerrainGridViewDistance = int.TryParse(value, out var v9) ? v9 : 50; break;
                default: data.UnknownLines.Add(line); break;
            }
        }

        return data;
    }

    public string SerializeBasicConfig(BasicConfigData data)
    {
        var lines = new List<string>
        {
            "// KAST Basic Server Configuration",
            "",
            $"MaxMsgSend = {data.MaxMsgSend};",
            $"MaxSizeGuaranteed = {data.MaxSizeGuaranteed};",
            $"MaxSizeNonguaranteed = {data.MaxSizeNonguaranteed};",
            $"MinBandwidth = {data.MinBandwidth};",
            $"MaxBandwidth = {data.MaxBandwidth};",
            $"MinErrorToSend = {data.MinErrorToSend};",
            $"MinErrorToSendNear = {data.MinErrorToSendNear};",
            $"MaxCustomFileSize = {data.MaxCustomFileSize};",
            $"terrainGridViewDistance = {data.TerrainGridViewDistance};"
        };

        if (data.UnknownLines.Count > 0)
        {
            lines.Add("");
            lines.Add("// Additional settings");
            lines.AddRange(data.UnknownLines);
        }

        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"^(\w+(?:\[\])?)\s*=\s*(.+)$")]
    private static partial Regex ConfigLineRegex();
}
