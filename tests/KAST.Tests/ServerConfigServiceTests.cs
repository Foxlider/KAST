using KAST.Core.Interfaces;
using KAST.Infrastructure.Services;

namespace KAST.Tests;

public class ServerConfigServiceTests
{
    private readonly IServerConfigService _sut = new ServerConfigService();

    // ── Server Config Parsing ──

    [Fact]
    public void ParseServerConfig_SimpleKeyValues_ParsesCorrectly()
    {
        var raw = """
            hostname = "My Arma Server";
            password = "secret";
            passwordAdmin = "admin123";
            maxPlayers = 64;
            """;

        var result = _sut.ParseServerConfig(raw);

        Assert.Equal("My Arma Server", result.Hostname);
        Assert.Equal("secret", result.Password);
        Assert.Equal("admin123", result.PasswordAdmin);
        Assert.Equal(64, result.MaxPlayers);
    }

    [Fact]
    public void ParseServerConfig_BooleanFields_ParsedFromIntValues()
    {
        var raw = """
            persistent = 1;
            BattlEye = 0;
            """;

        var result = _sut.ParseServerConfig(raw);

        Assert.True(result.PersistentBattlefield);
        Assert.False(result.BattlEye);
    }

    [Fact]
    public void ParseServerConfig_MotdArray_JoinedWithNewlines()
    {
        var raw = """
            motd[] = {"Welcome to the server", "Have fun", "Rules apply"};
            motdInterval = 10;
            """;

        var result = _sut.ParseServerConfig(raw);

        Assert.Equal("Welcome to the server\nHave fun\nRules apply", result.Motd);
        Assert.Equal(10, result.MotdInterval);
    }

    [Fact]
    public void ParseServerConfig_VotingEnabled_DetectsAllowedVoteCmds()
    {
        var raw = """
            allowedVoteCmds[] = {"admin", "kick", "missions"};
            """;

        var result = _sut.ParseServerConfig(raw);

        Assert.True(result.VotingEnabled);
    }

    [Fact]
    public void ParseServerConfig_VotingEnabled_ByDefault_WhenNoAllowedVoteCmds()
    {
        var raw = """
            hostname = "Test";
            """;

        var result = _sut.ParseServerConfig(raw);

        // Voting is enabled by default in Arma 3 unless explicitly disabled with empty allowedVoteCmds[]
        Assert.True(result.VotingEnabled);
    }

    [Fact]
    public void ParseServerConfig_VotingDisabled_WhenEmptyAllowedVoteCmds()
    {
        var raw = """
            hostname = "Test";
            allowedVoteCmds[] = {};
            """;

        var result = _sut.ParseServerConfig(raw);

        Assert.False(result.VotingEnabled);
    }

    [Fact]
    public void ParseServerConfig_Comments_PreservedInAst()
    {
        var raw = """
            // This is a comment
            hostname = "Test";
            // Another comment
            """;

        var result = _sut.ParseServerConfig(raw);

        Assert.Equal(3, result.Ast.Count);
        Assert.IsType<CommentNode>(result.Ast[0]);
        Assert.IsType<KeyValueNode>(result.Ast[1]);
        Assert.IsType<CommentNode>(result.Ast[2]);
    }

    [Fact]
    public void ParseServerConfig_EmptyLines_PreservedAsWhitespace()
    {
        var raw = "hostname = \"Test\";\n\npassword = \"pass\";";

        var result = _sut.ParseServerConfig(raw);

        Assert.Equal(3, result.Ast.Count);
        Assert.IsType<KeyValueNode>(result.Ast[0]);
        Assert.IsType<WhitespaceNode>(result.Ast[1]);
        Assert.IsType<KeyValueNode>(result.Ast[2]);
    }

    [Fact]
    public void ParseServerConfig_InlineComments_CapturedOnKeyValues()
    {
        var raw = """
            maxPlayers = 64; // Maximum player count
            """;

        var result = _sut.ParseServerConfig(raw);

        var kv = Assert.IsType<KeyValueNode>(result.Ast[0]);
        Assert.Equal("maxPlayers", kv.Key);
        Assert.Equal("64", kv.Value);
        Assert.Equal("Maximum player count", kv.Comment);
    }

    [Fact]
    public void ParseServerConfig_ClassBlocks_ParsedAsClassNodes()
    {
        var raw = """
            class Missions
            {
                class Mission1
                {
                    template = "MyMission.Altis";
                    difficulty = "veteran";
                };
            };
            """;

        var result = _sut.ParseServerConfig(raw);

        var classNode = Assert.IsType<ClassNode>(result.Ast[0]);
        Assert.Equal("Missions", classNode.Name);
        Assert.Single(classNode.Children); // Mission1

        var innerClass = Assert.IsType<ClassNode>(classNode.Children[0]);
        Assert.Equal("Mission1", innerClass.Name);
        Assert.Equal(2, innerClass.Children.Count);

        var template = Assert.IsType<KeyValueNode>(innerClass.Children[0]);
        Assert.Equal("template", template.Key);
        Assert.Equal("\"MyMission.Altis\"", template.Value);
    }

    // ── Server Config Serialization ──

    [Fact]
    public void SerializeServerConfig_RoundTrip_PreservesStructure()
    {
        var raw = """
            // Server configuration
            hostname = "My Server";
            password = "secret";
            maxPlayers = 64;
            persistent = 1;
            BattlEye = 1;
            voteThreshold = 33;
            voteMissionPlayers = 1;
            motdInterval = 5;
            """;

        var parsed = _sut.ParseServerConfig(raw);
        var serialized = _sut.SerializeServerConfig(parsed);
        var reparsed = _sut.ParseServerConfig(serialized);

        Assert.Equal(parsed.Hostname, reparsed.Hostname);
        Assert.Equal(parsed.Password, reparsed.Password);
        Assert.Equal(parsed.MaxPlayers, reparsed.MaxPlayers);
        Assert.Equal(parsed.PersistentBattlefield, reparsed.PersistentBattlefield);
        Assert.Equal(parsed.BattlEye, reparsed.BattlEye);
    }

    [Fact]
    public void SerializeServerConfig_UpdatesValues_InExistingAst()
    {
        var raw = """
            hostname = "Old Name";
            maxPlayers = 32;
            """;

        var parsed = _sut.ParseServerConfig(raw);
        parsed.Hostname = "New Name";
        parsed.MaxPlayers = 64;

        var serialized = _sut.SerializeServerConfig(parsed);

        Assert.Contains("\"New Name\"", serialized);
        Assert.Contains("64", serialized);
        Assert.DoesNotContain("\"Old Name\"", serialized);
    }

    [Fact]
    public void SerializeServerConfig_EmptyAst_CreatesDefaultHeaders()
    {
        var data = new ServerConfigData
        {
            Hostname = "Fresh Server",
            MaxPlayers = 32,
            VoteThreshold = 33,
            VoteMissionPlayers = 1,
            MotdInterval = 5
        };

        var serialized = _sut.SerializeServerConfig(data);

        Assert.Contains("// KAST Server Configuration", serialized);
        Assert.Contains("\"Fresh Server\"", serialized);
    }

    [Fact]
    public void SerializeServerConfig_MotdArray_SerializedCorrectly()
    {
        var data = new ServerConfigData
        {
            Motd = "Line 1\nLine 2",
            MaxPlayers = 32,
            VoteThreshold = 33,
            VoteMissionPlayers = 1,
            MotdInterval = 5
        };

        var serialized = _sut.SerializeServerConfig(data);

        Assert.Contains("motd[]", serialized);
        Assert.Contains("\"Line 1\"", serialized);
        Assert.Contains("\"Line 2\"", serialized);
    }

    // ── Basic Config Parsing ──

    [Fact]
    public void ParseBasicConfig_AllFields_ParsedCorrectly()
    {
        var raw = """
            MaxMsgSend = 256;
            MaxSizeGuaranteed = 1024;
            MaxSizeNonguaranteed = 512;
            MinBandwidth = 262144;
            MaxBandwidth = 20000000000;
            MinErrorToSend = 0.002;
            MinErrorToSendNear = 0.02;
            MaxCustomFileSize = 1048576;
            terrainGrid = 25;
            """;

        var result = _sut.ParseBasicConfig(raw);

        Assert.Equal(256, result.MaxMsgSend);
        Assert.Equal(1024, result.MaxSizeGuaranteed);
        Assert.Equal(512, result.MaxSizeNonguaranteed);
        Assert.Equal(262144, result.MinBandwidth);
        Assert.Equal(20000000000, result.MaxBandwidth);
        Assert.Equal(0.002, result.MinErrorToSend);
        Assert.Equal(0.02, result.MinErrorToSendNear);
        Assert.Equal(1048576, result.MaxCustomFileSize);
        Assert.Equal(25, result.TerrainGrid);
    }

    [Fact]
    public void ParseBasicConfig_Defaults_UsedWhenFieldsMissing()
    {
        var raw = """
            MaxMsgSend = 128;
            """;

        var result = _sut.ParseBasicConfig(raw);

        Assert.Equal(128, result.MaxMsgSend);
        Assert.Equal(512, result.MaxSizeGuaranteed); // default
        Assert.Equal(256, result.MaxSizeNonguaranteed); // default
    }

    [Fact]
    public void SerializeBasicConfig_RoundTrip_PreservesValues()
    {
        var raw = """
            MaxMsgSend = 256;
            MaxSizeGuaranteed = 1024;
            MinBandwidth = 262144;
            MaxBandwidth = 20000000000;
            MinErrorToSend = 0.002;
            MinErrorToSendNear = 0.02;
            MaxSizeNonguaranteed = 512;
            MaxCustomFileSize = 0;
            terrainGrid = 25;
            """;

        var parsed = _sut.ParseBasicConfig(raw);
        var serialized = _sut.SerializeBasicConfig(parsed);
        var reparsed = _sut.ParseBasicConfig(serialized);

        Assert.Equal(parsed.MaxMsgSend, reparsed.MaxMsgSend);
        Assert.Equal(parsed.MaxSizeGuaranteed, reparsed.MaxSizeGuaranteed);
        Assert.Equal(parsed.MinBandwidth, reparsed.MinBandwidth);
        Assert.Equal(parsed.MaxBandwidth, reparsed.MaxBandwidth);
        Assert.Equal(parsed.MinErrorToSend, reparsed.MinErrorToSend);
    }

    // ── Edge cases ──

    [Fact]
    public void ParseServerConfig_EmptyContent_ReturnsDefaults()
    {
        var result = _sut.ParseServerConfig("");

        Assert.Null(result.Hostname);
        Assert.Equal(32, result.MaxPlayers); // default
        // Empty string splits to one empty line → one WhitespaceNode
        Assert.Single(result.Ast);
        Assert.IsType<WhitespaceNode>(result.Ast[0]);
    }

    [Fact]
    public void ParseServerConfig_UnrecognizedLines_PreservedInAst()
    {
        var raw = """
            hostname = "Test";
            some random garbage line
            password = "pass";
            """;

        var result = _sut.ParseServerConfig(raw);

        Assert.Equal("Test", result.Hostname);
        Assert.Equal("pass", result.Password);
        // Unrecognized line preserved as WhitespaceNode
        Assert.Equal(3, result.Ast.Count);
    }

    [Fact]
    public void ParseServerConfig_ClassWithBraceOnSameLine_ParsedCorrectly()
    {
        var raw = """
            class Missions {
                class Mission1 {
                    template = "test.Altis";
                };
            };
            """;

        var result = _sut.ParseServerConfig(raw);

        var outerClass = Assert.IsType<ClassNode>(result.Ast[0]);
        Assert.Equal("Missions", outerClass.Name);

        var innerClass = Assert.IsType<ClassNode>(outerClass.Children[0]);
        Assert.Equal("Mission1", innerClass.Name);
    }

    [Fact]
    public void ParseServerConfig_ArrayWithInlineComment_CapturesComment()
    {
        var raw = """
            motd[] = {"Hello", "World"}; // Server motd
            """;

        var result = _sut.ParseServerConfig(raw);

        var arr = Assert.IsType<ArrayNode>(result.Ast[0]);
        Assert.Equal("motd", arr.Key);
        Assert.Equal(2, arr.Values.Count);
        Assert.Equal("Server motd", arr.Comment);
    }
}
