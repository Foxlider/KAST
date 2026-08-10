using System.Text.RegularExpressions;

namespace KAST.Tests;

public class UiPathSanitizationGuardTests
{
    private static readonly Regex PathFieldReferenceRegex = new(
    @"@[^\r\n]*(\.InstallPath|\.LocalPath|\.ModsDirectory|\.ServersDirectory)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void RazorMarkup_PathFieldsMustUseOutputSanitizer()
    {
        var repoRoot = FindRepoRoot();
        var componentsRoot = Path.Combine(repoRoot, "src", "KAST.UI", "Components");

        Assert.True(Directory.Exists(componentsRoot), $"Components folder not found: {componentsRoot}");

        var violations = new List<string>();

        foreach (var filePath in Directory.EnumerateFiles(componentsRoot, "*.razor", SearchOption.AllDirectories))
        {
            var fileText = File.ReadAllText(filePath);
            var markupOnly = GetMarkupSection(fileText);
            var lines = markupOnly.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!PathFieldReferenceRegex.IsMatch(line))
                    continue;

                if (line.Contains("IsNullOrWhiteSpace(", StringComparison.Ordinal))
                    continue;

                if (line.Contains("OutputSanitizer.", StringComparison.Ordinal))
                    continue;

                var relativePath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');
                violations.Add($"{relativePath}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Unsanitized path field usage detected in Razor markup. Wrap path display in OutputSanitizer.\n"
            + string.Join("\n", violations));
    }

    private static string GetMarkupSection(string razorFileContent)
    {
        var codeBlockIndex = razorFileContent.IndexOf("@code", StringComparison.Ordinal);
        return codeBlockIndex >= 0 ? razorFileContent[..codeBlockIndex] : razorFileContent;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "KAST.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing KAST.slnx");
    }
}
