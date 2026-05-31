using KAST.Core.Enums;
using KAST.Core.Interfaces;
using KAST.Core.Models;

namespace KAST.Infrastructure.Services.Content;

/// <summary>
/// Installs a local mod: extracts a ZIP or validates an existing folder.
/// </summary>
public class LocalModInstaller(IFileSystemService fs, IOutputSanitizer sanitizer) : IContentInstaller
{
    public ContentType Type => ContentType.LocalMod;

    public IReadOnlyList<ContentStep> PlanSteps(ContentInstallRequest request)
    {
        bool isZip = request.SourcePath?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true;
        var steps = new List<ContentStep>();

        if (isZip)
            steps.Add(new ContentStep { Name = "Extract archive", Detail = $"{sanitizer.ToDisplayPath(request.SourcePath)} → {sanitizer.ToDisplayPath(request.DestinationPath)}" });

        steps.Add(new ContentStep { Name = "Calculate size", Detail = sanitizer.ToDisplayPath(request.DestinationPath) });

        return steps;
    }

    public async Task InstallAsync(ContentInstallRequest request, ContentInstallState state, CancellationToken ct)
    {
        bool isZip = request.SourcePath?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true;
        int stepIdx = 0;

        if (isZip)
        {
            state.BeginStep(stepIdx);
            state.AddLog($"Extracting {sanitizer.ToDisplayPath(request.SourcePath)} to {sanitizer.ToDisplayPath(request.DestinationPath)}");

            var currentStep = stepIdx;
            var progress = new Progress<double>(pct => state.SetStepProgress(currentStep, pct));
            await fs.ExtractZipAsync(request.SourcePath!, request.DestinationPath, progress, ct);

            state.CompleteStep(currentStep);
            state.AddLog("Extraction complete.");
            stepIdx++;
        }

        state.BeginStep(stepIdx);
        state.AddLog($"Calculating size on disk for {sanitizer.ToDisplayPath(request.DestinationPath)}...");

        long size = fs.GetDirectorySize(request.DestinationPath);
        state.AddLog($"Size: {size / (1024.0 * 1024.0):F1} MiB");

        state.CompleteStep(stepIdx);
    }

    public IReadOnlyList<ContentValidationResult> Validate(ContentInstallRequest request)
    {
        var results = new List<ContentValidationResult>();
        bool exists = Directory.Exists(request.DestinationPath);
        var displayPath = sanitizer.ToDisplayPath(request.DestinationPath);
        results.Add(new("Mod directory", exists, exists ? displayPath : $"Missing: {displayPath}"));

        if (!exists) return results;

        long size = fs.GetDirectorySize(request.DestinationPath);
        results.Add(new("Files on disk", size > 0, $"{size / (1024.0 * 1024.0):F1} MiB"));

        return results;
    }
}
