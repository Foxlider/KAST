using KAST.Infrastructure.Services;

namespace KAST.Tests;

public class OutputSanitizerTests
{
    [Theory]
    [InlineData("Failed to read C:\\servers\\arma\\server.cfg", "server.cfg")]
    [InlineData("Failed to read C:/servers/arma/server.cfg", "server.cfg")]
    [InlineData("Failed to read \\\\nas01\\share\\server.cfg", "server.cfg")]
    [InlineData("Failed to read /home/kast/servers/server.cfg", "server.cfg")]
    [InlineData("Failed to read ~/kast/servers/server.cfg", "server.cfg")]
    [InlineData("Failed to read file:///C:/servers/arma/server.cfg", "server.cfg")]
    public void Sanitize_StripsPathRootsAndKeepsRelativeContext(string message, string expectedRelativePath)
    {
        var sanitizer = new OutputSanitizer();

        var sanitized = sanitizer.Sanitize(message);

        Assert.Contains(expectedRelativePath, sanitized);
        Assert.DoesNotContain("servers/arma", sanitized);
        Assert.DoesNotContain("share/", sanitized);
        Assert.DoesNotContain("<path>", sanitized);
        AssertNoAbsoluteRoot(sanitized);
    }

    [Fact]
    public void ToDisplayPath_StripsRootAndPreservesKnownFolderSuffix()
    {
        var sanitizer = new OutputSanitizer("D:/games/KAST");

        var displayPath = sanitizer.ToDisplayPath("D:\\games\\KAST\\servers\\alpha\\arma3server_x64.exe");

        Assert.Equal("servers/alpha/arma3server_x64.exe", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    [Fact]
    public void Sanitize_RewritesWindowsStackTracePathToRelativeFile()
    {
        var sanitizer = new OutputSanitizer();
        var message = "   at KAST.Service.Run() in C:\\src\\KAST\\Service.cs:line 42";

        var sanitized = sanitizer.Sanitize(message);

        Assert.Contains("in Service.cs:line 42", sanitized);
        AssertNoAbsoluteRoot(sanitized);
    }

    [Fact]
    public void SanitizeException_UsesTypeAndSanitizedMessageOnly()
    {
        var sanitizer = new OutputSanitizer();
        var exception = new InvalidOperationException("Could not access C:\\servers\\arma\\server.cfg");

        var sanitized = sanitizer.SanitizeException(exception);

        Assert.StartsWith("InvalidOperationException:", sanitized);
        Assert.Contains("server.cfg", sanitized);
        Assert.DoesNotContain("servers/arma", sanitized);
        AssertNoAbsoluteRoot(sanitized);
    }

    [Fact]
    public void ToDisplayPath_StripsRootsForConfiguredKastFolders()
    {
        var sanitizer = new OutputSanitizer(
            new OutputSanitizer.VirtualPathRoot("C:/apps/KAST", "KAST"),
            new OutputSanitizer.VirtualPathRoot("C:/apps/KAST/.KAST_DATA/mods", "mods"),
            new OutputSanitizer.VirtualPathRoot("D:/server/Arma3/KAST/server", "server"));

        Assert.Equal("KAST/README.md", sanitizer.ToDisplayPath("C:/apps/KAST/README.md"));
        Assert.Equal("mods/@ace/addons/ace_main.pbo", sanitizer.ToDisplayPath("C:/apps/KAST/.KAST_DATA/mods/@ace/addons/ace_main.pbo"));
        Assert.Equal("server/Server1/arma3server_x64.exe", sanitizer.ToDisplayPath("D:/server/Arma3/KAST/server/Server1/arma3server_x64.exe"));
    }

    [Fact]
    public void ToDisplayPath_UnknownAbsolutePathOnlyShowsSafeFileName()
    {
        var sanitizer = new OutputSanitizer("C:/apps/KAST/.KAST_DATA/mods");

        var displayPath = sanitizer.ToDisplayPath("E:/random/user/private/secret.cfg");

        Assert.Equal("secret.cfg", displayPath);
        Assert.DoesNotContain("random", displayPath);
        Assert.DoesNotContain("private", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    [Fact]
    public void ToDisplayPath_VirtualRootPreventsTraversalEscapeDisclosure()
    {
        var sanitizer = new OutputSanitizer(new OutputSanitizer.VirtualPathRoot("C:/apps/KAST/.KAST_DATA/mods", "mods"));

        var displayPath = sanitizer.ToDisplayPath("C:/apps/KAST/.KAST_DATA/mods/../../private/token.txt");

        Assert.Equal("token.txt", displayPath);
        Assert.DoesNotContain("private", displayPath);
        Assert.DoesNotContain("..", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    [Theory]
    [InlineData("../secrets/config.json", "secrets/config.json")]
    [InlineData("mods/@ace/../@cba/addons/cba_main.pbo", "mods/@cba/addons/cba_main.pbo")]
    public void ToDisplayPath_NormalizesRelativeTraversalWithoutLeakingParentFolders(string input, string expected)
    {
        var sanitizer = new OutputSanitizer(
            "C:/apps/KAST",
            "C:/apps/KAST/.KAST_DATA/mods",
            "D:/server/Arma3/KAST/server");

        var displayPath = sanitizer.ToDisplayPath(input);

        Assert.Equal(expected, displayPath);
        Assert.DoesNotContain("..", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    [Fact]
    public void Sanitize_RewritesConfiguredKastFolderPaths()
    {
        var sanitizer = new OutputSanitizer(
            new OutputSanitizer.VirtualPathRoot("C:/apps/KAST", "KAST"),
            new OutputSanitizer.VirtualPathRoot("C:/apps/KAST/.KAST_DATA/mods", "mods"),
            new OutputSanitizer.VirtualPathRoot("D:/server/Arma3/KAST/server", "server"));
        var message = "Read C:/apps/KAST/README.md, C:/apps/KAST/.KAST_DATA/mods/@ace/addons/ace_main.pbo and D:/server/Arma3/KAST/server/Server1/arma3server_x64.exe";

        var sanitized = sanitizer.Sanitize(message);

        Assert.Contains("KAST/README.md", sanitized);
        Assert.Contains("mods/@ace/addons/ace_main.pbo", sanitized);
        Assert.Contains("Server1/arma3server_x64.exe", sanitized);
        Assert.DoesNotContain("Arma3/KAST/server", sanitized);
        AssertNoAbsoluteRoot(sanitized);
    }

    [Fact]
    public void ToDisplayPath_SymlinkTargetInsideVirtualRootsKeepsLexicalDisplayPath()
    {
        var sanitizer = new OutputSanitizer(
            path => path.Equals("D:/server/Arma3/KAST/server/Server1/mods/@ace/addons/ace_main.pbo", StringComparison.OrdinalIgnoreCase)
                ? "C:/apps/KAST/.KAST_DATA/mods/@ace/addons/ace_main.pbo"
                : null,
            new OutputSanitizer.VirtualPathRoot("C:/apps/KAST/.KAST_DATA/mods", "mods"),
            new OutputSanitizer.VirtualPathRoot("D:/server/Arma3/KAST/server", "server"));

        var displayPath = sanitizer.ToDisplayPath("D:/server/Arma3/KAST/server/Server1/mods/@ace/addons/ace_main.pbo");

        Assert.Equal("server/Server1/mods/@ace/addons/ace_main.pbo", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    [Fact]
    public void ToDisplayPath_ExternalSymlinkTargetOutsideVirtualRootsFallsBackToFileName()
    {
        var sanitizer = new OutputSanitizer(
            path => path.Equals("D:/server/Arma3/KAST/server/Server1/mods/@evil/token.txt", StringComparison.OrdinalIgnoreCase)
                ? "C:/Users/Admin/secrets/token.txt"
                : null,
            new OutputSanitizer.VirtualPathRoot("C:/apps/KAST/.KAST_DATA/mods", "mods"),
            new OutputSanitizer.VirtualPathRoot("D:/server/Arma3/KAST/server", "server"));

        var displayPath = sanitizer.ToDisplayPath("D:/server/Arma3/KAST/server/Server1/mods/@evil/token.txt");

        Assert.Equal("token.txt", displayPath);
        Assert.DoesNotContain("Users", displayPath);
        Assert.DoesNotContain("secrets", displayPath);
        Assert.DoesNotContain("server/Server1", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    [Fact]
    public void ToDisplayPath_UnresolvableSymlinkUsesLexicalVirtualRootDisplayPath()
    {
        var sanitizer = new OutputSanitizer(
            _ => throw new IOException("broken link"),
            new OutputSanitizer.VirtualPathRoot("D:/server/Arma3/KAST/server", "server"));

        var displayPath = sanitizer.ToDisplayPath("D:/server/Arma3/KAST/server/Server1/@ace");

        Assert.Equal("server/Server1/@ace", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    [Fact]
    public void ToDisplayPath_VirtualRootPrefixCollisionDoesNotMatch()
    {
        var sanitizer = new OutputSanitizer(new OutputSanitizer.VirtualPathRoot("C:/apps/KAST", "KAST"));

        var displayPath = sanitizer.ToDisplayPath("C:/apps/KAST_backup/private/secret.txt");

        Assert.Equal("secret.txt", displayPath);
        Assert.DoesNotContain("KAST_backup", displayPath);
        Assert.DoesNotContain("private", displayPath);
        AssertNoAbsoluteRoot(displayPath);
    }

    private static void AssertNoAbsoluteRoot(string value)
    {
        Assert.DoesNotContain("C:\\", value);
        Assert.DoesNotContain("D:\\", value);
        Assert.DoesNotContain("C:/", value);
        Assert.DoesNotContain("D:/", value);
        Assert.DoesNotContain("\\\\nas01", value);
        Assert.DoesNotContain("/home/", value);
        Assert.DoesNotContain("~/", value);
        Assert.DoesNotContain("file:///", value);
    }
}
